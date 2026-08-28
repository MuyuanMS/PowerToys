// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CommandPalette.Extensions.Toolkit.UnitTests;

[TestClass]
public class FormContentTests
{
    private sealed class FormContentWithDataSubmission : FormContent
    {
        public string? SubmittedInputs { get; private set; }

        public string? SubmittedData { get; private set; }

        public ICommandResult SubmitResult { get; } = CommandResult.KeepOpen();

        public override ICommandResult SubmitForm(string inputs, string data)
        {
            SubmittedInputs = inputs;
            SubmittedData = data;
            return SubmitResult;
        }
    }

    [TestMethod]
    public void SubmitAction_ForwardsInputsAndDataToSubmitForm()
    {
        var form = new FormContentWithDataSubmission();

        var result = form.SubmitAction("save", "{\"name\":\"PowerToys\"}", "{\"source\":\"execute\"}");

        Assert.AreEqual("{\"name\":\"PowerToys\"}", form.SubmittedInputs);
        Assert.AreEqual("{\"source\":\"execute\"}", form.SubmittedData);
        Assert.AreSame(form.SubmitResult, result);
    }
}
