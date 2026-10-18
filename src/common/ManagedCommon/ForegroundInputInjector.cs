// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Threading;

namespace ManagedCommon
{
    /// <summary>
    /// Sends the synthetic input that lets SetForegroundWindow get past the foreground lock, without letting it hang the caller.
    /// </summary>
    /// <remarks>
    /// SendInput is a synchronous call into win32k that has been seen to never return from NtUserSendInput. Made inline on a
    /// UI thread, that hung PowerToys Settings on startup and left a process that could not be terminated. The input is sent
    /// from a background thread that the caller waits on for a bounded time, and no new send starts while one is still stuck.
    /// </remarks>
    internal sealed class ForegroundInputInjector
    {
        private readonly Action _sendInput;
        private readonly Action<Thread> _startThread;
        private int _sendInProgress;

        internal ForegroundInputInjector(Action sendInput)
            : this(sendInput, static thread => thread.Start())
        {
        }

        internal ForegroundInputInjector(Action sendInput, Action<Thread> startThread)
        {
            _sendInput = sendInput;
            _startThread = startThread;
        }

        internal bool IsSendInProgress => Volatile.Read(ref _sendInProgress) != 0;

        /// <summary>
        /// Sends the input and waits up to <paramref name="timeout"/> for it to complete.
        /// </summary>
        /// <returns><see langword="true"/> if the input was sent within <paramref name="timeout"/>.</returns>
        internal bool TrySend(TimeSpan timeout)
        {
            if (Interlocked.CompareExchange(ref _sendInProgress, 1, 0) != 0)
            {
                Logger.LogWarning("Skipping foreground input because a previous SendInput call has not returned.");
                return false;
            }

            try
            {
                var sendThread = new Thread(Send)
                {
                    IsBackground = true,
                    Name = "PowerToys foreground input",
                };
                _startThread(sendThread);
            }
            catch
            {
                Volatile.Write(ref _sendInProgress, 0);
                throw;
            }

            // Thread.Join and WaitHandle.WaitOne pump COM and sent messages on STA threads. Polling keeps the caller
            // (usually a UI thread) from being re-entered, like the inline SendInput call this replaces.
            var elapsed = Stopwatch.StartNew();
            var spinner = default(SpinWait);
            while (IsSendInProgress)
            {
                if (elapsed.Elapsed >= timeout)
                {
                    Logger.LogWarning($"SendInput did not return within {timeout.TotalMilliseconds} ms; continuing without it.");
                    return false;
                }

                spinner.SpinOnce();
            }

            return true;
        }

        private void Send()
        {
            try
            {
                _sendInput();
            }
            finally
            {
                Volatile.Write(ref _sendInProgress, 0);
            }
        }
    }
}
