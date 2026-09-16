// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using ToolGood.Words.Pinyin;

namespace Microsoft.CmdPal.Common.Text;

public sealed class PrecomputedFuzzyMatcherWithPinyin : IPrecomputedFuzzyMatcher
{
    // Upper bound on the number of pinyin readings generated per target. Polyphonic characters
    // expand the candidate readings combinatorially, so this cap keeps memory and scoring work
    // bounded (each variant is a full folded string plus a bloom value cached per item).
    private const int MaxSecondaryVariants = 8;

    private readonly IBloomFilter _bloom;
    private readonly PrecomputedFuzzyMatcher _core;

    private readonly IStringFolder _stringFolder;
    private readonly PinyinFuzzyMatcherOptions _pinyin;

    public PrecomputedFuzzyMatcherWithPinyin(
        PrecomputedFuzzyMatcherOptions coreOptions,
        PinyinFuzzyMatcherOptions pinyinOptions,
        IStringFolder stringFolder,
        IBloomFilter bloom)
    {
        _pinyin = pinyinOptions;
        _stringFolder = stringFolder;
        _bloom = bloom;

        _core = new PrecomputedFuzzyMatcher(coreOptions, stringFolder, bloom);

        SchemaId = CombineSchema(_core.SchemaId, _pinyin);
    }

    public uint SchemaId { get; }

    public FuzzyQuery PrecomputeQuery(string? input)
    {
        input ??= string.Empty;

        var primary = _core.PrecomputeQuery(input);

        // Fast exit if effectively off (provider should already filter, but keep robust)
        if (!IsPinyinEnabled(_pinyin))
        {
            return primary;
        }

        // Match legacy: remove apostrophes for query secondary
        var queryForPinyin = _pinyin.RemoveApostrophesForQuery ? RemoveApostrophesIfAny(input) : input;

        var pinyin = WordsHelper.GetPinyin(queryForPinyin);
        if (string.IsNullOrEmpty(pinyin))
        {
            return primary;
        }

        var secondary = _core.PrecomputeQuery(pinyin);
        return new FuzzyQuery(
            primary.Original,
            primary.Folded,
            primary.Bloom,
            primary.EffectiveLength,
            primary.IsAllLowercaseAsciiOrNonLetter,
            secondary.Original,
            secondary.Folded,
            secondary.Bloom,
            secondary.EffectiveLength,
            secondary.SecondaryIsAllLowercaseAsciiOrNonLetter);
    }

    public FuzzyTarget PrecomputeTarget(string? input)
    {
        input ??= string.Empty;

        var primary = _core.PrecomputeTarget(input);

        if (!IsPinyinEnabled(_pinyin))
        {
            return primary;
        }

        // Match legacy: only compute target pinyin when target contains Chinese
        if (!ContainsToolGoodChinese(input))
        {
            return primary;
        }

        var variants = BuildPinyinVariants(input, MaxSecondaryVariants);
        if (variants.Count == 0)
        {
            return primary;
        }

        var secondaryOriginals = new string[variants.Count];
        var secondaryFoldeds = new string[variants.Count];
        var secondaryBlooms = new ulong[variants.Count];

        for (var i = 0; i < variants.Count; i++)
        {
            var variantTarget = _core.PrecomputeTarget(variants[i]);
            secondaryOriginals[i] = variantTarget.Original;
            secondaryFoldeds[i] = variantTarget.Folded;
            secondaryBlooms[i] = variantTarget.Bloom;
        }

        return new FuzzyTarget(
            primary.Original,
            primary.Folded,
            primary.Bloom,
            secondaryOriginals,
            secondaryFoldeds,
            secondaryBlooms);
    }

    // Builds the set of pinyin readings to match a Chinese target against. The library's
    // context-aware reading is always included first (so behavior is a superset of the previous
    // single-reading approach); alternative readings of polyphonic characters are then added via a
    // bounded Cartesian expansion so that, for example, 重启 is reachable by both "zhongqi" and
    // "chongqi". Expansion stops once MaxSecondaryVariants distinct readings have been produced.
    private static List<string> BuildPinyinVariants(string input, int maxVariants)
    {
        var result = new List<string>(maxVariants);

        var basePinyin = WordsHelper.GetPinyin(input);
        if (!string.IsNullOrEmpty(basePinyin))
        {
            result.Add(basePinyin);
        }

        var combos = new List<string>(maxVariants) { string.Empty };

        foreach (var c in input)
        {
            string[] readings;
            if (WordsHelper.HasChinese(c.ToString()))
            {
                var all = WordsHelper.GetAllPinyin(c);
                readings = all is { Count: > 0 }
                    ? all.Distinct(StringComparer.Ordinal).ToArray()
                    : new[] { c.ToString() };
            }
            else
            {
                readings = new[] { c.ToString() };
            }

            if (readings.Length == 1)
            {
                for (var i = 0; i < combos.Count; i++)
                {
                    combos[i] += readings[0];
                }

                continue;
            }

            // Branch, but never exceed the variant cap. Once capped we stop adding branches;
            // the combos kept so far still accumulate the remaining characters below.
            var next = new List<string>(Math.Min(maxVariants, combos.Count * readings.Length));
            foreach (var prefix in combos)
            {
                foreach (var reading in readings)
                {
                    if (next.Count >= maxVariants)
                    {
                        break;
                    }

                    next.Add(prefix + reading);
                }

                if (next.Count >= maxVariants)
                {
                    break;
                }
            }

            combos = next;
        }

        foreach (var combo in combos)
        {
            if (result.Count >= maxVariants)
            {
                break;
            }

            if (combo.Length > 0 && !result.Contains(combo, StringComparer.Ordinal))
            {
                result.Add(combo);
            }
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int Score(scoped in FuzzyQuery query, scoped in FuzzyTarget target)
        => _core.Score(in query, in target);

    private static bool IsPinyinEnabled(PinyinFuzzyMatcherOptions o) => o.Mode switch
    {
        PinyinMode.Off => false,
        PinyinMode.On => true,
        PinyinMode.AutoSimplifiedChineseUi => IsSimplifiedChineseUi(),
        _ => false,
    };

    private static bool IsSimplifiedChineseUi()
    {
        var culture = CultureInfo.CurrentUICulture;
        return culture.Name.StartsWith("zh-CN", StringComparison.OrdinalIgnoreCase)
               || culture.Name.StartsWith("zh-Hans", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsToolGoodChinese(string s)
    {
        return WordsHelper.HasChinese(s);
    }

    private static string RemoveApostrophesIfAny(string input)
    {
        var first = input.IndexOf('\'');
        if (first < 0)
        {
            return input;
        }

        var removeCount = 1;
        for (var i = first + 1; i < input.Length; i++)
        {
            if (input[i] == '\'')
            {
                removeCount++;
            }
        }

        return string.Create(input.Length - removeCount, input, static (dst, src) =>
        {
            var di = 0;
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                if (c == '\'')
                {
                    continue;
                }

                dst[di++] = c;
            }
        });
    }

    private static uint CombineSchema(uint coreSchemaId, PinyinFuzzyMatcherOptions p)
    {
        const uint fnvOffset = 2166136261;
        const uint fnvPrime = 16777619;

        var h = fnvOffset;
        h = unchecked((h ^ coreSchemaId) * fnvPrime);
        h = unchecked((h ^ (uint)p.Mode) * fnvPrime);
        h = unchecked((h ^ (p.RemoveApostrophesForQuery ? 1u : 0u)) * fnvPrime);

        // bump if you change formatting/conversion behavior
        const uint pinyinAlgoVersion = 2;
        h = unchecked((h ^ pinyinAlgoVersion) * fnvPrime);

        return h;
    }
}
