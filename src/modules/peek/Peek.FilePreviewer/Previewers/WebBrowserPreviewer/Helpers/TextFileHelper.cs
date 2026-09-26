// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using ManagedCommon;

namespace Peek.FilePreviewer.Previewers
{
    /// <summary>
    /// Heuristically detects whether a file's content is text, so files with no extension
    /// or an extension Peek doesn't otherwise recognize can still fall back to a plain text
    /// preview instead of just showing file details.
    /// </summary>
    public static class TextFileHelper
    {
        // Matches the sample size commonly used by tools like git to decide whether a file is text or binary.
        internal const int SampleSize = 8000;

        /// <summary>
        /// Determines whether the file at the given path is likely to be a text file, based on its size and content.
        /// </summary>
        /// <param name="path">The path to the file to check.</param>
        /// <param name="maxFileSizeBytes">
        /// Files larger than this are rejected outright, so an oversized file never reaches ReadHelper.Read
        /// and the Monaco/WebView pipeline, which load the full content into memory.
        /// </param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
        /// <returns>True if the file is likely to be a text file; otherwise, false.</returns>
        public static async Task<bool> IsTextFileAsync(string path, long maxFileSizeBytes = ReadHelper.MaxReadableFileSizeBytes, CancellationToken cancellationToken = default)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                using var stream = ReadHelper.OpenReadOnly(path);
                if (stream.Length > maxFileSizeBytes)
                {
                    return false;
                }

                int bytesToRead = (int)Math.Min(SampleSize, stream.Length);
                if (bytesToRead == 0)
                {
                    return true;
                }

                var buffer = new byte[bytesToRead];

                // A single ReadAsync can return fewer bytes than requested (common on network or
                // compressed streams), so keep reading until the sample buffer is full or we hit EOF.
                int bytesRead = await stream.ReadAtLeastAsync(buffer.AsMemory(0, bytesToRead), bytesToRead, throwOnEndOfStream: false, cancellationToken).ConfigureAwait(false);

                // If the file starts with a Unicode BOM, we can assume it's a text file.
                if (HasUnicodeBom(buffer, bytesRead))
                {
                    return true;
                }

                if (TryDetectBomlessUnicodeEncoding(buffer, bytesRead) != null)
                {
                    return true;
                }

                // A NUL byte in the sample is a strong signal of binary content.
                for (int i = 0; i < bytesRead; i++)
                {
                    if (buffer[i] == 0)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                // Let navigation cancellation propagate instead of being reported as "not text".
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to determine if file is text: " + ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Determines whether the given buffer contains a Unicode BOM (Byte Order Mark) for UTF-16 or UTF-32.
        /// </summary>
        /// <param name="buffer">The byte array to check for a BOM.</param>
        /// <param name="bytesRead">The number of bytes read into the buffer.</param>
        /// <returns>True if the buffer contains a Unicode BOM; otherwise, false.</returns>
        private static bool HasUnicodeBom(byte[] buffer, int bytesRead)
        {
            // UTF-32 BOMs must be checked before UTF-16, since the UTF-32LE BOM (FF FE 00 00) starts with the UTF-16LE BOM (FF FE).
            bool isUtf32 = bytesRead >= 4 &&
                ((buffer[0] == 0xFF && buffer[1] == 0xFE && buffer[2] == 0x00 && buffer[3] == 0x00) ||
                 (buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF));
            if (isUtf32)
            {
                return true;
            }

            bool isUtf16 = bytesRead >= 2 &&
                ((buffer[0] == 0xFF && buffer[1] == 0xFE) ||
                 (buffer[0] == 0xFE && buffer[1] == 0xFF));
            return isUtf16;
        }

        internal static Encoding? TryDetectBomlessUnicodeEncoding(byte[] buffer, int bytesRead)
        {
            if (HasExpectedNullPattern(buffer, bytesRead, unitSize: 4, textByteIndex: 0, requiredNullByteIndexes: [2, 3]))
            {
                return new UTF32Encoding(bigEndian: false, byteOrderMark: false);
            }

            if (HasExpectedNullPattern(buffer, bytesRead, unitSize: 4, textByteIndex: 3, requiredNullByteIndexes: [0, 1]))
            {
                return new UTF32Encoding(bigEndian: true, byteOrderMark: false);
            }

            Encoding? utf32Encoding = TryDetectBomlessUtf32WithValidatedText(buffer, bytesRead);
            if (utf32Encoding != null)
            {
                return utf32Encoding;
            }

            if (HasExpectedNullPattern(buffer, bytesRead, unitSize: 2, textByteIndex: 0, requiredNullByteIndexes: [1]))
            {
                return Encoding.Unicode;
            }

            if (HasExpectedNullPattern(buffer, bytesRead, unitSize: 2, textByteIndex: 1, requiredNullByteIndexes: [0]))
            {
                return Encoding.BigEndianUnicode;
            }

            return TryDetectBomlessUtf16WithValidatedText(buffer, bytesRead);
        }

        private static Encoding? TryDetectBomlessUtf32WithValidatedText(byte[] buffer, int bytesRead)
        {
            if (bytesRead < 8 || bytesRead % 4 != 0)
            {
                return null;
            }

            bool isLittleEndianText = IsPlausibleBomlessUtf32Text(buffer, bytesRead, bigEndian: false);
            bool isBigEndianText = IsPlausibleBomlessUtf32Text(buffer, bytesRead, bigEndian: true);
            if (isLittleEndianText == isBigEndianText)
            {
                return null;
            }

            return new UTF32Encoding(bigEndian: isBigEndianText, byteOrderMark: false);
        }

        private static bool IsPlausibleBomlessUtf32Text(byte[] buffer, int bytesRead, bool bigEndian)
        {
            string text;
            try
            {
                text = new UTF32Encoding(bigEndian, byteOrderMark: false, throwOnInvalidCharacters: true)
                    .GetString(buffer, 0, bytesRead);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            int runeCount = 0;
            foreach (Rune rune in text.EnumerateRunes())
            {
                if (!IsTextRune(rune))
                {
                    return false;
                }

                runeCount++;
            }

            return runeCount >= 2;
        }

        private static bool HasExpectedNullPattern(byte[] buffer, int bytesRead, int unitSize, int textByteIndex, int[] requiredNullByteIndexes)
        {
            int unitCount = bytesRead / unitSize;
            if (unitCount < 2)
            {
                return false;
            }

            int textNulls = 0;
            var requiredNullCounts = new int[requiredNullByteIndexes.Length];
            for (int unit = 0; unit < unitCount; unit++)
            {
                int offset = unit * unitSize;
                if (buffer[offset + textByteIndex] == 0)
                {
                    textNulls++;
                }

                for (int index = 0; index < requiredNullByteIndexes.Length; index++)
                {
                    if (buffer[offset + requiredNullByteIndexes[index]] == 0)
                    {
                        requiredNullCounts[index]++;
                    }
                }
            }

            return requiredNullCounts.All(count => count * 10 >= unitCount * 3) &&
                textNulls * 10 <= unitCount * 3;
        }

        private static Encoding? TryDetectBomlessUtf16WithValidatedText(byte[] buffer, int bytesRead)
        {
            if (bytesRead < 6 || bytesRead % 2 != 0)
            {
                return null;
            }

            int littleEndianNulls = 0;
            int bigEndianNulls = 0;
            for (int offset = 0; offset < bytesRead; offset += 2)
            {
                if (buffer[offset + 1] == 0)
                {
                    littleEndianNulls++;
                }

                if (buffer[offset] == 0)
                {
                    bigEndianNulls++;
                }
            }

            if (littleEndianNulls > bigEndianNulls &&
                IsPlausibleBomlessUtf16Text(buffer, bytesRead, bigEndian: false))
            {
                return Encoding.Unicode;
            }

            if (bigEndianNulls > littleEndianNulls &&
                IsPlausibleBomlessUtf16Text(buffer, bytesRead, bigEndian: true))
            {
                return Encoding.BigEndianUnicode;
            }

            return null;
        }

        private static bool IsPlausibleBomlessUtf16Text(byte[] buffer, int bytesRead, bool bigEndian)
        {
            bool hasNonAsciiByte = false;
            for (int index = 0; index < bytesRead; index++)
            {
                byte value = buffer[index];
                if (value != 0 && (value < 0x20 || value > 0x7E))
                {
                    hasNonAsciiByte = true;
                    break;
                }
            }

            if (!hasNonAsciiByte)
            {
                return false;
            }

            string text;
            try
            {
                text = new UnicodeEncoding(bigEndian, byteOrderMark: false, throwOnInvalidBytes: true)
                    .GetString(buffer, 0, bytesRead);
            }
            catch (DecoderFallbackException)
            {
                return false;
            }

            var distinctRunes = new HashSet<Rune>();
            foreach (Rune rune in text.EnumerateRunes())
            {
                if (!IsTextRune(rune))
                {
                    return false;
                }

                distinctRunes.Add(rune);
            }

            // Sparse NULs alone are ambiguous; require a varied valid decoding to avoid mistaking ASCII or binary data for UTF-16.
            return distinctRunes.Count >= 3;
        }

        private static bool IsTextRune(Rune rune)
        {
            UnicodeCategory category = Rune.GetUnicodeCategory(rune);
            return Rune.IsLetterOrDigit(rune) ||
                Rune.IsWhiteSpace(rune) ||
                Rune.IsPunctuation(rune) ||
                Rune.IsSymbol(rune) ||
                category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format;
        }
    }
}
