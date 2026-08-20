// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
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
        private const int SampleSize = 8000;

        public static Task<bool> IsTextFileAsync(string path, CancellationToken cancellationToken)
        {
            return Task.Run(() => IsTextFile(path, cancellationToken), cancellationToken);
        }

        private static bool IsTextFile(string path, CancellationToken cancellationToken)
        {
            try
            {
                using var stream = ReadHelper.OpenReadOnly(path);
                int bytesToRead = (int)Math.Min(SampleSize, stream.Length);
                if (bytesToRead == 0)
                {
                    return true;
                }

                var buffer = new byte[bytesToRead];
                int bytesRead = stream.Read(buffer, 0, bytesToRead);
                cancellationToken.ThrowIfCancellationRequested();

                if (HasUnicodeByteOrderMark(buffer, bytesRead) || IsLikelyUnicodeText(buffer, bytesRead))
                {
                    return true;
                }

                // A NUL byte in the sample is a strong signal of binary content.
                for (int i = 0; i < bytesRead; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (buffer[i] == 0)
                    {
                        return false;
                    }
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.LogError("Failed to determine if file is text: " + ex.Message);
                return false;
            }
        }

        private static bool HasUnicodeByteOrderMark(byte[] buffer, int bytesRead)
        {
            return bytesRead >= 2 &&
                ((buffer[0] == 0xFF && buffer[1] == 0xFE) ||
                (buffer[0] == 0xFE && buffer[1] == 0xFF) ||
                (bytesRead >= 4 && buffer[0] == 0x00 && buffer[1] == 0x00 && buffer[2] == 0xFE && buffer[3] == 0xFF));
        }

        private static bool IsLikelyUnicodeText(byte[] buffer, int bytesRead)
        {
            return IsLikelyUtf16Text(buffer, bytesRead) || IsLikelyUtf32Text(buffer, bytesRead);
        }

        private static bool IsLikelyUtf16Text(byte[] buffer, int bytesRead)
        {
            int pairs = bytesRead / 2;
            if (pairs < 2)
            {
                return false;
            }

            int littleEndianAsciiPairs = 0;
            int bigEndianAsciiPairs = 0;
            for (int i = 0; i + 1 < bytesRead; i += 2)
            {
                if (buffer[i] != 0 && buffer[i + 1] == 0)
                {
                    littleEndianAsciiPairs++;
                }
                else if (buffer[i] == 0 && buffer[i + 1] != 0)
                {
                    bigEndianAsciiPairs++;
                }
            }

            return littleEndianAsciiPairs * 10 >= pairs * 8 || bigEndianAsciiPairs * 10 >= pairs * 8;
        }

        private static bool IsLikelyUtf32Text(byte[] buffer, int bytesRead)
        {
            int groups = bytesRead / 4;
            if (groups < 2)
            {
                return false;
            }

            int littleEndianAsciiGroups = 0;
            int bigEndianAsciiGroups = 0;
            for (int i = 0; i + 3 < bytesRead; i += 4)
            {
                if (buffer[i] != 0 && buffer[i + 1] == 0 && buffer[i + 2] == 0 && buffer[i + 3] == 0)
                {
                    littleEndianAsciiGroups++;
                }
                else if (buffer[i] == 0 && buffer[i + 1] == 0 && buffer[i + 2] == 0 && buffer[i + 3] != 0)
                {
                    bigEndianAsciiGroups++;
                }
            }

            return littleEndianAsciiGroups * 10 >= groups * 8 || bigEndianAsciiGroups * 10 >= groups * 8;
        }
    }
}
