// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

namespace Microsoft.CmdPal.Common.Text;

public readonly struct FuzzyTarget
{
    public readonly string Original;
    public readonly string Folded;
    public readonly ulong Bloom;

    // Secondary match variants (for example, pinyin readings). A single target can expose more
    // than one secondary variant so that polyphonic characters, whose reading depends on context
    // (for example, 重 can be read "zhong" or "chong"), are all matchable. The three arrays are
    // parallel: each index describes one variant and they always have the same length.
    public readonly string[]? SecondaryOriginals;
    public readonly string[]? SecondaryFoldeds;
    public readonly ulong[]? SecondaryBlooms;

    public int Length => Folded.Length;

    public bool HasSecondary => SecondaryFoldeds is { Length: > 0 };

    public int SecondaryCount => SecondaryFoldeds?.Length ?? 0;

    public ReadOnlySpan<char> OriginalSpan => Original.AsSpan();

    public ReadOnlySpan<char> FoldedSpan => Folded.AsSpan();

    public ReadOnlySpan<char> SecondaryOriginalSpan(int index) => SecondaryOriginals![index].AsSpan();

    public ReadOnlySpan<char> SecondaryFoldedSpan(int index) => SecondaryFoldeds![index].AsSpan();

    public int SecondaryLength(int index) => SecondaryFoldeds![index].Length;

    public ulong SecondaryBloom(int index) => SecondaryBlooms![index];

    public FuzzyTarget(
        string original,
        string folded,
        ulong bloom,
        string[]? secondaryOriginals = null,
        string[]? secondaryFoldeds = null,
        ulong[]? secondaryBlooms = null)
    {
        Original = original;
        Folded = folded;
        Bloom = bloom;
        SecondaryOriginals = secondaryOriginals;
        SecondaryFoldeds = secondaryFoldeds;
        SecondaryBlooms = secondaryBlooms;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="FuzzyTarget"/> struct with a single secondary
    /// variant. Kept for callers (and tests) that only produce one secondary reading.
    /// </summary>
    public FuzzyTarget(
        string original,
        string folded,
        ulong bloom,
        string? secondaryOriginal,
        string? secondaryFolded,
        ulong secondaryBloom)
        : this(
            original,
            folded,
            bloom,
            secondaryFolded is null ? null : new[] { secondaryOriginal ?? string.Empty },
            secondaryFolded is null ? null : new[] { secondaryFolded },
            secondaryFolded is null ? null : new[] { secondaryBloom })
    {
    }
}
