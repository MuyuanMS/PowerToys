#pragma once

#include "pch.h"

// Non-localizable constants
namespace constants::nonlocalizable
{
    // String key used by PowerToys runner
    constexpr WCHAR PowerToyKey[] = L"Copy as UNC";

    // Nonlocalized name of this PowerToy, for logs, etc.
    constexpr WCHAR PowerToyName[] = L"CopyAsUNC";

    // Name of the tier 1 context menu package
    constexpr WCHAR ContextMenuPackageName[] = L"CopyAsUNCContextMenu";
}
