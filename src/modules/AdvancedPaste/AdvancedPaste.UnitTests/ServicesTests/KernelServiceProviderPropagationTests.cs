// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using AdvancedPaste.Models;
using AdvancedPaste.Models.KernelQueryCache;
using AdvancedPaste.Services;
using AdvancedPaste.Services.CustomActions;
using AdvancedPaste.Settings;
using AdvancedPaste.UnitTests.Mocks;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.SemanticKernel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.ApplicationModel.DataTransfer;

namespace AdvancedPaste.UnitTests.ServicesTests;

[TestClass]
public class KernelServiceProviderPropagationTests
{
    [TestMethod]
    public async Task CachedNestedTransform_UsesResolvedProvider()
    {
        var customActionTransformService = new CapturingCustomActionTransformService();
        var userSettings = new IntegrationTestUserSettings();
        var kernelService = new TestKernelService(
            new CachedActionChainService(),
            new NoOpPromptModerationService(),
            userSettings,
            customActionTransformService);
        var input = new DataPackage();
        input.SetText("input");

        await kernelService.TransformClipboardAsync(
            "top-level prompt",
            input.GetView(),
            isSavedQuery: false,
            CancellationToken.None,
            new NoOpProgress(),
            providerIdOverride: "selected-provider");

        Assert.AreEqual("selected-provider", customActionTransformService.ProviderIdOverride);
    }

    private sealed class TestKernelService(
        IKernelQueryCacheService queryCacheService,
        IPromptModerationService promptModerationService,
        IUserSettings userSettings,
        ICustomActionTransformService customActionTransformService)
        : KernelServiceBase(queryCacheService, promptModerationService, userSettings, customActionTransformService)
    {
        protected override PromptExecutionSettings GetPromptExecutionSettings(IKernelRuntimeConfiguration runtimeConfig) => new();

        protected override void AddChatCompletionService(IKernelBuilder kernelBuilder, IKernelRuntimeConfiguration runtimeConfig)
        {
        }

        protected override AIServiceUsage GetAIServiceUsage(ChatMessageContent chatMessage) => AIServiceUsage.None;

        protected override IKernelRuntimeConfiguration GetRuntimeConfiguration(string providerIdOverride)
            => new TestRuntimeConfiguration(providerIdOverride ?? "default-provider");
    }

    private sealed record TestRuntimeConfiguration(string ProviderId) : IKernelRuntimeConfiguration
    {
        public AIServiceType ServiceType => AIServiceType.OpenAI;

        public string ModelName => "test-model";

        public string Endpoint => string.Empty;

        public string DeploymentName => string.Empty;

        public string ModelPath => string.Empty;

        public string SystemPrompt => string.Empty;

        public bool ModerationEnabled => false;
    }

    private sealed class CachedActionChainService : IKernelQueryCacheService
    {
        public Task WriteAsync(CacheKey key, CacheValue value) => Task.CompletedTask;

        public CacheValue ReadOrNull(CacheKey key)
            => new(
            [
                new ActionChainItem(
                    PasteFormats.CustomTextTransformation,
                    new Dictionary<string, string> { ["prompt"] = "nested prompt" }),
            ]);
    }

    private sealed class CapturingCustomActionTransformService : ICustomActionTransformService
    {
        public string ProviderIdOverride { get; private set; }

        public Task<CustomActionTransformResult> TransformAsync(
            string prompt,
            string inputText,
            byte[] imageBytes,
            CancellationToken cancellationToken,
            IProgress<double> progress,
            string systemPromptOverride = null,
            string providerIdOverride = null)
        {
            ProviderIdOverride = providerIdOverride;
            return Task.FromResult(new CustomActionTransformResult("output", AIServiceUsage.None));
        }
    }

    private sealed class NoOpPromptModerationService : IPromptModerationService
    {
        public Task ValidateAsync(string fullPrompt, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
