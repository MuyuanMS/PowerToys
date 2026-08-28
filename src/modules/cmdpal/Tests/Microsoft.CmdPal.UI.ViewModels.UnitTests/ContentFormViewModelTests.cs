// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Threading.Tasks;
using AdaptiveCards.ObjectModel.WinUI3;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.Data.Json;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public partial class ContentFormViewModelTests
{
    private sealed class TestPageContext : IPageContext
    {
        public TaskScheduler Scheduler => TaskScheduler.Default;

        public ICommandProviderContext ProviderContext => CommandProviderContext.Empty;

        public void ShowException(Exception ex, string? extensionHint = null)
        {
            throw new AssertFailedException($"Unexpected exception from view model: {ex}");
        }
    }

    private sealed partial class ActionFormContent : FormContent
    {
        public TaskCompletionSource<(string ActionId, string Inputs, string Data)> SubmitActionCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override ICommandResult SubmitAction(string actionId, string inputs, string data)
        {
            SubmitActionCalled.SetResult((actionId, inputs, data));
            return CommandResult.KeepOpen();
        }
    }

    private sealed partial class LegacyFormContent : BaseObservable, IFormContent
    {
        public string TemplateJson { get; } = string.Empty;

        public string DataJson { get; } = string.Empty;

        public string StateJson { get; } = string.Empty;

        public TaskCompletionSource<(string Inputs, string Data)> SubmitFormCalled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ICommandResult SubmitForm(string inputs, string data)
        {
            SubmitFormCalled.SetResult((inputs, data));
            return CommandResult.KeepOpen();
        }
    }

    private static WeakReference<IPageContext> CreatePageContext()
    {
        var context = new TestPageContext();
        return new WeakReference<IPageContext>(context);
    }

    [TestMethod]
    public void GetActionData_AdaptiveExecuteAction_ReturnsDataJson()
    {
        var action = new AdaptiveExecuteAction
        {
            DataJson = global::Windows.Data.Json.JsonValue.Parse("{\"source\":\"execute\"}"),
        };

        Assert.AreEqual("{\"source\":\"execute\"}", ContentFormViewModel.GetActionData(action));
    }

    [TestMethod]
    public void GetActionData_ActionsWithoutData_ReturnsEmptyString()
    {
        Assert.AreEqual(string.Empty, ContentFormViewModel.GetActionData(new AdaptiveSubmitAction()));
        Assert.AreEqual(string.Empty, ContentFormViewModel.GetActionData(new AdaptiveExecuteAction()));
    }

    [TestMethod]
    public async Task HandleSubmit_IFormContent2_PassesActionIdInputsAndData()
    {
        var form = new ActionFormContent();
        var viewModel = new ContentFormViewModel(form, CreatePageContext());
        var action = new AdaptiveExecuteAction
        {
            Id = "save",
            DataJson = JsonValue.Parse("{\"source\":\"execute\"}"),
        };
        var inputs = new JsonObject
        {
            ["name"] = JsonValue.CreateStringValue("PowerToys"),
        };

        viewModel.HandleSubmit(action, inputs);

        var submission = await form.SubmitActionCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("save", submission.ActionId);
        Assert.AreEqual("{\"name\":\"PowerToys\"}", submission.Inputs);
        Assert.AreEqual("{\"source\":\"execute\"}", submission.Data);
    }

    [TestMethod]
    public async Task HandleSubmit_IFormContent_FallsBackToSubmitForm()
    {
        var form = new LegacyFormContent();
        var viewModel = new ContentFormViewModel(form, CreatePageContext());
        var action = new AdaptiveSubmitAction
        {
            Id = "legacy-save",
            DataJson = JsonValue.Parse("{\"source\":\"submit\"}"),
        };
        var inputs = new JsonObject
        {
            ["name"] = JsonValue.CreateStringValue("PowerToys"),
        };

        viewModel.HandleSubmit(action, inputs);

        var submission = await form.SubmitFormCalled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.AreEqual("{\"name\":\"PowerToys\"}", submission.Inputs);
        Assert.AreEqual("{\"source\":\"submit\"}", submission.Data);
    }
}
