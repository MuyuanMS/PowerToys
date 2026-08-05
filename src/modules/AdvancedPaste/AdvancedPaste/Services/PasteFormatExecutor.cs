// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using AdvancedPaste.Helpers;
using AdvancedPaste.Models;
using AdvancedPaste.Services.CustomActions;
using AdvancedPaste.Settings;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.PowerToys.Telemetry;
using Windows.ApplicationModel.DataTransfer;

namespace AdvancedPaste.Services;

public sealed class PasteFormatExecutor(IKernelService kernelService, ICustomActionTransformService customActionTransformService, IUserSettings userSettings) : IPasteFormatExecutor
{
    private readonly IKernelService _kernelService = kernelService;
    private readonly ICustomActionTransformService _customActionTransformService = customActionTransformService;
    private readonly IUserSettings _userSettings = userSettings;

    public async Task<DataPackage> ExecutePasteFormatAsync(PasteFormat pasteFormat, PasteActionSource source, CancellationToken cancellationToken, IProgress<double> progress)
    {
        if (!pasteFormat.IsEnabled)
        {
            return null;
        }

        if (PasteFormat.MetadataDict[pasteFormat.Format].RequiresAIService
            && !IsProviderAllowedByGPO(pasteFormat.ProviderId))
        {
            throw new PasteActionException(
                ResourceLoaderInstance.ResourceLoader.GetString("PasteError"),
                new InvalidOperationException("The selected AI provider is disabled by policy."));
        }

        var format = pasteFormat.Format;

        WriteTelemetry(format, source);

        var clipboardData = Clipboard.GetContent();

        // Run on thread-pool; although we use Async routines consistently, some actions still occasionally take a long time without yielding.
        return await Task.Run(async () =>
            pasteFormat.Format switch
            {
                PasteFormats.KernelQuery => await _kernelService.TransformClipboardAsync(pasteFormat.Prompt, clipboardData, pasteFormat.IsSavedQuery, cancellationToken, progress, pasteFormat.ProviderId),
                PasteFormats.CustomTextTransformation => DataPackageHelpers.CreateFromText((await _customActionTransformService.TransformAsync(pasteFormat.Prompt, await clipboardData.GetTextOrHtmlTextAsync(), await clipboardData.GetImageAsPngBytesAsync(), cancellationToken, progress, providerIdOverride: pasteFormat.ProviderId))?.Content ?? string.Empty),
                PasteFormats.FixSpellingAndGrammar => DataPackageHelpers.CreateFromText((await _customActionTransformService.TransformAsync(GetFixSpellingPrompt(), await clipboardData.GetTextOrHtmlTextAsync(), null, cancellationToken, progress, GetFixSpellingSystemPrompt(), pasteFormat.ProviderId))?.Content ?? string.Empty),
                _ => await TransformHelpers.TransformAsync(format, clipboardData, cancellationToken, progress),
            });
    }

    private static void WriteTelemetry(PasteFormats format, PasteActionSource source)
    {
        switch (source)
        {
            case PasteActionSource.ContextMenu:
                PowerToysTelemetry.Log.WriteEvent(new Telemetry.AdvancedPasteFormatClickedEvent(format));
                break;

            case PasteActionSource.InAppKeyboardShortcut:
                PowerToysTelemetry.Log.WriteEvent(new Telemetry.AdvancedPasteInAppKeyboardShortcutEvent(format));
                break;

            case PasteActionSource.GlobalKeyboardShortcut:
            case PasteActionSource.PromptBox:
                break; // no telemetry yet for these sources

            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    private string GetFixSpellingPrompt()
    {
        var customPrompt = _userSettings.FixSpellingAndGrammarPrompt;
        return string.IsNullOrWhiteSpace(customPrompt) ? AdvancedPasteDefaultPrompts.FixSpellingAndGrammar : customPrompt;
    }

    private string GetFixSpellingSystemPrompt()
    {
        var customSystemPrompt = _userSettings.FixSpellingAndGrammarSystemPrompt;
        return string.IsNullOrWhiteSpace(customSystemPrompt) ? AdvancedPasteDefaultPrompts.FixSpellingAndGrammarSystem : customSystemPrompt;
    }

    private bool IsProviderAllowedByGPO(string providerId)
    {
        var configuration = _userSettings.PasteAIConfiguration;
        var provider = !string.IsNullOrWhiteSpace(providerId)
            ? configuration?.Providers?.FirstOrDefault(item => string.Equals(item.Id, providerId, StringComparison.OrdinalIgnoreCase))
            : null;
        provider ??= configuration?.ActiveProvider ?? configuration?.Providers?.FirstOrDefault();

        if (provider is null)
        {
            return true;
        }

        var serviceType = provider.ServiceTypeKind == AIServiceType.Unknown
            ? AIServiceType.OpenAI
            : provider.ServiceTypeKind;
        if (AIServiceTypeRegistry.GetMetadata(serviceType).IsOnlineService
            && PowerToys.GPOWrapper.GPOWrapper.GetAllowedAdvancedPasteOnlineAIModelsValue() == PowerToys.GPOWrapper.GpoRuleConfigured.Disabled)
        {
            return false;
        }

        return serviceType switch
        {
            AIServiceType.OpenAI => PowerToys.GPOWrapper.GPOWrapper.GetAllowedAdvancedPasteOpenAIValue() != PowerToys.GPOWrapper.GpoRuleConfigured.Disabled,
            AIServiceType.AzureOpenAI => PowerToys.GPOWrapper.GPOWrapper.GetAllowedAdvancedPasteAzureOpenAIValue() != PowerToys.GPOWrapper.GpoRuleConfigured.Disabled,
            AIServiceType.AzureAIInference => PowerToys.GPOWrapper.GPOWrapper.GetAllowedAdvancedPasteAzureAIInferenceValue() != PowerToys.GPOWrapper.GpoRuleConfigured.Disabled,
            AIServiceType.Mistral => PowerToys.GPOWrapper.GPOWrapper.GetAllowedAdvancedPasteMistralValue() != PowerToys.GPOWrapper.GpoRuleConfigured.Disabled,
            AIServiceType.Google => PowerToys.GPOWrapper.GPOWrapper.GetAllowedAdvancedPasteGoogleValue() != PowerToys.GPOWrapper.GpoRuleConfigured.Disabled,
            AIServiceType.Ollama => PowerToys.GPOWrapper.GPOWrapper.GetAllowedAdvancedPasteOllamaValue() != PowerToys.GPOWrapper.GpoRuleConfigured.Disabled,
            AIServiceType.FoundryLocal => PowerToys.GPOWrapper.GPOWrapper.GetAllowedAdvancedPasteFoundryLocalValue() != PowerToys.GPOWrapper.GpoRuleConfigured.Disabled,
            _ => true,
        };
    }
}
