// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using AdvancedPaste.Helpers;
using AdvancedPaste.Models;
using AdvancedPaste.Services;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.SemanticKernel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedPaste.UnitTests.ServicesTests;

[TestClass]
public sealed class AdvancedAIProviderResolverTests
{
    [TestMethod]
    public void TryResolveAdvancedProvider_WithPhiSilicaOverrideAndAdvancedActiveProvider_ReturnsFalse()
    {
        var advancedProvider = CreateAdvancedProvider("advanced", AIServiceType.OpenAI);
        var phiSilicaProvider = new PasteAIProviderDefinition { Id = "phi", ServiceTypeKind = AIServiceType.PhiSilica };
        var configuration = CreateConfiguration(advancedProvider, advancedProvider, phiSilicaProvider);

        var result = AdvancedAIProviderResolver.TryResolveAdvancedProvider(configuration, phiSilicaProvider.Id, out var provider);

        Assert.IsFalse(result);
        Assert.IsNull(provider);
    }

    [TestMethod]
    public void TryResolveAdvancedProvider_WithNonActiveAdvancedOverride_ReturnsOverride()
    {
        var activeProvider = CreateAdvancedProvider("active", AIServiceType.OpenAI);
        var overrideProvider = CreateAdvancedProvider("override", AIServiceType.AzureOpenAI);
        var configuration = CreateConfiguration(activeProvider, activeProvider, overrideProvider);

        var result = AdvancedAIProviderResolver.TryResolveAdvancedProvider(configuration, overrideProvider.Id, out var provider);

        Assert.IsTrue(result);
        Assert.AreSame(overrideProvider, provider);
    }

    [TestMethod]
    public void ResolveProvider_WithNonActiveOverride_ReturnsOverride()
    {
        var activeProvider = CreateAdvancedProvider("active", AIServiceType.OpenAI);
        var overrideProvider = new PasteAIProviderDefinition { Id = "override", ServiceTypeKind = AIServiceType.PhiSilica };
        var configuration = CreateConfiguration(activeProvider, activeProvider, overrideProvider);

        var provider = AdvancedAIProviderResolver.ResolveProvider(configuration, overrideProvider.Id);

        Assert.AreSame(overrideProvider, provider);
    }

    [TestMethod]
    public void GetDefaultModelName_WithPhiSilica_ReturnsDisplayName()
    {
        Assert.AreEqual("Phi Silica", PasteAIProviderDefaults.GetDefaultModelName(AIServiceType.PhiSilica));
    }

    [TestMethod]
    public void SupportsClipboardFormats_WithPhiSilica_RequiresText()
    {
        var provider = new PasteAIProviderDefinition { ServiceTypeKind = AIServiceType.PhiSilica };

        Assert.IsFalse(AIProviderPolicy.SupportsClipboardFormats(provider, ClipboardFormat.Image));
        Assert.IsTrue(AIProviderPolicy.SupportsClipboardFormats(provider, ClipboardFormat.Text));
        Assert.IsTrue(AIProviderPolicy.SupportsClipboardFormats(provider, ClipboardFormat.Text | ClipboardFormat.Image));
    }

    [TestMethod]
    public void KernelProviderId_RoundTrips()
    {
        var kernel = Kernel.CreateBuilder().Build();

        kernel.SetProviderId("provider-b");

        Assert.AreEqual("provider-b", kernel.GetProviderId());
    }

    private static PasteAIProviderDefinition CreateAdvancedProvider(string id, AIServiceType serviceType) =>
        new()
        {
            Id = id,
            ServiceTypeKind = serviceType,
            EnableAdvancedAI = true,
        };

    private static PasteAIConfiguration CreateConfiguration(PasteAIProviderDefinition activeProvider, params PasteAIProviderDefinition[] providers) =>
        new()
        {
            ActiveProviderId = activeProvider.Id,
            Providers = new ObservableCollection<PasteAIProviderDefinition>(providers),
        };
}
