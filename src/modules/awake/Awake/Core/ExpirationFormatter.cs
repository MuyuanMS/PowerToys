// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;

namespace Awake.Core;

internal static class ExpirationFormatter
{
    // Uses the culture's own month/day and short time patterns, because a fixed pattern like
    // "h:mm tt" forces a 12-hour clock and loses the AM/PM marker in cultures that have none.
    internal static string Format(DateTimeOffset expireAt, CultureInfo culture)
    {
        return $"{expireAt.ToString("M", culture)}, {expireAt.ToString("t", culture)}";
    }
}
