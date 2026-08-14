// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.PowerToys.Telemetry;
using MouseWithoutBorders.Class;

// <summary>
//     Drag/Drop implementation.
// </summary>
// <history>
//     2008 created by Truong Do (ductdo).
//     2009-... modified by Truong Do (TruongDo).
//     2023- Included in PowerToys.
// </history>
namespace MouseWithoutBorders.Core;

/* Common.DragDrop.cs
 * Drag&Drop is one complicated implementation of the tool with some tricks.
 *
 * SEQUENCE OF EVENTS:
 * DragDropStep01: MachineX: Remember mouse down state since it could be a start of a dragging
 * DragDropStep02: MachineY: Send a message to the MachineX to ask it to check if it is
 *                           doing drag/drop
 * DragDropStep03: MachineX: Got explorerDragDrop, send WM_CHECK_EXPLORER_DRAG_DROP to its mainForm
 * DragDropStep04: MachineX: Show Mouse Without Borders Helper form at mouse cursor to get DragEnter event.
 * DragDropStepXX: MachineX: Mouse Without Borders Helper: Called by DragEnter, check if dragging a single file,
 *                           remember the file (set as its window caption)
 * DragDropStep05: MachineX: Get the file name from Mouse Without Borders Helper, hide Mouse Without Borders Helper window
 * DragDropStep06: MachineX: Broadcast a message saying that it has some drag file.
 * DragDropStep08: MachineY: Got ClipboardDragDrop, isDropping set, get the MachineX name from the package.
 * DragDropStep09: MachineY: Since isDropping is true, show up the drop form (looks like an icon).
 * DragDropStep10: MachineY: MouseUp, set isDropping to false, hide the drop "icon" and get data.
 * DragDropStep11: MachineX: Mouse move back without drop event, cancelling drag/dop
 *                           SendClipboardBeatDragDropEnd
 * DragDropStep12: MachineY: Hide the drop "icon" when received ClipboardDragDropEnd.
 *
 * FROM VERSION 1.6.3: Drag/Drop is temporary removed, Drop action cannot be done from a lower integrity app to a higher one.
 * We have to run a helper process...
 * http://forums.microsoft.com/MSDN/ShowPost.aspx?PageIndex=1&SiteID=1&PageID=1&PostID=736086
 *
 * 2008.10.28: Trying to restore the Drag/Drop feature by adding the drag/drop helper process. Coming in version
 * 1.6.5
 * */

internal static class DragDrop
{
    private static readonly object DragActivationLock = new();
    private static readonly object DragNetworkQueueLock = new();
    private static Task dragNetworkQueue = Task.CompletedTask;
    private static bool isDragging;
    private static volatile bool mouseDown;
    private static long transientDragValidationGeneration;
    private static bool dragActivationNetworkInProgress;
    private static bool dragActivationReleaseRequested;
    private static ID dragActivationReleaseDestination;

    internal static bool IsDragging
    {
        get
        {
            lock (DragActivationLock)
            {
                return isDragging;
            }
        }

        set
        {
            lock (DragActivationLock)
            {
                isDragging = value;
            }
        }
    }

    internal static void DragDropStep01(int wParam)
    {
        if (!Setting.Values.TransferFile)
        {
            return;
        }

        if (wParam == WM.WM_LBUTTONDOWN)
        {
            lock (DragActivationLock)
            {
                transientDragValidationGeneration = Clipboard.BeginTransientDragFileValidation();
                MouseDown = true;
                DragMachine = MachineStuff.desMachineID;
                MachineStuff.dropMachineID = ID.NONE;
            }

            Logger.LogDebug("DragDropStep01: MouseDown");
        }
        else if (wParam == WM.WM_LBUTTONUP)
        {
            lock (DragActivationLock)
            {
                MouseDown = false;
            }

            Logger.LogDebug("DragDropStep01: MouseUp");
        }

        if (wParam == WM.WM_RBUTTONUP && IsDropping)
        {
            IsDropping = false;
            Clipboard.LastIDWithClipboardData = ID.NONE;
        }
    }

    internal static void DragDropStep02()
    {
        if (MachineStuff.desMachineID == Common.MachineID)
        {
            Logger.LogDebug("DragDropStep02: SendCheckExplorerDragDrop sent to myself");
            Common.DoSomethingInUIThread(() =>
            {
                _ = NativeMethods.PostMessage(Common.MainForm.Handle, NativeMethods.WM_CHECK_EXPLORER_DRAG_DROP, (IntPtr)0, (IntPtr)0);
            });
        }
        else
        {
            SendCheckExplorerDragDrop();
            Logger.LogDebug("DragDropStep02: SendCheckExplorerDragDrop sent");
        }
    }

    internal static void DragDropStep03(DATA package)
    {
        if (Common.RunOnLogonDesktop || Common.RunOnScrSaverDesktop)
        {
            return;
        }

        if (package.Des == Common.MachineID || package.Des == ID.ALL)
        {
            Logger.LogDebug("DragDropStep03: ExplorerDragDrop Received.");
            MachineStuff.dropMachineID = package.Src; // Drop machine is the machine that sent ExplorerDragDrop
            if (MouseDown || IsDropping)
            {
                Logger.LogDebug("DragDropStep03: Mouse is down, check if dragging...sending WM_CHECK_EXPLORER_DRAG_DROP to myself...");
                Common.DoSomethingInUIThread(() =>
                {
                    _ = NativeMethods.PostMessage(Common.MainForm.Handle, NativeMethods.WM_CHECK_EXPLORER_DRAG_DROP, (IntPtr)0, (IntPtr)0);
                });
            }
        }
    }

    private static int dragDropStep05ExCalledByIpc;

    internal static void DragDropStep04()
    {
        if (!IsDropping)
        {
            IntPtr h = (IntPtr)NativeMethods.FindWindow(null, Helper.HELPER_FORM_TEXT);
            if (h.ToInt32() > 0)
            {
                _ = Interlocked.Exchange(ref dragDropStep05ExCalledByIpc, 0);
                long validationGeneration;
                lock (DragActivationLock)
                {
                    validationGeneration = transientDragValidationGeneration;
                }

                _ = Helper.SendMessageToHelper(
                    SharedConst.SET_DRAG_VALIDATION_GENERATION_CMD,
                    checked((IntPtr)validationGeneration),
                    IntPtr.Zero);

                Common.MainForm.Hide();
                Common.MainFormVisible = false;

                Point p = default;

                // NativeMethods.SetWindowText(h, "");
                _ = NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0, NativeMethods.SWP_SHOWWINDOW);

                for (int i = -10; i < 10; i++)
                {
                    if (dragDropStep05ExCalledByIpc > 0)
                    {
                        Logger.LogDebug("DragDropStep04: DragDropStep05ExCalledByIpc.");
                        break;
                    }

                    _ = NativeMethods.GetCursorPos(ref p);
                    Logger.LogDebug("DragDropStep04: Moving Mouse Without Borders Helper to (" + p.X.ToString(CultureInfo.CurrentCulture) + ", " + p.Y.ToString(CultureInfo.CurrentCulture) + ")");
                    _ = NativeMethods.SetWindowPos(h, NativeMethods.HWND_TOPMOST, p.X - 100 + i, p.Y - 100 + i, 200, 200, 0);
                    _ = NativeMethods.SendMessage(h, 0x000F, IntPtr.Zero, IntPtr.Zero); // WM_PAINT
                    Thread.Sleep(20);
                    Application.DoEvents();

                    // if (GetText(h).Length > 1) break;
                }
            }
            else
            {
                Logger.LogDebug("DragDropStep04: Mouse without Borders Helper not found!");
            }
        }
        else
        {
            Logger.LogDebug("DragDropStep04: IsDropping == true, skip checking");
        }

        Logger.LogDebug("DragDropStep04: Got WM_CHECK_EXPLORER_DRAG_DROP, done with processing jump to DragDropStep05...");
    }

    internal static void DragDropStep05Ex(string dragFileName, long validationGeneration)
    {
        Logger.LogDebug("DragDropStep05 called.");

        if (Common.RunOnLogonDesktop || Common.RunOnScrSaverDesktop)
        {
            return;
        }

        bool isCurrentValidation;
        lock (DragActivationLock)
        {
            isCurrentValidation = validationGeneration != 0
                && transientDragValidationGeneration == validationGeneration;
            if (isCurrentValidation)
            {
                transientDragValidationGeneration = 0;
            }
        }

        if (!isCurrentValidation)
        {
            Clipboard.CancelTransientDragFileValidation(validationGeneration);
            Logger.LogDebug("DragDropStep05: Ignoring a stale drag validation callback.");
            return;
        }

        _ = Interlocked.Exchange(ref dragDropStep05ExCalledByIpc, 1);

        if (!IsDropping)
        {
            if (!MouseDown)
            {
                Clipboard.CancelTransientDragFileValidation(validationGeneration);
                Logger.LogDebug("DragDropStep05: Drag ended before path validation started.");
                _ = NativeMethods.PostMessage(Common.MainForm.Handle, NativeMethods.WM_HIDE_DD_HELPER, (IntPtr)0, (IntPtr)0);
                return;
            }

            if (LocalPathLease.TryCreate(dragFileName, out LocalPathLease lease))
            {
                bool activated = false;
                ID dropMachineId = ID.NONE;
                lock (DragActivationLock)
                {
                    if (MouseDown
                        && !isDropping
                        && Clipboard.TrySetValidatedTransientDragFile(validationGeneration, dragFileName, lease))
                    {
                        /*
                         * possibleDropMachineID is used as desID sent in DragDropStep06();
                         * */
                        if (MachineStuff.dropMachineID == ID.NONE)
                        {
                            MachineStuff.dropMachineID = MachineStuff.newDesMachineID;
                        }

                        isDragging = true;
                        dragActivationNetworkInProgress = true;
                        dragActivationReleaseRequested = false;
                        dropMachineId = MachineStuff.dropMachineID;
                        activated = true;
                    }
                }

                if (activated)
                {
                    PublishDragActivation(dropMachineId);
                    Logger.LogDebug("DragDropStep05: File dragging: " + dragFileName);
                    _ = NativeMethods.PostMessage(Common.MainForm.Handle, NativeMethods.WM_HIDE_DD_HELPER, (IntPtr)1, (IntPtr)0);
                    Logger.LogDebug("DragDropStep05: WM_HIDE_DDHelper sent");
                }
                else
                {
                    lease.Dispose();
                    Logger.LogDebug("DragDropStep05: Drag ended before path validation completed.");
                    _ = NativeMethods.PostMessage(Common.MainForm.Handle, NativeMethods.WM_HIDE_DD_HELPER, (IntPtr)0, (IntPtr)0);
                }
            }
            else
            {
                Clipboard.CancelTransientDragFileValidation(validationGeneration);
                Logger.Log("DragDropStep05: Rejected non-local or unstable path: [" + dragFileName + "]");
                _ = NativeMethods.PostMessage(Common.MainForm.Handle, NativeMethods.WM_HIDE_DD_HELPER, (IntPtr)0, (IntPtr)0);
            }
        }
        else
        {
            ID dropMachineId;
            lock (DragActivationLock)
            {
                if (!isDropping)
                {
                    Logger.LogDebug("DragDropStep05: Drop state changed before callback processing.");
                    return;
                }

                Logger.LogDebug("DragDropStep05: IsDropping == true, change drop machine...");
                isDropping = false;
                dragActivationNetworkInProgress = true;
                dragActivationReleaseRequested = false;
                Common.MainFormVisible = true; // WM_HIDE_DRAG_DROP
                dropMachineId = MachineStuff.dropMachineID; // Set in DragDropStep03
            }

            PublishDropBegin(dropMachineId);
        }

        MouseDown = false;
    }

    private static void PublishDragActivation(ID dropMachineId)
    {
        PublishDragNetwork(() =>
        {
            Logger.LogDebug("DragDropStep06: SendClipboardBeatDragDrop");
            SendClipboardBeatDragDrop();
            SendDropBegin(dropMachineId);
        });
    }

    private static void PublishDropBegin(ID dropMachineId)
    {
        PublishDragNetwork(() => SendDropBegin(dropMachineId));
    }

    private static void PublishDragNetwork(Action publication)
    {
        QueueDragNetworkAction(() =>
        {
            try
            {
                publication();
            }
            finally
            {
                bool sendEnd;
                ID releaseDestination;
                lock (DragActivationLock)
                {
                    dragActivationNetworkInProgress = false;
                    sendEnd = dragActivationReleaseRequested;
                    releaseDestination = dragActivationReleaseDestination;
                    dragActivationReleaseRequested = false;
                    dragActivationReleaseDestination = ID.NONE;
                }

                if (sendEnd)
                {
                    SendClipboardBeatDragDropEnd(releaseDestination);
                }
            }
        });
    }

    internal static void DragDropStep08(DATA package)
    {
        Receiver.GetNameOfMachineWithClipboardData(package);
        Logger.LogDebug("DragDropStep08: ClipboardDragDrop Received. machine with drag file was set");
    }

    internal static void DragDropStep08_2(DATA package)
    {
        if (package.Des == Common.MachineID && !Common.RunOnLogonDesktop && !Common.RunOnScrSaverDesktop)
        {
            IsDropping = true;
            MachineStuff.dropMachineID = Common.MachineID;
            Logger.LogDebug("DragDropStep08_2: ClipboardDragDropOperation Received. IsDropping set");
        }
    }

    internal static void DragDropStep09(int wParam)
    {
        if (wParam == WM.WM_MOUSEMOVE && IsDropping)
        {
            // Show/Move form
            Common.DoSomethingInUIThread(() =>
            {
                _ = NativeMethods.PostMessage(Common.MainForm.Handle, NativeMethods.WM_SHOW_DRAG_DROP, (IntPtr)0, (IntPtr)0);
            });
        }
        else if (wParam == WM.WM_LBUTTONUP)
        {
            bool completeDrop;
            lock (DragActivationLock)
            {
                completeDrop = isDropping;
                if (completeDrop)
                {
                    isDropping = false;
                    isDragging = false;
                    Clipboard.LastIDWithClipboardData = ID.NONE;
                }
                else if (isDragging || dragActivationNetworkInProgress)
                {
                    isDragging = false;
                    Clipboard.LastIDWithClipboardData = ID.NONE;
                    if (dragActivationNetworkInProgress)
                    {
                        dragActivationReleaseRequested = true;
                        dragActivationReleaseDestination = MachineStuff.desMachineID;
                    }
                }

                Clipboard.RequestLastDragDropFileReleaseAfterSend();
            }

            if (completeDrop)
            {
                FinishDragDropStep10();
            }
        }
    }

    private static void FinishDragDropStep10()
    {
        Logger.LogDebug("DragDropStep10: Hide the form and get data...");
        Common.DoSomethingInUIThread(() =>
        {
            _ = NativeMethods.PostMessage(Common.MainForm.Handle, NativeMethods.WM_HIDE_DRAG_DROP, (IntPtr)0, (IntPtr)0);
        });

        PowerToysTelemetry.Log.WriteEvent(new MouseWithoutBorders.Telemetry.MouseWithoutBordersDragAndDropEvent());
        Clipboard.GetRemoteClipboard("desktop");
    }

    internal static void DragDropStep11()
    {
        Logger.LogDebug("DragDropStep11: Mouse drag coming back, canceling drag/drop");
        bool sendEnd;
        ID endDestination;
        lock (DragActivationLock)
        {
            long validationGeneration = transientDragValidationGeneration;
            transientDragValidationGeneration = 0;
            MouseDown = false;
            IsDropping = false;
            IsDragging = false;
            DragMachine = (ID)1;
            Clipboard.CancelTransientDragFileValidation(validationGeneration);
            Clipboard.LastIDWithClipboardData = ID.NONE;
            Clipboard.LastDragDropFile = null;
            sendEnd = !dragActivationNetworkInProgress;
            endDestination = MachineStuff.desMachineID;
            if (!sendEnd)
            {
                dragActivationReleaseRequested = true;
                dragActivationReleaseDestination = endDestination;
            }
        }

        if (sendEnd)
        {
            QueueDragNetworkAction(() => SendClipboardBeatDragDropEnd(endDestination));
        }
    }

    internal static void DragDropStep12()
    {
        Logger.LogDebug("DragDropStep12: ClipboardDragDropEnd received");
        IsDropping = false;
        Clipboard.LastIDWithClipboardData = ID.NONE;

        Common.DoSomethingInUIThread(() =>
        {
            _ = NativeMethods.PostMessage(Common.MainForm.Handle, NativeMethods.WM_HIDE_DRAG_DROP, (IntPtr)0, (IntPtr)0);
        });
    }

    private static void SendCheckExplorerDragDrop()
    {
        DATA package = new();
        package.Type = PackageType.ExplorerDragDrop;

        /*
         * package.src = newDesMachineID:
         * sent from the master machine but the src must be the
         * new des machine since the previous des machine will get this and set
         * to possibleDropMachineID in DragDropStep3()
         * */
        package.Src = MachineStuff.newDesMachineID;

        package.Des = MachineStuff.desMachineID;
        package.MachineName = Common.MachineName;

        Common.SkSend(package, null, false);
    }

    internal static void ChangeDropMachine()
    {
        bool sendEnd = false;
        bool sendBegin = false;
        ID endDestination = ID.NONE;
        ID beginDestination = ID.NONE;
        lock (DragActivationLock)
        {
            // desMachineID = current drop machine
            // newDesMachineID = new drop machine

            // 1. Cancelling dropping in current drop machine
            if (MachineStuff.dropMachineID == Common.MachineID)
            {
                // Drag/Drop coming through me
                IsDropping = false;
            }
            else
            {
                // Drag/Drop coming back
                sendEnd = true;
                endDestination = MachineStuff.desMachineID;
            }

            // 2. SendClipboardBeatDragDrop to new drop machine
            // new drop machine is not me
            if (MachineStuff.newDesMachineID != Common.MachineID)
            {
                MachineStuff.dropMachineID = MachineStuff.newDesMachineID;
                sendBegin = true;
                beginDestination = MachineStuff.dropMachineID;
                dragActivationNetworkInProgress = true;
                dragActivationReleaseRequested = false;
            }

            // New drop machine is me
            else
            {
                IsDropping = true;
            }
        }

        Action transition = () =>
        {
            if (sendEnd)
            {
                SendClipboardBeatDragDropEnd(endDestination);
            }

            if (sendBegin)
            {
                SendDropBegin(beginDestination);
            }
        };

        if (sendBegin)
        {
            PublishDragNetwork(transition);
        }
        else
        {
            QueueDragNetworkAction(transition);
        }
    }

    private static void SendClipboardBeatDragDrop()
    {
        Common.SendPackage(ID.ALL, PackageType.ClipboardDragDrop);
    }

    private static void SendDropBegin(ID dropMachineId)
    {
        Logger.LogDebug("SendDropBegin...");
        Common.SendPackage(dropMachineId, PackageType.ClipboardDragDropOperation);
    }

    private static void SendClipboardBeatDragDropEnd(ID destinationMachineId)
    {
        if (destinationMachineId != Common.MachineID)
        {
            Common.SendPackage(destinationMachineId, PackageType.ClipboardDragDropEnd);
        }
    }

    private static void QueueDragNetworkAction(Action action)
    {
        lock (DragNetworkQueueLock)
        {
            dragNetworkQueue = dragNetworkQueue.ContinueWith(
                _ =>
                {
                    try
                    {
                        action();
                    }
                    catch (Exception exception)
                    {
                        Logger.Log(exception);
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.None,
                TaskScheduler.Default);
        }
    }

    private static bool isDropping;
    private static ID dragMachine;

    internal static ID DragMachine
    {
        get => DragDrop.dragMachine;
        set => DragDrop.dragMachine = value;
    }

    internal static bool IsDropping
    {
        get
        {
            lock (DragActivationLock)
            {
                return isDropping;
            }
        }

        set
        {
            lock (DragActivationLock)
            {
                isDropping = value;
            }
        }
    }

    internal static bool MouseDown
    {
        get => mouseDown;
        set => mouseDown = value;
    }
}
