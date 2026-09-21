// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Helpers;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Microsoft.PowerToys.Settings.UI.Views
{
    public sealed partial class ScreenTranslatorPage : NavigablePage, IRefreshablePage
    {
        private ScreenTranslatorViewModel ViewModel { get; set; }

        public ScreenTranslatorPage()
        {
            var settingsUtils = SettingsUtils.Default;
            ViewModel = new ScreenTranslatorViewModel(
                settingsUtils,
                SettingsRepository<GeneralSettings>.GetInstance(settingsUtils),
                SettingsRepository<ScreenTranslatorSettings>.GetInstance(settingsUtils),
                ShellPage.SendDefaultIPCMessage);
            DataContext = ViewModel;
            InitializeComponent();
            Loaded += (s, e) =>
            {
                ViewModel.OnPageLoaded();
                ClearApiKeyInputs();
                RefreshApiKeyStatus();
            };
        }

        private void ClearApiKeyInputs()
        {
            if (AzureApiKeyPasswordBox != null)
            {
                AzureApiKeyPasswordBox.Password = string.Empty;
            }

            if (LibreTranslateApiKeyPasswordBox != null)
            {
                LibreTranslateApiKeyPasswordBox.Password = string.Empty;
            }

            if (AzureVisionApiKeyPasswordBox != null)
            {
                AzureVisionApiKeyPasswordBox.Password = string.Empty;
            }
        }

        private void SaveAzureApiKey_Click(object sender, RoutedEventArgs e)
        {
            if (AzureApiKeyPasswordBox != null)
            {
                if (string.IsNullOrWhiteSpace(AzureApiKeyPasswordBox.Password))
                {
                    ShowExistingOrMissingCredential(AzureApiKeyStatusInfoBar, ViewModel.HasAzureApiKey);
                    return;
                }

                if (ViewModel.SaveAzureApiKey(AzureApiKeyPasswordBox.Password))
                {
                    AzureApiKeyPasswordBox.Password = string.Empty;
                    ShowCredentialSaved(AzureApiKeyStatusInfoBar);
                }
                else
                {
                    ShowCredentialSaveError(AzureApiKeyStatusInfoBar);
                }
            }
        }

        private void ClearAzureApiKey_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RemoveAzureApiKey();
            if (AzureApiKeyPasswordBox != null)
            {
                AzureApiKeyPasswordBox.Password = string.Empty;
            }

            AzureApiKeyStatusInfoBar.IsOpen = false;
            AzureConnectionStatusInfoBar.IsOpen = false;
        }

        private async void TestAzureConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            TestAzureConnectionButton.IsEnabled = false;
            AzureConnectionStatusInfoBar.Severity = InfoBarSeverity.Informational;
            AzureConnectionStatusInfoBar.Title = "Testing Azure Translator";
            AzureConnectionStatusInfoBar.Message = "Sending a small translation request...";
            AzureConnectionStatusInfoBar.IsOpen = true;

            var result = await ViewModel.TestAzureTranslatorConnectionAsync();
            AzureConnectionStatusInfoBar.Severity = result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error;
            AzureConnectionStatusInfoBar.Title = result.Success ? "Connection successful" : "Connection failed";
            AzureConnectionStatusInfoBar.Message = result.Message;
            TestAzureConnectionButton.IsEnabled = true;
        }

        private void ViewAzureUsageButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://portal.azure.com/#browse/Microsoft.CognitiveServices%2Faccounts",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Logger.LogError($"Unable to open Azure usage in the browser: {ex.Message}");
                AzureConnectionStatusInfoBar.Severity = InfoBarSeverity.Error;
                AzureConnectionStatusInfoBar.Title = "Could not open Azure portal";
                AzureConnectionStatusInfoBar.Message = "Open portal.azure.com and select your Translator resource, then Monitoring > Metrics.";
                AzureConnectionStatusInfoBar.IsOpen = true;
            }
        }

        private void SaveAzureVisionApiKey_Click(object sender, RoutedEventArgs e)
        {
            if (AzureVisionApiKeyPasswordBox != null)
            {
                if (string.IsNullOrWhiteSpace(AzureVisionApiKeyPasswordBox.Password))
                {
                    ShowExistingOrMissingCredential(AzureVisionApiKeyStatusInfoBar, ViewModel.HasAzureVisionApiKey);
                    return;
                }

                if (ViewModel.SaveAzureVisionApiKey(AzureVisionApiKeyPasswordBox.Password))
                {
                    AzureVisionApiKeyPasswordBox.Password = string.Empty;
                    ShowCredentialSaved(AzureVisionApiKeyStatusInfoBar);
                }
                else
                {
                    ShowCredentialSaveError(AzureVisionApiKeyStatusInfoBar);
                }
            }
        }

        private void ClearAzureVisionApiKey_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RemoveAzureVisionApiKey();
            if (AzureVisionApiKeyPasswordBox != null)
            {
                AzureVisionApiKeyPasswordBox.Password = string.Empty;
            }

            AzureVisionApiKeyStatusInfoBar.IsOpen = false;
        }

        private void SaveLibreTranslateApiKey_Click(object sender, RoutedEventArgs e)
        {
            if (LibreTranslateApiKeyPasswordBox != null)
            {
                if (string.IsNullOrWhiteSpace(LibreTranslateApiKeyPasswordBox.Password))
                {
                    ShowExistingOrMissingCredential(LibreTranslateApiKeyStatusInfoBar, ViewModel.HasLibreTranslateApiKey);
                    return;
                }

                if (ViewModel.SaveLibreTranslateApiKey(LibreTranslateApiKeyPasswordBox.Password))
                {
                    LibreTranslateApiKeyPasswordBox.Password = string.Empty;
                    ShowCredentialSaved(LibreTranslateApiKeyStatusInfoBar);
                }
                else
                {
                    ShowCredentialSaveError(LibreTranslateApiKeyStatusInfoBar);
                }
            }
        }

        private void ClearLibreTranslateApiKey_Click(object sender, RoutedEventArgs e)
        {
            ViewModel.RemoveLibreTranslateApiKey();
            if (LibreTranslateApiKeyPasswordBox != null)
            {
                LibreTranslateApiKeyPasswordBox.Password = string.Empty;
            }

            LibreTranslateApiKeyStatusInfoBar.IsOpen = false;
        }

        public void RefreshEnabledState()
        {
            ViewModel.RefreshEnabledState();
            ClearApiKeyInputs();
            RefreshApiKeyStatus();
        }

        public static Visibility BoolToVisibility(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

        private void RefreshApiKeyStatus()
        {
            SetCredentialStatus(AzureApiKeyStatusInfoBar, ViewModel.HasAzureApiKey);
            SetCredentialStatus(AzureVisionApiKeyStatusInfoBar, ViewModel.HasAzureVisionApiKey);
            SetCredentialStatus(LibreTranslateApiKeyStatusInfoBar, ViewModel.HasLibreTranslateApiKey);
        }

        private static void SetCredentialStatus(InfoBar infoBar, bool isSaved)
        {
            infoBar.IsOpen = isSaved;
            if (isSaved)
            {
                ShowCredentialSaved(infoBar);
            }
        }

        private static void ShowCredentialSaved(InfoBar infoBar)
        {
            infoBar.Severity = InfoBarSeverity.Success;
            infoBar.Title = "Saved securely";
            infoBar.Message = "The key is stored in Windows Credential Vault.";
            infoBar.IsOpen = true;
        }

        private static void ShowCredentialSaveError(InfoBar infoBar)
        {
            infoBar.Severity = InfoBarSeverity.Error;
            infoBar.Title = "Key was not saved";
            infoBar.Message = "The value remains in the field. Try saving again.";
            infoBar.IsOpen = true;
        }

        private static void ShowExistingOrMissingCredential(InfoBar infoBar, bool isSaved)
        {
            if (isSaved)
            {
                ShowCredentialSaved(infoBar);
                return;
            }

            infoBar.Severity = InfoBarSeverity.Warning;
            infoBar.Title = "Enter a key";
            infoBar.Message = "Paste a subscription key before selecting Save.";
            infoBar.IsOpen = true;
        }
    }
}
