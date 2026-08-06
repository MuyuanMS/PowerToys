// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.CommandLine;
using System.Globalization;
using System.Linq;

namespace FancyZonesCLI.CommandLine;

internal static class FancyZonesCliUsage
{
    public static void PrintUsage()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine(Properties.Resources.usage_title);
        Console.WriteLine();

        var cmd = FancyZonesCliCommandFactory.CreateRootCommand();

        Console.WriteLine(Properties.Resources.usage_syntax);
        Console.WriteLine();

        Console.WriteLine(Properties.Resources.usage_options);
        foreach (var option in cmd.Options)
        {
            var aliases = string.Join(", ", option.Aliases);
            var description = option.Description ?? string.Empty;
            Console.WriteLine($"  {aliases,-30} {description}");
        }

        Console.WriteLine();
        Console.WriteLine(Properties.Resources.usage_commands);
        foreach (var command in cmd.Subcommands)
        {
            if (command.IsHidden)
            {
                continue;
            }

            // Format: "command-name <args>, alias"
            string argsLabel = string.Join(" ", command.Arguments.Select(a => $"<{a.Name}>"));
            string baseLabel = string.IsNullOrEmpty(argsLabel) ? command.Name : $"{command.Name} {argsLabel}";

            // Find first alias (Aliases includes Name)
            string alias = command.Aliases.FirstOrDefault(a => !string.Equals(a, command.Name, StringComparison.OrdinalIgnoreCase));
            string label = string.IsNullOrEmpty(alias) ? baseLabel : $"{baseLabel}, {alias}";

            var description = command.Description ?? string.Empty;
            Console.WriteLine($"  {label,-30} {description}");
        }

        Console.WriteLine();
        Console.WriteLine(Properties.Resources.usage_examples);
        Console.WriteLine("  PowerToys.FancyZones.CLI.exe --help");
        Console.WriteLine("  PowerToys.FancyZones.CLI.exe --version");
        Console.WriteLine("  PowerToys.FancyZones.CLI.exe get-monitors");
        Console.WriteLine("  PowerToys.FancyZones.CLI.exe set-layout focus");
        Console.WriteLine("  PowerToys.FancyZones.CLI.exe set-layout <uuid> --monitor 1");
        Console.WriteLine("  PowerToys.FancyZones.CLI.exe get-hotkeys");
    }

    public static void PrintCommandUsage(string commandName)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        var rootCmd = FancyZonesCliCommandFactory.CreateRootCommand();

        // Find matching subcommand by name or alias
        var subcommand = rootCmd.Subcommands.FirstOrDefault(c =>
            c.Aliases.Any(a => string.Equals(a, commandName, StringComparison.OrdinalIgnoreCase)));

        if (subcommand == null)
        {
            Console.WriteLine(string.Format(CultureInfo.InvariantCulture, Properties.Resources.usage_unknown_command, commandName));
            Console.WriteLine();
            Console.WriteLine(Properties.Resources.usage_run_help);
            return;
        }

        // Command name and description
        Console.WriteLine($"{Properties.Resources.usage_command} {subcommand.Name}");
        if (!string.IsNullOrEmpty(subcommand.Description))
        {
            Console.WriteLine($"  {subcommand.Description}");
        }

        Console.WriteLine();

        // Usage line
        string argsLabel = string.Join(" ", subcommand.Arguments.Select(a => $"<{a.Name}>"));
        string optionsLabel = subcommand.Options.Any() ? " [options]" : string.Empty;
        Console.WriteLine($"Usage: PowerToys.FancyZones.CLI.exe {subcommand.Name} {argsLabel}{optionsLabel}".TrimEnd());
        Console.WriteLine();

        // Aliases
        var aliases = subcommand.Aliases.Where(a => !string.Equals(a, subcommand.Name, StringComparison.OrdinalIgnoreCase)).ToList();
        if (aliases.Count > 0)
        {
            Console.WriteLine($"{Properties.Resources.usage_aliases} {string.Join(", ", aliases)}");
            Console.WriteLine();
        }

        // Arguments
        if (subcommand.Arguments.Any())
        {
            Console.WriteLine(Properties.Resources.usage_arguments);
            foreach (var arg in subcommand.Arguments)
            {
                var argDescription = arg.Description ?? string.Empty;
                Console.WriteLine($"  <{arg.Name}>{(arg.Arity.MinimumNumberOfValues == 0 ? $" {Properties.Resources.usage_optional}" : string.Empty),-20} {argDescription}");
            }

            Console.WriteLine();
        }

        // Options
        if (subcommand.Options.Any())
        {
            Console.WriteLine(Properties.Resources.usage_options);
            foreach (var option in subcommand.Options)
            {
                var optAliases = string.Join(", ", option.Aliases);
                var optDescription = option.Description ?? string.Empty;
                Console.WriteLine($"  {optAliases,-25} {optDescription}");
            }

            Console.WriteLine();
        }

        // Command-specific examples
        PrintCommandExamples(subcommand.Name);
    }

    private static void PrintCommandExamples(string commandName)
    {
        Console.WriteLine(Properties.Resources.usage_examples);

        switch (commandName.ToLowerInvariant())
        {
            case "get-monitors":
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe get-monitors");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe m");
                break;

            case "get-layouts":
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe get-layouts");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe ls");
                break;

            case "get-active-layout":
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe get-active-layout");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe active");
                break;

            case "set-layout":
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe set-layout focus");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe set-layout columns --monitor 1");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe set-layout {uuid} --all");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe s rows -m 2");
                break;

            case "open-editor":
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe open-editor");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe e");
                break;

            case "open-settings":
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe open-settings");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe settings");
                break;

            case "get-hotkeys":
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe get-hotkeys");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe hk");
                break;

            case "set-hotkey":
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe set-hotkey 1 {layout-uuid}");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe shk 2 0CEBCBA9-9C32-4395-B93E-DC77485AD6D0");
                break;

            case "remove-hotkey":
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe remove-hotkey 1");
                Console.WriteLine("  PowerToys.FancyZones.CLI.exe rhk 2");
                break;

            default:
                Console.WriteLine($"  PowerToys.FancyZones.CLI.exe {commandName}");
                break;
        }
    }
}
