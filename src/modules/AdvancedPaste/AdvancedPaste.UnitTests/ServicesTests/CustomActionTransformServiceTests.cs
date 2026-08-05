// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.ObjectModel;
using AdvancedPaste.Services;
using AdvancedPaste.Services.CustomActions;
using AdvancedPaste.Settings;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace AdvancedPaste.UnitTests.ServicesTests;

[TestClass]
public sealed class CustomActionTransformServiceTests
{
    [TestMethod]
    public void BuildProviderConfigUsesExplicitProviderAndCredentials()
    {
        var (service, configuration, credentials, _, _) = CreateService();

        var result = service.BuildProviderConfig(configuration, "secondary");

        Assert.AreEqual(AIServiceType.AzureOpenAI, result.ProviderType);
        Assert.AreEqual("secondary-model", result.Model);
        Assert.AreEqual("secondary-key", result.ApiKey);
        credentials.Verify(provider => provider.GetKey(AIServiceType.AzureOpenAI, "secondary"), Times.Once);
    }

    [TestMethod]
    public void BuildProviderConfigUsesActiveProviderWithoutOverride()
    {
        var (service, configuration, credentials, _, _) = CreateService();

        var result = service.BuildProviderConfig(configuration);

        Assert.AreEqual(AIServiceType.OpenAI, result.ProviderType);
        Assert.AreEqual("active-model", result.Model);
        Assert.AreEqual("active-key", result.ApiKey);
        credentials.Verify(provider => provider.GetKey(AIServiceType.OpenAI, "active"), Times.Once);
    }

    [TestMethod]
    public void BuildProviderConfigFallsBackToActiveProviderForStaleOverride()
    {
        var (service, configuration, _, _, _) = CreateService();

        var result = service.BuildProviderConfig(configuration, "missing");

        Assert.AreEqual(AIServiceType.OpenAI, result.ProviderType);
        Assert.AreEqual("active-model", result.Model);
    }

    [TestMethod]
    public async System.Threading.Tasks.Task TransformAsyncModeratesWithExplicitProviderCredential()
    {
        var (service, _, _, moderation, _) = CreateService();

        await service.TransformAsync("rewrite", "input", null, default, null, providerIdOverride: "secondary");

        moderation.Verify(
            item => item.ValidateAsync(
                It.IsAny<string>(),
                It.IsAny<System.Threading.CancellationToken>(),
                "secondary-key"),
            Times.Once);
    }

    private static (
        CustomActionTransformService Service,
        PasteAIConfiguration Configuration,
        Mock<IAICredentialsProvider> Credentials,
        Mock<IPromptModerationService> Moderation,
        Mock<IPasteAIProviderFactory> Factory) CreateService()
    {
        var active = new PasteAIProviderDefinition
        {
            Id = "active",
            ServiceTypeKind = AIServiceType.OpenAI,
            ModelName = "active-model",
        };
        var secondary = new PasteAIProviderDefinition
        {
            Id = "secondary",
            ServiceTypeKind = AIServiceType.AzureOpenAI,
            ModelName = "secondary-model",
            ModerationEnabled = true,
        };
        var configuration = new PasteAIConfiguration
        {
            ActiveProviderId = active.Id,
            Providers = new ObservableCollection<PasteAIProviderDefinition> { active, secondary },
        };

        var userSettings = new Mock<IUserSettings>();
        userSettings.SetupGet(settings => settings.PasteAIConfiguration).Returns(configuration);

        var credentials = new Mock<IAICredentialsProvider>();
        credentials.Setup(provider => provider.GetKey(AIServiceType.OpenAI, "active")).Returns("active-key");
        credentials.Setup(provider => provider.GetKey(AIServiceType.AzureOpenAI, "secondary")).Returns("secondary-key");

        var moderation = new Mock<IPromptModerationService>();
        var pasteProvider = new Mock<IPasteAIProvider>();
        pasteProvider
            .Setup(provider => provider.ProcessPasteAsync(
                It.IsAny<PasteAIRequest>(),
                It.IsAny<System.Threading.CancellationToken>(),
                It.IsAny<System.IProgress<double>>()))
            .ReturnsAsync("result");
        var factory = new Mock<IPasteAIProviderFactory>();
        factory.Setup(item => item.CreateProvider(It.IsAny<PasteAIConfig>())).Returns(pasteProvider.Object);

        var service = new CustomActionTransformService(
            moderation.Object,
            factory.Object,
            credentials.Object,
            userSettings.Object);

        return (service, configuration, credentials, moderation, factory);
    }
}
