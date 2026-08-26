// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;

using global::PowerToys.GPOWrapper;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public partial class FileConverterViewModel : Observable
    {
        private readonly GeneralSettings _generalSettingsConfig;

        private readonly Func<string, int> _sendConfigMessage;

        private bool _isEnabled;

        private bool _enabledStateIsGpoConfigured;

        public FileConverterViewModel(
            SettingsUtils settingsUtils,
            ISettingsRepository<GeneralSettings> settingsRepository,
            Func<string, int> ipcMessageCallback)
        {
            ArgumentNullException.ThrowIfNull(settingsUtils);
            ArgumentNullException.ThrowIfNull(settingsRepository);
            ArgumentNullException.ThrowIfNull(ipcMessageCallback);

            _generalSettingsConfig = settingsRepository.SettingsConfig;
            _sendConfigMessage = ipcMessageCallback;

            InitializeEnabledValue();
        }

        public bool IsEnabledGpoConfigured => _enabledStateIsGpoConfigured;

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_enabledStateIsGpoConfigured)
                {
                    return;
                }

                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    _generalSettingsConfig.Enabled.FileConverter = value;

                    OutGoingGeneralSettings outgoing = new(_generalSettingsConfig);
                    _sendConfigMessage(outgoing.ToString());

                    OnPropertyChanged(nameof(IsEnabled));
                }
            }
        }

        public void RefreshEnabledState()
        {
            InitializeEnabledValue();
            OnPropertyChanged(nameof(IsEnabled));
            OnPropertyChanged(nameof(IsEnabledGpoConfigured));
        }

        private void InitializeEnabledValue()
        {
            var gpoConfiguration = GPOWrapper.GetConfiguredFileConverterEnabledValue();
            _enabledStateIsGpoConfigured =
                gpoConfiguration == GpoRuleConfigured.Disabled ||
                gpoConfiguration == GpoRuleConfigured.Enabled;
            _isEnabled = _enabledStateIsGpoConfigured ?
                gpoConfiguration == GpoRuleConfigured.Enabled :
                _generalSettingsConfig.Enabled.FileConverter;
        }
    }
}
