// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using Microsoft.CmdPal.Common.Helpers;

namespace Microsoft.CmdPal.Common.UnitTests.Helpers;

[TestClass]
public class AppLanguageHelperTests
{
    [TestMethod]
    public void Apply_SetsDotNetUiCultureToRequestedLanguage()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            AppLanguageHelper.Apply("ja-JP");

            Assert.IsTrue(string.Equals("ja-JP", AppLanguageHelper.LanguageOverride, StringComparison.Ordinal));
            Assert.IsTrue(string.Equals("ja-JP", CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(string.Equals("ja-JP", CultureInfo.DefaultThreadCurrentUICulture?.Name, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(string.Equals(originalCulture.Name, CultureInfo.CurrentCulture.Name, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(originalDefaultCulture?.Name, CultureInfo.DefaultThreadCurrentCulture?.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            AppLanguageHelper.Apply(string.Empty);
        }
    }

    [TestMethod]
    public void Apply_EmptyTag_ClearsLanguageOverrideWithoutThrowing()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            AppLanguageHelper.Apply("de-DE");
            AppLanguageHelper.Apply(string.Empty);

            Assert.IsTrue(string.Equals(string.Empty, AppLanguageHelper.LanguageOverride, StringComparison.Ordinal));
            Assert.IsTrue(string.Equals(originalCulture.Name, CultureInfo.CurrentCulture.Name, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(string.Equals(originalUiCulture.Name, CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(originalDefaultCulture?.Name, CultureInfo.DefaultThreadCurrentCulture?.Name);
            Assert.AreEqual(originalDefaultUiCulture?.Name, CultureInfo.DefaultThreadCurrentUICulture?.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            AppLanguageHelper.Apply(string.Empty);
        }
    }

    [TestMethod]
    public void Apply_UnknownTag_DoesNotThrow()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        var originalDefaultCulture = CultureInfo.DefaultThreadCurrentCulture;
        var originalDefaultUiCulture = CultureInfo.DefaultThreadCurrentUICulture;

        try
        {
            AppLanguageHelper.Apply("de-DE");
            AppLanguageHelper.Apply("not_a_valid_language_tag");

            Assert.IsTrue(string.Equals("de-DE", AppLanguageHelper.LanguageOverride, StringComparison.Ordinal));
            Assert.IsTrue(string.Equals("de-DE", CultureInfo.CurrentUICulture.Name, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(string.Equals("de-DE", CultureInfo.DefaultThreadCurrentUICulture?.Name, StringComparison.OrdinalIgnoreCase));
            Assert.IsTrue(string.Equals(originalCulture.Name, CultureInfo.CurrentCulture.Name, StringComparison.OrdinalIgnoreCase));
            Assert.AreEqual(originalDefaultCulture?.Name, CultureInfo.DefaultThreadCurrentCulture?.Name);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
            CultureInfo.DefaultThreadCurrentCulture = originalDefaultCulture;
            CultureInfo.DefaultThreadCurrentUICulture = originalDefaultUiCulture;
            AppLanguageHelper.Apply(string.Empty);
        }
    }
}
