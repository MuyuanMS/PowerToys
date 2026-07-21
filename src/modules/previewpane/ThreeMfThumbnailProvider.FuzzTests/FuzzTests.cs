// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;

using Provider = Microsoft.PowerToys.ThumbnailHandler.ThreeMf.ThreeMfThumbnailProvider;

namespace ThreeMfThumbnailProvider.FuzzTests
{
    public class FuzzTests
    {
        // Fuzz target for the full 3MF thumbnail pipeline (ZIP/OPC parsing, relationship resolution,
        // embedded-image decoding and mesh rendering). The provider is invoked on untrusted files by
        // Explorer, so this feeds arbitrary bytes as a candidate .3mf package. GetThumbnail is designed
        // to swallow malformed input and return null; libFuzzer still surfaces the failures that must
        // never happen here — hangs, stack overflows and memory-exhaustion (the reason the loader
        // enforces decompression / geometry / part-count budgets).
        public static void FuzzGetThumbnail(ReadOnlySpan<byte> input)
        {
            using var stream = new MemoryStream(input.ToArray());
            _ = Provider.GetThumbnail(stream, 256);
        }
    }
}
