// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#pragma once

#include <string>
#include <string_view>

namespace CommandLine
{
    // Removes argv[0] the way the CRT tokenizes it - every quote toggles an in-quotes flag and the
    // name ends at the first whitespace outside quotes, so neither a quote nor a backslash-escape
    // terminates it - then trims the separating spaces/tabs and preserves the remaining
    // command-line text verbatim. Note that CommandLineToArgvW uses a different rule for argv[0];
    // the CRT's is the one the target CLI tools actually parse. Leading whitespace means an empty
    // argv[0], so the remaining text is forwarded as-is; see CommandLine.cpp.
    std::wstring StripArgumentZero(std::wstring_view commandLine);
}
