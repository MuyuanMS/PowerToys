// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using AdvancedPaste.Services;
using Microsoft.PowerToys.Settings.UI.Library;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace AdvancedPaste.UnitTests.ServicesTests;

[TestClass]
public class AdvancedPastePolicyTests
{
    [TestMethod]
    public void NormalizeServiceType_UnknownMatchesExecutionFallback()
    {
        Assert.AreEqual(AIServiceType.OpenAI, AdvancedPastePolicy.NormalizeServiceType(AIServiceType.Unknown));
    }

    [TestMethod]
    public void NormalizeServiceType_KnownProviderRemainsUnchanged()
    {
        Assert.AreEqual(AIServiceType.AzureOpenAI, AdvancedPastePolicy.NormalizeServiceType(AIServiceType.AzureOpenAI));
    }
}
