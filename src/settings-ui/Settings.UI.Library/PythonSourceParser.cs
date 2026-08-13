// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.PowerToys.Settings.UI.Library;

public static class PythonSourceParser
{
    public static IReadOnlyList<string> GetCodeLines(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        string multilineDelimiter = null;
        char? stringDelimiter = null;
        bool escaped = false;

        foreach (var line in lines)
        {
            var code = new StringBuilder(line.Length);

            for (int index = 0; index < line.Length;)
            {
                if (multilineDelimiter is not null)
                {
                    if (MatchesToken(line, index, multilineDelimiter) && !IsEscaped(line, index))
                    {
                        code.Append(' ', multilineDelimiter.Length);
                        index += multilineDelimiter.Length;
                        multilineDelimiter = null;
                    }
                    else
                    {
                        code.Append(' ');
                        index++;
                    }

                    continue;
                }

                var character = line[index];
                if (stringDelimiter is not null)
                {
                    code.Append(' ');
                    if (escaped)
                    {
                        escaped = false;
                    }
                    else if (character == '\\')
                    {
                        escaped = true;
                    }
                    else if (character == stringDelimiter)
                    {
                        stringDelimiter = null;
                    }

                    index++;
                    continue;
                }

                if (character == '#')
                {
                    code.Append(' ', line.Length - index);
                    break;
                }

                if (MatchesToken(line, index, "\"\"\"") || MatchesToken(line, index, "'''"))
                {
                    multilineDelimiter = line.Substring(index, 3);
                    code.Append(' ', 3);
                    index += 3;
                    continue;
                }

                if (character is '\'' or '"')
                {
                    stringDelimiter = character;
                    code.Append(' ');
                    index++;
                    continue;
                }

                code.Append(character);
                index++;
            }

            escaped = false;
            result.Add(code.ToString());
        }

        return result;
    }

    private static bool MatchesToken(string value, int index, string token) =>
        index + token.Length <= value.Length &&
        value.AsSpan(index, token.Length).SequenceEqual(token);

    private static bool IsEscaped(string value, int index)
    {
        int backslashCount = 0;
        for (int current = index - 1; current >= 0 && value[current] == '\\'; current--)
        {
            backslashCount++;
        }

        return backslashCount % 2 != 0;
    }
}
