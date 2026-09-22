// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;

namespace RobocopyUI.Helpers
{
    internal sealed class RCJParser
    {
        public sealed record RCJCommand(string Command, string? Argument);

        private readonly string _input;

        internal RCJParser(string input)
        {
            _input = input;
        }

        public RCJCommand[] Parse()
        {
            var commands = new List<RCJCommand>();

            foreach (var line in _input.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            {
                var content = line.Trim();
                if (content.Length == 0 || content.StartsWith("::", StringComparison.Ordinal))
                {
                    continue;
                }

                var start = content.IndexOf('/');
                if (start < 0)
                {
                    continue;
                }

                content = content[start..];
                var separator = FindSeparator(content);
                var command = separator < 0 ? content[1..] : content[1..separator];
                if (command.Length == 0)
                {
                    continue;
                }

                var argumentStart = separator < 0 ? command.Length + 1 : separator + 1;
                var argument = argumentStart < content.Length ? content[argumentStart..].Trim() : string.Empty;
                commands.Add(new RCJCommand(command, argument.Length == 0 ? null : Unquote(argument)));
            }

            return [.. commands];
        }

        private static int FindSeparator(string content)
        {
            var inQuotes = false;
            for (var index = 1; index < content.Length; index++)
            {
                var character = content[index];
                if (character == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes && (character == ':' || char.IsWhiteSpace(character)))
                {
                    return index;
                }
            }

            return -1;
        }

        private static string Unquote(string argument)
        {
            return argument.Length >= 2 && argument[0] == '"' && argument[^1] == '"'
                ? argument[1..^1]
                : argument;
        }
    }
}
