// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using ScreenTranslator.Core.Actions;
using Windows.ApplicationModel.DataTransfer;
using WinRT;

namespace ScreenTranslator.Helpers;

internal sealed class TextShareService
{
    private static readonly Guid DataTransferManagerIid = new("a5caee9b-8708-49d1-8d36-67d25a8da00c");

    private DataTransferManager? _dataTransferManager;
    private string _text = string.Empty;
    private string _title = string.Empty;

    public void Show(IntPtr hwnd, string text, string title)
    {
        _text = text;
        _title = title;

        IDataTransferManagerInterop interop = DataTransferManager.As<IDataTransferManagerInterop>();
        if (_dataTransferManager is null)
        {
            Guid iid = DataTransferManagerIid;
            IntPtr dataTransferManagerPointer = interop.GetForWindow(hwnd, ref iid);
            _dataTransferManager = MarshalInterface<DataTransferManager>.FromAbi(dataTransferManagerPointer);
            _dataTransferManager.DataRequested += DataTransferManager_DataRequested;
        }

        interop.ShowShareUIForWindow(hwnd);
    }

    public void Close()
    {
        if (_dataTransferManager is not null)
        {
            _dataTransferManager.DataRequested -= DataTransferManager_DataRequested;
            _dataTransferManager = null;
        }
    }

    private void DataTransferManager_DataRequested(DataTransferManager sender, DataRequestedEventArgs args)
    {
        args.Request.Data.Properties.Title = _title;
        if (CardActionHelper.TryGetWebUri(_text, out Uri? uri))
        {
            args.Request.Data.SetWebLink(uri);
        }
        else
        {
            args.Request.Data.SetText(_text);
        }
    }

    [ComImport]
    [Guid("3A3DCD6C-3EAB-43DC-BCDE-45671CE800C8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDataTransferManagerInterop
    {
        IntPtr GetForWindow(IntPtr appWindow, ref Guid riid);

        void ShowShareUIForWindow(IntPtr appWindow);
    }
}
