// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using AdaptiveCards.ObjectModel.WinUI3;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.UI.ViewModels.UnitTests;

[TestClass]
public class ContentFormViewModelTests
{
    [TestMethod]
    public void GetActionData_AdaptiveExecuteAction_ReturnsDataJson()
    {
        var action = new AdaptiveExecuteAction
        {
            DataJson = global::Windows.Data.Json.JsonValue.Parse("{\"source\":\"execute\"}"),
        };

        Assert.AreEqual("{\"source\":\"execute\"}", ContentFormViewModel.GetActionData(action));
    }
}
