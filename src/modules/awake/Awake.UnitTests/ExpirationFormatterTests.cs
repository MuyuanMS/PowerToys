// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using Awake.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Awake.UnitTests;

[TestClass]
public class ExpirationFormatterTests
{
    private static readonly DateTimeOffset Afternoon = new(2026, 9, 30, 17, 30, 0, TimeSpan.FromHours(2));

    [TestMethod]
    public void Format_With24HourCulture_Uses24HourClock()
    {
        string text = ExpirationFormatter.Format(Afternoon, CultureInfo.GetCultureInfo("de-DE"));

        Assert.AreEqual("30. September, 17:30", text);
    }

    [TestMethod]
    public void Format_With12HourCulture_KeepsAmPmDesignator()
    {
        CultureInfo culture = CultureInfo.GetCultureInfo("en-US");

        string text = ExpirationFormatter.Format(Afternoon, culture);

        StringAssert.StartsWith(text, "September 30, 5:30");
        StringAssert.EndsWith(text, culture.DateTimeFormat.PMDesignator);
    }
}
