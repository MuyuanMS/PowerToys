// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using ManagedCommon;
using Microsoft.PowerToys.Settings.UI.Helpers;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Settings.UI.Library.Helpers;
using Microsoft.PowerToys.Settings.UI.Library.Interfaces;
using Microsoft.PowerToys.Settings.UI.SerializationContext;
using Windows.Media.Ocr;
using Windows.Security.Credentials;

namespace Microsoft.PowerToys.Settings.UI.ViewModels
{
    public partial class ScreenTranslatorViewModel : PageViewModelBase
    {
        public const string AzureCredentialResource = "https://api.cognitive.microsofttranslator.com";

        public const string AzureCredentialUsername = "PowerToys_ScreenTranslator_AzureTranslator";
        public const string AzureVisionCredentialResource = "https://*.cognitiveservices.azure.com";
        public const string AzureVisionCredentialUsername = "PowerToys_ScreenTranslator_AzureVision";

        public const string LibreTranslateCredentialResource = "https://libretranslate.com";

        public const string LibreTranslateCredentialUsername = "PowerToys_ScreenTranslator_LibreTranslate";

        protected override string ModuleName => ScreenTranslatorSettings.ModuleName;

        private bool _disposed;

        private const int SaveSettingsDelayInMs = 500;

        private GeneralSettings GeneralSettingsConfig { get; set; }

        private readonly SettingsUtils _settingsUtils;

        private readonly System.Threading.Lock _delayedActionLock = new System.Threading.Lock();

        private readonly ScreenTranslatorSettings _screenTranslatorSettings;

        private Timer _delayedTimer;

        private bool _enabledStateIsGPOConfigured;

        private bool _isEnabled;

        private Func<string, int> SendConfigMSG { get; }

        public static readonly string[] ProviderKeys = ["Passthrough", "AzureTranslator", "LibreTranslate", "AcpAgent"];

        private IReadOnlyList<ScreenTranslatorLanguageOption> _translationLanguages =
            ScreenTranslatorLanguageCatalog.CreateSystemFallback();

        private System.Threading.CancellationTokenSource _languageCatalogCancellation;

        public ObservableCollection<string> AvailableProviders { get; } = new ObservableCollection<string>
        {
            "Passthrough (Offline Prototype)",
            "Azure AI Translator (Cloud v3)",
            "LibreTranslate (Self-Hosted / Cloud)",
            "ACP Agent (Experimental)",
        };

        public ObservableCollection<string> AvailableOcrProviders { get; } = new ObservableCollection<string>
        {
            "Automatic (Windows AI, then Windows OCR)",
            "Windows.Media.Ocr (Local)",
            "Azure AI Vision OCR (Cloud)",
        };

        public ObservableCollection<ScreenTranslatorLanguageOption> AvailableSourceLanguages { get; } = new();

        public ObservableCollection<ScreenTranslatorLanguageOption> AvailableTargetLanguages { get; } = new();

        public ScreenTranslatorViewModel(
            SettingsUtils settingsUtils,
            ISettingsRepository<GeneralSettings> settingsRepository,
            ISettingsRepository<ScreenTranslatorSettings> screenTranslatorSettingsRepository,
            Func<string, int> ipcMSGCallBackFunc)
        {
            ArgumentNullException.ThrowIfNull(settingsRepository);
            GeneralSettingsConfig = settingsRepository.SettingsConfig;

            _settingsUtils = settingsUtils ?? throw new ArgumentNullException(nameof(settingsUtils));
            ArgumentNullException.ThrowIfNull(screenTranslatorSettingsRepository);
            _screenTranslatorSettings = screenTranslatorSettingsRepository.SettingsConfig ?? new ScreenTranslatorSettings();

            InitializeEnabledValue();
            InitializeLanguageLists();

            SendConfigMSG = ipcMSGCallBackFunc;

            _delayedTimer = new Timer();
            _delayedTimer.Interval = SaveSettingsDelayInMs;
            _delayedTimer.Elapsed += DelayedTimer_Tick;
            _delayedTimer.AutoReset = false;
        }

        private void InitializeLanguageLists()
        {
            ApplyLanguageCatalog(_translationLanguages);
        }

        public async Task RefreshLanguageCatalogAsync()
        {
            _languageCatalogCancellation?.Cancel();
            _languageCatalogCancellation?.Dispose();
            _languageCatalogCancellation = new System.Threading.CancellationTokenSource();
            System.Threading.CancellationToken cancellationToken = _languageCatalogCancellation.Token;
            try
            {
                IReadOnlyList<ScreenTranslatorLanguageOption> languages =
                    await ScreenTranslatorLanguageCatalog.GetTranslationLanguagesAsync(
                        _screenTranslatorSettings.Properties.SelectedProvider,
                        _screenTranslatorSettings.Properties.AzureEndpoint,
                        _screenTranslatorSettings.Properties.LibreTranslateEndpoint,
                        cancellationToken: cancellationToken);
                if (!cancellationToken.IsCancellationRequested)
                {
                    _translationLanguages = languages;
                    ApplyLanguageCatalog(languages);
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        private void ApplyLanguageCatalog(IReadOnlyList<ScreenTranslatorLanguageOption> languages)
        {
            AvailableTargetLanguages.Clear();
            foreach (ScreenTranslatorLanguageOption language in languages)
            {
                AvailableTargetLanguages.Add(language);
            }

            AvailableSourceLanguages.Clear();
            bool cloudOcrEnabled = IsAzureVisionOcrSelected;
            IEnumerable<string> installedOcrLanguages = OcrEngine.AvailableRecognizerLanguages
                .Select(language => language.LanguageTag);
            foreach (ScreenTranslatorLanguageOption language in ScreenTranslatorLanguageCatalog.CreateSourceOptions(
                languages,
                cloudOcrEnabled,
                installedOcrLanguages))
            {
                AvailableSourceLanguages.Add(language);
            }

            OnPropertyChanged(nameof(SourceLanguageIndex));
            OnPropertyChanged(nameof(TargetLanguageIndex));
            OnPropertyChanged(nameof(SecondaryTargetLanguageIndex));
        }

        private void InitializeEnabledValue()
        {
            _enabledStateIsGPOConfigured = false;
            _isEnabled = GeneralSettingsConfig.Enabled.ScreenTranslator;
        }

        public void RefreshEnabledState()
        {
            InitializeEnabledValue();
            OnPropertyChanged(nameof(IsEnabled));
        }

        public override Dictionary<string, HotkeySettings[]> GetAllHotkeySettings()
        {
            return new Dictionary<string, HotkeySettings[]>
            {
                [ModuleName] = [ActivationShortcut, CurrentScreenShortcut, ActiveWindowShortcut, ScanTextShortcut],
            };
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                if (_enabledStateIsGPOConfigured)
                {
                    return;
                }

                if (_isEnabled != value)
                {
                    _isEnabled = value;
                    OnPropertyChanged(nameof(IsEnabled));

                    GeneralSettingsConfig.Enabled.ScreenTranslator = value;
                    var outgoing = new OutGoingGeneralSettings(GeneralSettingsConfig);
                    SendConfigMSG(outgoing.ToString());
                }
            }
        }

        public bool IsWin11OrGreater => OSVersionHelper.IsWindows11();

        public bool IsEnabledGpoConfigured => _enabledStateIsGPOConfigured;

        public HotkeySettings ActivationShortcut
        {
            get => _screenTranslatorSettings.Properties.ActivationShortcut;
            set
            {
                if (_screenTranslatorSettings.Properties.ActivationShortcut != value)
                {
                    _screenTranslatorSettings.Properties.ActivationShortcut = value ?? _screenTranslatorSettings.Properties.DefaultActivationShortcut;
                    OnPropertyChanged(nameof(ActivationShortcut));
                    _settingsUtils.SaveSettings(_screenTranslatorSettings.ToJsonString(), ScreenTranslatorSettings.ModuleName);
                    NotifySettingsChanged();
                }
            }
        }

        public HotkeySettings CurrentScreenShortcut
        {
            get => _screenTranslatorSettings.Properties.CurrentScreenShortcut;
            set
            {
                if (_screenTranslatorSettings.Properties.CurrentScreenShortcut != value)
                {
                    _screenTranslatorSettings.Properties.CurrentScreenShortcut = value ?? _screenTranslatorSettings.Properties.DefaultCurrentScreenShortcut;
                    OnPropertyChanged(nameof(CurrentScreenShortcut));
                    _settingsUtils.SaveSettings(_screenTranslatorSettings.ToJsonString(), ScreenTranslatorSettings.ModuleName);
                    NotifySettingsChanged();
                }
            }
        }

        public HotkeySettings ActiveWindowShortcut
        {
            get => _screenTranslatorSettings.Properties.ActiveWindowShortcut;
            set
            {
                if (_screenTranslatorSettings.Properties.ActiveWindowShortcut != value)
                {
                    _screenTranslatorSettings.Properties.ActiveWindowShortcut = value ?? _screenTranslatorSettings.Properties.DefaultActiveWindowShortcut;
                    OnPropertyChanged(nameof(ActiveWindowShortcut));
                    _settingsUtils.SaveSettings(_screenTranslatorSettings.ToJsonString(), ScreenTranslatorSettings.ModuleName);
                    NotifySettingsChanged();
                }
            }
        }

        public HotkeySettings ScanTextShortcut
        {
            get => _screenTranslatorSettings.Properties.ScanTextShortcut;
            set
            {
                if (_screenTranslatorSettings.Properties.ScanTextShortcut != value)
                {
                    _screenTranslatorSettings.Properties.ScanTextShortcut = value ?? _screenTranslatorSettings.Properties.DefaultScanTextShortcut;
                    OnPropertyChanged(nameof(ScanTextShortcut));
                    _settingsUtils.SaveSettings(_screenTranslatorSettings.ToJsonString(), ScreenTranslatorSettings.ModuleName);
                    NotifySettingsChanged();
                }
            }
        }

        public int SelectedProviderIndex
        {
            get
            {
                string provider = _screenTranslatorSettings.Properties.SelectedProvider;
                for (int i = 0; i < ProviderKeys.Length; i++)
                {
                    if (string.Equals(ProviderKeys[i], provider, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }

                return 0;
            }

            set
            {
                if (value >= 0 && value < ProviderKeys.Length)
                {
                    string selected = ProviderKeys[value];
                    if (!string.Equals(_screenTranslatorSettings.Properties.SelectedProvider, selected, StringComparison.Ordinal))
                    {
                        _screenTranslatorSettings.Properties.SelectedProvider = selected;
                        OnPropertyChanged(nameof(SelectedProviderIndex));
                        OnPropertyChanged(nameof(IsAzureProviderSelected));
                        OnPropertyChanged(nameof(IsLibreTranslateProviderSelected));
                        OnPropertyChanged(nameof(IsAcpAgentProviderSelected));
                        SaveAndNotifySettings();
                        _ = RefreshLanguageCatalogAsync();
                    }
                }
            }
        }

        public bool IsAzureProviderSelected => SelectedProviderIndex == 1;

        public int SelectedOcrProviderIndex
        {
            get => _screenTranslatorSettings.Properties.OcrProvider switch
            {
                "WindowsMedia" => 1,
                "AzureVision" => 2,
                _ => 0,
            };
            set
            {
                string selected = value switch
                {
                    1 => "WindowsMedia",
                    2 => "AzureVision",
                    _ => "Automatic",
                };
                if (!string.Equals(_screenTranslatorSettings.Properties.OcrProvider, selected, StringComparison.Ordinal))
                {
                    _screenTranslatorSettings.Properties.OcrProvider = selected;
                    OnPropertyChanged(nameof(SelectedOcrProviderIndex));
                    OnPropertyChanged(nameof(IsAzureVisionOcrSelected));
                    SaveAndNotifySettings();
                    ApplyLanguageCatalog(_translationLanguages);
                }
            }
        }

        public bool IsAzureVisionOcrSelected => SelectedOcrProviderIndex == 2;

        public bool IsLibreTranslateProviderSelected => SelectedProviderIndex == 2;

        public bool IsAcpAgentProviderSelected => SelectedProviderIndex == 3;

        public string AcpAgentCommand
        {
            get => _screenTranslatorSettings.Properties.AcpAgentCommand;
            set
            {
                if (_screenTranslatorSettings.Properties.AcpAgentCommand != value)
                {
                    _screenTranslatorSettings.Properties.AcpAgentCommand = value;
                    OnPropertyChanged(nameof(AcpAgentCommand));
                    SaveAndNotifySettings();
                }
            }
        }

        public bool FreezeCapturedContent
        {
            get => _screenTranslatorSettings.Properties.FreezeCapturedContent;
            set
            {
                if (_screenTranslatorSettings.Properties.FreezeCapturedContent != value)
                {
                    _screenTranslatorSettings.Properties.FreezeCapturedContent = value;
                    OnPropertyChanged(nameof(FreezeCapturedContent));
                    SaveAndNotifySettings();
                }
            }
        }

        public int SourceLanguageIndex
        {
            get
            {
                string code = _screenTranslatorSettings.Properties.SourceLanguage;
                for (int i = 0; i < AvailableSourceLanguages.Count; i++)
                {
                    if (ScreenTranslatorLanguageCatalog.AreEquivalentLanguageTags(AvailableSourceLanguages[i].Code, code))
                    {
                        return i;
                    }
                }

                return 0;
            }

            set
            {
                if (value >= 0 && value < AvailableSourceLanguages.Count && !AvailableSourceLanguages[value].IsEnabled)
                {
                    OnPropertyChanged(nameof(SourceLanguageIndex));
                    return;
                }

                if (value >= 0 && value < AvailableSourceLanguages.Count)
                {
                    string code = AvailableSourceLanguages[value].Code;
                    if (!string.Equals(_screenTranslatorSettings.Properties.SourceLanguage, code, StringComparison.Ordinal))
                    {
                        _screenTranslatorSettings.Properties.SourceLanguage = code;
                        OnPropertyChanged(nameof(SourceLanguageIndex));
                        SaveAndNotifySettings();
                    }
                }
            }
        }

        public int TargetLanguageIndex
        {
            get
            {
                string code = _screenTranslatorSettings.Properties.TargetLanguage;
                for (int i = 0; i < AvailableTargetLanguages.Count; i++)
                {
                    if (ScreenTranslatorLanguageCatalog.AreEquivalentLanguageTags(AvailableTargetLanguages[i].Code, code))
                    {
                        return i;
                    }
                }

                return 0;
            }

            set
            {
                if (value >= 0 && value < AvailableTargetLanguages.Count)
                {
                    string code = AvailableTargetLanguages[value].Code;
                    if (!string.Equals(_screenTranslatorSettings.Properties.TargetLanguage, code, StringComparison.Ordinal))
                    {
                        _screenTranslatorSettings.Properties.TargetLanguage = code;
                        OnPropertyChanged(nameof(TargetLanguageIndex));
                        SaveAndNotifySettings();
                    }
                }
            }
        }

        public int SecondaryTargetLanguageIndex
        {
            get
            {
                string code = _screenTranslatorSettings.Properties.SecondaryTargetLanguage;
                for (int i = 0; i < AvailableTargetLanguages.Count; i++)
                {
                    if (ScreenTranslatorLanguageCatalog.AreEquivalentLanguageTags(AvailableTargetLanguages[i].Code, code))
                    {
                        return i;
                    }
                }

                return FindLanguageIndex(AvailableTargetLanguages, "zh-Hans");
            }

            set
            {
                if (value >= 0 && value < AvailableTargetLanguages.Count)
                {
                    string code = AvailableTargetLanguages[value].Code;
                    if (!string.Equals(_screenTranslatorSettings.Properties.SecondaryTargetLanguage, code, StringComparison.Ordinal))
                    {
                        _screenTranslatorSettings.Properties.SecondaryTargetLanguage = code;
                        OnPropertyChanged(nameof(SecondaryTargetLanguageIndex));
                        SaveAndNotifySettings();
                    }
                }
            }
        }

        private static int FindLanguageIndex(
            IReadOnlyList<ScreenTranslatorLanguageOption> languages,
            string languageCode)
        {
            for (int index = 0; index < languages.Count; index++)
            {
                if (ScreenTranslatorLanguageCatalog.AreEquivalentLanguageTags(
                    languages[index].Code,
                    languageCode))
                {
                    return index;
                }
            }

            return 0;
        }

        public string AzureEndpoint
        {
            get => _screenTranslatorSettings.Properties.AzureEndpoint;
            set
            {
                if (_screenTranslatorSettings.Properties.AzureEndpoint != value)
                {
                    _screenTranslatorSettings.Properties.AzureEndpoint = value;
                    OnPropertyChanged(nameof(AzureEndpoint));
                    SaveAndNotifySettings();
                }
            }
        }

        public string AzureRegion
        {
            get => _screenTranslatorSettings.Properties.AzureRegion;
            set
            {
                if (_screenTranslatorSettings.Properties.AzureRegion != value)
                {
                    _screenTranslatorSettings.Properties.AzureRegion = value;
                    OnPropertyChanged(nameof(AzureRegion));
                    SaveAndNotifySettings();
                }
            }
        }

        public string AzureVisionEndpoint
        {
            get => _screenTranslatorSettings.Properties.AzureVisionEndpoint;
            set
            {
                if (_screenTranslatorSettings.Properties.AzureVisionEndpoint != value)
                {
                    _screenTranslatorSettings.Properties.AzureVisionEndpoint = value;
                    OnPropertyChanged(nameof(AzureVisionEndpoint));
                    SaveAndNotifySettings();
                }
            }
        }

        public string LibreTranslateEndpoint
        {
            get => _screenTranslatorSettings.Properties.LibreTranslateEndpoint;
            set
            {
                if (_screenTranslatorSettings.Properties.LibreTranslateEndpoint != value)
                {
                    _screenTranslatorSettings.Properties.LibreTranslateEndpoint = value;
                    OnPropertyChanged(nameof(LibreTranslateEndpoint));
                    SaveAndNotifySettings();
                }
            }
        }

        public bool HasAzureApiKey => !string.IsNullOrWhiteSpace(RetrieveCredential(AzureCredentialResource, AzureCredentialUsername));

        public bool HasAzureVisionApiKey => !string.IsNullOrWhiteSpace(RetrieveCredential(AzureVisionCredentialResource, AzureVisionCredentialUsername));

        public bool HasLibreTranslateApiKey => !string.IsNullOrWhiteSpace(RetrieveCredential(LibreTranslateCredentialResource, LibreTranslateCredentialUsername));

        public async Task<(bool Success, string Message)> TestAzureTranslatorConnectionAsync()
        {
            string apiKey = RetrieveCredential(AzureCredentialResource, AzureCredentialUsername);
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                return (false, "Save an Azure subscription key first.");
            }

            string endpoint = string.IsNullOrWhiteSpace(AzureEndpoint)
                ? AzureCredentialResource
                : AzureEndpoint.Trim().TrimEnd('/');
            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out Uri endpointUri) ||
                endpointUri.Scheme != Uri.UriSchemeHttps)
            {
                return (false, "Enter a valid HTTPS Azure Translator endpoint.");
            }

            try
            {
                using var client = new HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(15),
                };
                using var request = new HttpRequestMessage(
                    HttpMethod.Post,
                    new Uri(endpointUri, "/translate?api-version=3.0&to=es"))
                {
                    Content = new StringContent("[{\"Text\":\"Hello\"}]", Encoding.UTF8, "application/json"),
                };
                request.Headers.Add("Ocp-Apim-Subscription-Key", apiKey);
                if (!string.IsNullOrWhiteSpace(AzureRegion))
                {
                    request.Headers.Add("Ocp-Apim-Subscription-Region", AzureRegion.Trim());
                }

                using HttpResponseMessage response = await client.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    return (true, "Azure Translator responded successfully.");
                }

                string responseBody = await response.Content.ReadAsStringAsync();
                return (
                    false,
                    $"Azure returned HTTP {(int)response.StatusCode}: {GetAzureErrorMessage(responseBody)}");
            }
            catch (TaskCanceledException)
            {
                return (false, "Azure Translator did not respond within 15 seconds.");
            }
            catch (Exception ex)
            {
                Logger.LogError($"Azure Translator connection test failed: {ex.Message}");
                return (false, $"Connection test failed: {ex.Message}");
            }
        }

        public bool SaveAzureApiKey(string key)
        {
            var saved = SaveCredential(AzureCredentialResource, AzureCredentialUsername, key);
            OnPropertyChanged(nameof(HasAzureApiKey));
            return saved;
        }

        public void RemoveAzureApiKey()
        {
            RemoveCredential(AzureCredentialResource, AzureCredentialUsername);
            OnPropertyChanged(nameof(HasAzureApiKey));
        }

        public bool SaveAzureVisionApiKey(string key)
        {
            var saved = SaveCredential(AzureVisionCredentialResource, AzureVisionCredentialUsername, key);
            OnPropertyChanged(nameof(HasAzureVisionApiKey));
            return saved;
        }

        public void RemoveAzureVisionApiKey()
        {
            RemoveCredential(AzureVisionCredentialResource, AzureVisionCredentialUsername);
            OnPropertyChanged(nameof(HasAzureVisionApiKey));
        }

        public bool SaveLibreTranslateApiKey(string key)
        {
            var saved = SaveCredential(LibreTranslateCredentialResource, LibreTranslateCredentialUsername, key);
            OnPropertyChanged(nameof(HasLibreTranslateApiKey));
            return saved;
        }

        public void RemoveLibreTranslateApiKey()
        {
            RemoveCredential(LibreTranslateCredentialResource, LibreTranslateCredentialUsername);
            OnPropertyChanged(nameof(HasLibreTranslateApiKey));
        }

        private static string RetrieveCredential(string resource, string username)
        {
            try
            {
                var vault = new PasswordVault();
                var cred = vault.Retrieve(resource, username);
                cred?.RetrievePassword();
                return cred?.Password?.Trim() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool SaveCredential(string resource, string username, string secret)
        {
            if (string.IsNullOrWhiteSpace(secret))
            {
                return false;
            }

            try
            {
                var vault = new PasswordVault();
                RemoveCredential(resource, username);
                var cred = new PasswordCredential(resource, username, secret.Trim());
                vault.Add(cred);

                return !string.IsNullOrWhiteSpace(RetrieveCredential(resource, username));
            }
            catch (Exception ex)
            {
                Logger.LogError($"Failed to save credential in PasswordVault: {ex.Message}");
                return false;
            }
        }

        private static string GetAzureErrorMessage(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
            {
                return "No error details were returned.";
            }

            try
            {
                using JsonDocument document = JsonDocument.Parse(responseBody);
                if (document.RootElement.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("message", out JsonElement message))
                {
                    return message.GetString() ?? "Unknown Azure error.";
                }
            }
            catch (JsonException)
            {
            }

            return responseBody.Length > 200 ? string.Concat(responseBody.AsSpan(0, 200), "...") : responseBody;
        }

        private static void RemoveCredential(string resource, string username)
        {
            try
            {
                var vault = new PasswordVault();
                var cred = vault.Retrieve(resource, username);
                if (cred != null)
                {
                    vault.Remove(cred);
                }
            }
            catch
            {
                // Credential doesn't exist, which is fine
            }
        }

        private void SaveAndNotifySettings()
        {
            _settingsUtils.SaveSettings(_screenTranslatorSettings.ToJsonString(), ScreenTranslatorSettings.ModuleName);
            ScheduleSavingOfSettings();
        }

        private void ScheduleSavingOfSettings()
        {
            lock (_delayedActionLock)
            {
                if (_delayedTimer.Enabled)
                {
                    _delayedTimer.Stop();
                }

                _delayedTimer.Start();
            }
        }

        private void DelayedTimer_Tick(object sender, ElapsedEventArgs e)
        {
            lock (_delayedActionLock)
            {
                _delayedTimer.Stop();
                NotifySettingsChanged();
            }
        }

        private void NotifySettingsChanged()
        {
            try
            {
                // Using InvariantCulture as this is an IPC message
                SendConfigMSG(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{{ \"powertoys\": {{ \"{0}\": {1} }} }}",
                        ScreenTranslatorSettings.ModuleName,
                        JsonSerializer.Serialize(_screenTranslatorSettings, SourceGenerationContextContext.Default.ScreenTranslatorSettings)));
            }
            catch (Exception ex)
            {
                Logger.LogWarning($"Failed to notify settings changed: {ex.Message}");
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _delayedTimer?.Dispose();
                    _languageCatalogCancellation?.Cancel();
                    _languageCatalogCancellation?.Dispose();
                }

                _disposed = true;
            }

            base.Dispose(disposing);
        }
    }
}
