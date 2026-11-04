// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;

namespace Awake.Core;

internal static class ExpirationFormatter
{
    internal static string Format(DateTimeOffset expireAt, CultureInfo culture)
    {
        return $"{expireAt.ToString("M", culture)}, {expireAt.ToString("t", culture)}";
    }
}
