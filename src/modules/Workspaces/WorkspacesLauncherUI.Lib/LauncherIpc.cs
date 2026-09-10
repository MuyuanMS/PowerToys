// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace WorkspacesLauncherUI
{
    internal static class LauncherIpc
    {
        public static Action<string> MessageReceivedCallback { get; set; }

        public static Action CancelAcknowledgedCallback { get; set; }

        public static Action<string> SendMessageCallback { get; set; }

        public static void SendMessage(string message)
        {
            SendMessageCallback?.Invoke(message);
        }

        public static void ProcessMessage(string message, Action<Action> enqueue, Action stopReadyTimer)
        {
            if (message == "cancel_ack")
            {
                enqueue(() => CancelAcknowledgedCallback?.Invoke());
                return;
            }

            Action<string> callback = MessageReceivedCallback;
            if (callback != null && message.Length > 0)
            {
                enqueue(() =>
                {
                    stopReadyTimer();
                    callback(message);
                });
            }
        }
    }
}
