// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using CommunityToolkit.Mvvm.Messaging;
using Microsoft.CmdPal.UI.Services;
using Microsoft.CmdPal.UI.ViewModels.Messages;
using Microsoft.CmdPal.UI.ViewModels.Services;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public sealed partial class ExternalCommandLinkCoordinatorTests
{
    [TestMethod]
    public async Task RejectedConsent_DoesNotDispatch()
    {
        using var context = await TestContext.CreateAsync(new TestCommandProvider("command"));
        context.Presenter.ConsentHandler = (_, _) => ExternalCommandConsentResult.Rejected;

        await context.Coordinator.HandleLinkForTestAsync(new CmdPalProtocolRoute.ExecuteCommand(TestCommandProvider.ProviderId, "command"));

        Assert.AreEqual(0, context.Recipient.Messages.Count);
    }

    [TestMethod]
    public async Task RememberedGrant_BypassesConsentOnlyForMatchingTarget()
    {
        using var context = await TestContext.CreateAsync(new TestCommandProvider("allowed", "other"));
        context.PermissionStore
            .Setup(store => store.IsAllowedAsync(It.IsAny<ExternalCommandPermissionKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ExternalCommandPermissionKey key, CancellationToken _) => key.CommandId == "allowed");
        context.Presenter.ConsentHandler = (_, _) => ExternalCommandConsentResult.Rejected;

        await context.Coordinator.HandleLinkForTestAsync(new CmdPalProtocolRoute.ExecuteCommand(TestCommandProvider.ProviderId, "allowed"));
        await context.Coordinator.HandleLinkForTestAsync(new CmdPalProtocolRoute.ExecuteCommand(TestCommandProvider.ProviderId, "other"));

        Assert.AreEqual(1, context.Recipient.Messages.Count);
        Assert.AreEqual(1, context.Presenter.ConsentRequests.Count);
        Assert.AreEqual("other", context.Presenter.ConsentRequests[0].Permission.Key.CommandId);
    }

    [TestMethod]
    public async Task FilterRequest_CannotPersistConsent()
    {
        using var context = await TestContext.CreateAsync(new TestCommandProvider("page", isPage: true));
        context.Presenter.ConsentHandler = (request, _) =>
        {
            Assert.IsFalse(request.CanRemember);
            return ExternalCommandConsentResult.AllowOnce;
        };

        await context.Coordinator.HandleLinkForTestAsync(
            new CmdPalProtocolRoute.ExecuteCommand(
                TestCommandProvider.ProviderId,
                "page",
                new ListPageLaunchOptions(FilterId: "active")));

        Assert.AreEqual(1, context.Recipient.Messages.Count);
        context.PermissionStore.Verify(
            store => store.RememberAsync(It.IsAny<ExternalCommandPermission>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task SettingDisabledDuringConsent_BlocksDispatch()
    {
        using var context = await TestContext.CreateAsync(new TestCommandProvider("command"));
        context.Presenter.ConsentHandler = (_, _) =>
        {
            context.Settings = context.Settings with { EnableExternalCommandLinks = false };
            return ExternalCommandConsentResult.AllowOnce;
        };

        await context.Coordinator.HandleLinkForTestAsync(new CmdPalProtocolRoute.ExecuteCommand(TestCommandProvider.ProviderId, "command"));

        Assert.AreEqual(0, context.Recipient.Messages.Count);
    }

    [TestMethod]
    public async Task CommandShapeChangedAfterConsent_IsRejected()
    {
        using var context = await TestContext.CreateAsync(new TestCommandProvider("command", changeToPageAfterFirstLookup: true));
        context.Presenter.ConsentHandler = (_, _) => ExternalCommandConsentResult.AllowOnce;

        await context.Coordinator.HandleLinkForTestAsync(new CmdPalProtocolRoute.ExecuteCommand(TestCommandProvider.ProviderId, "command"));

        Assert.AreEqual(0, context.Recipient.Messages.Count);
        Assert.AreEqual(1, context.Presenter.UnavailableCount);
    }

    private sealed class TestContext : IDisposable
    {
        private TestContext(
            ServiceProvider services,
            TopLevelCommandManager manager,
            Mock<IExternalCommandPermissionStore> permissionStore,
            TestPresenter presenter,
            MessageRecipient recipient,
            ExternalCommandLinkCoordinator coordinator,
            SettingsModel settings)
        {
            Services = services;
            Manager = manager;
            PermissionStore = permissionStore;
            Presenter = presenter;
            Recipient = recipient;
            Coordinator = coordinator;
            Settings = settings;
        }

        public ServiceProvider Services { get; }

        public TopLevelCommandManager Manager { get; }

        public Mock<IExternalCommandPermissionStore> PermissionStore { get; }

        public TestPresenter Presenter { get; }

        public MessageRecipient Recipient { get; }

        public ExternalCommandLinkCoordinator Coordinator { get; }

        public SettingsModel Settings { get; set; }

        public static async Task<TestContext> CreateAsync(TestCommandProvider provider)
        {
            var settings = new SettingsModel();
            var settingsService = new Mock<ISettingsService>();
            TestContext? context = null;
            settingsService.SetupGet(service => service.Settings).Returns(() => context?.Settings ?? settings);

            var services = new ServiceCollection()
                .AddSingleton(TaskScheduler.Default)
                .AddSingleton(settingsService.Object)
                .BuildServiceProvider();

            var wrapper = new CommandProviderWrapper(provider, TaskScheduler.Default);
            var extensionService = new Mock<IExtensionService>();
            extensionService
                .Setup(service => service.LoadProvidersAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync([wrapper]);
            var manager = new TopLevelCommandManager(services, [extensionService.Object]);
            await manager.LoadExternalProvidersAsync();

            var permissionStore = new Mock<IExternalCommandPermissionStore>();
            permissionStore
                .Setup(store => store.IsAllowedAsync(It.IsAny<ExternalCommandPermissionKey>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            permissionStore
                .Setup(store => store.RememberAsync(It.IsAny<ExternalCommandPermission>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(true);

            var presenter = new TestPresenter();

            var messenger = new StrongReferenceMessenger();
            var recipient = new MessageRecipient();
            messenger.Register<PerformCommandMessage>(recipient);

            var coordinator = new ExternalCommandLinkCoordinator(
                settingsService.Object,
                permissionStore.Object,
                manager,
                presenter,
                messenger,
                null!,
                new ExternalCommandPermission(ExternalCommandPermissionKey.Reload, "Reload", "Command Palette"));

            context = new TestContext(services, manager, permissionStore, presenter, recipient, coordinator, settings);
            return context;
        }

        public void Dispose()
        {
            Coordinator.Dispose();
            Manager.Dispose();
            Services.Dispose();
        }
    }

    public sealed class MessageRecipient : IRecipient<PerformCommandMessage>
    {
        public List<PerformCommandMessage> Messages { get; } = [];

        public void Receive(PerformCommandMessage message)
        {
            Messages.Add(message);
        }
    }

    private sealed class TestPresenter : IExternalCommandLinkPresenter
    {
        public Func<ExternalCommandConsentRequest, bool, ExternalCommandConsentResult> ConsentHandler { get; set; } =
            (_, _) => ExternalCommandConsentResult.Rejected;

        public List<ExternalCommandConsentRequest> ConsentRequests { get; } = [];

        public int UnavailableCount { get; private set; }

        public Task<IExternalCommandLinkDialogSession?> PrepareLoadingAsync() =>
            Task.FromResult<IExternalCommandLinkDialogSession?>(null);

        public Task<ExternalCommandConsentResult> RequestConsentAsync(
            ExternalCommandConsentRequest request,
            bool summonWindow = true)
        {
            ConsentRequests.Add(request);
            return Task.FromResult(ConsentHandler(request, summonWindow));
        }

        public Task ShowUnavailableAsync(bool summonWindow = true)
        {
            UnavailableCount++;
            return Task.CompletedTask;
        }
    }

    private sealed partial class TestCommandProvider : CommandProvider
    {
        public const string ProviderId = "test-provider";

        private readonly Dictionary<string, ICommandItem> _commands;
        private readonly string? _changingCommandId;
        private readonly ICommandItem? _changedCommand;
        private int _lookupCount;

        public TestCommandProvider(params string[] commandIds)
            : this(commandIds, isPage: false)
        {
        }

        public TestCommandProvider(string commandId, bool isPage = false, bool changeToPageAfterFirstLookup = false)
            : this([commandId], isPage)
        {
            if (changeToPageAfterFirstLookup)
            {
                _changingCommandId = commandId;
                _changedCommand = CreateCommandItem(commandId, isPage: true);
            }
        }

        private TestCommandProvider(IEnumerable<string> commandIds, bool isPage)
        {
            Id = ProviderId;
            DisplayName = "Test provider";
            _commands = commandIds.ToDictionary(id => id, id => CreateCommandItem(id, isPage));
        }

        public override ICommandItem[] TopLevelCommands() => [];

        public override ICommandItem? GetCommandItem(string id)
        {
            var lookup = Interlocked.Increment(ref _lookupCount);
            if (id == _changingCommandId && lookup > 1)
            {
                return _changedCommand;
            }

            return _commands.GetValueOrDefault(id);
        }

        private static ICommandItem CreateCommandItem(string id, bool isPage)
        {
            ICommand command = isPage
                ? new TestListPage { Id = id, Name = id }
                : new NoOpCommand { Id = id, Name = id };
            return new CommandItem(command);
        }
    }

    private sealed partial class TestListPage : ListPage
    {
        public override IListItem[] GetItems() => [];
    }
}
