// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

namespace Microsoft.PowerToys.Settings.UI
{
    internal sealed class DeferredWindowActivation
    {
        private readonly Action _activatePreparedWindow;
        private readonly Action _startFallbackTimer;
        private readonly Action _stopFallbackTimer;
        private readonly Action _subscribeInitialContentLoaded;
        private readonly Action _unsubscribeInitialContentLoaded;
        private readonly Action _closeHiddenWindow;
        private bool _closed;
        private bool _bringToForegroundOnActivation;

        public DeferredWindowActivation(
            Action activatePreparedWindow,
            Action startFallbackTimer,
            Action stopFallbackTimer,
            Action subscribeInitialContentLoaded,
            Action unsubscribeInitialContentLoaded,
            Action closeHiddenWindow)
        {
            _activatePreparedWindow = activatePreparedWindow ?? throw new ArgumentNullException(nameof(activatePreparedWindow));
            _startFallbackTimer = startFallbackTimer ?? throw new ArgumentNullException(nameof(startFallbackTimer));
            _stopFallbackTimer = stopFallbackTimer ?? throw new ArgumentNullException(nameof(stopFallbackTimer));
            _subscribeInitialContentLoaded = subscribeInitialContentLoaded ?? throw new ArgumentNullException(nameof(subscribeInitialContentLoaded));
            _unsubscribeInitialContentLoaded = unsubscribeInitialContentLoaded ?? throw new ArgumentNullException(nameof(unsubscribeInitialContentLoaded));
            _closeHiddenWindow = closeHiddenWindow ?? throw new ArgumentNullException(nameof(closeHiddenWindow));
        }

        public bool ActivationPending { get; private set; }

        public void RequestActivation(bool canActivateImmediately, bool isInitialContentLoaded, bool bringToForeground)
        {
            _bringToForegroundOnActivation |= bringToForeground;

            if (canActivateImmediately || isInitialContentLoaded)
            {
                ActivatePreparedWindow();
                return;
            }

            _unsubscribeInitialContentLoaded();
            _subscribeInitialContentLoaded();
            ActivationPending = true;
            _startFallbackTimer();
        }

        public void CloseHiddenWindow(bool isWindowVisible)
        {
            if (!isWindowVisible && !ActivationPending)
            {
                _closeHiddenWindow();
            }
        }

        public void OnInitialContentLoaded()
        {
            ActivatePreparedWindow();
        }

        public void OnFallbackTimer()
        {
            ActivatePreparedWindow();
        }

        public void OnWindowClosed()
        {
            _closed = true;
            ActivationPending = false;
            _unsubscribeInitialContentLoaded();
            _stopFallbackTimer();
        }

        public bool ConsumeBringToForeground()
        {
            if (!_bringToForegroundOnActivation)
            {
                return false;
            }

            _bringToForegroundOnActivation = false;
            return true;
        }

        private void ActivatePreparedWindow()
        {
            if (_closed)
            {
                return;
            }

            _unsubscribeInitialContentLoaded();
            _stopFallbackTimer();
            ActivationPending = false;
            _activatePreparedWindow();
        }
    }
}
