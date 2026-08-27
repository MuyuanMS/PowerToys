// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using System.Text;
using Microsoft.CmdPal.Ext.TimeDate.Helpers;
using Microsoft.CmdPal.Ext.TimeDate.Pages;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using Windows.Foundation;

namespace Microsoft.CmdPal.Ext.TimeDate;

public sealed partial class TimeDateCommandsProvider : CommandProvider
{
    private readonly CommandItem _command;
    private static readonly SettingsManager _settingsManager = new SettingsManager();
    private static readonly CompositeFormat MicrosoftPluginTimedatePluginDescription = System.Text.CompositeFormat.Parse(Resources.Microsoft_plugin_timedate_plugin_description);
    private static readonly TimeDateExtensionPage _timeDateExtensionPage = new(_settingsManager);
    private readonly FallbackTimeDateItem _fallbackTimeDateItem = new(_settingsManager);

    private readonly NowDockBand _bandItem;
    private readonly ListItem _notificationCenterBandItem;
    private readonly WrappedDockItem _clockDockBand;
    private readonly WrappedDockItem _notificationCenterDockBand;
    private readonly TypedEventHandler<object, Settings> _settingsChangedHandler;

    public TimeDateCommandsProvider()
    {
        DisplayName = Resources.Microsoft_plugin_timedate_plugin_name;
        Id = "com.microsoft.cmdpal.builtin.datetime";
        _command = new CommandItem(_timeDateExtensionPage)
        {
            Icon = _timeDateExtensionPage.Icon,
            Title = Resources.Microsoft_plugin_timedate_plugin_name,
            MoreCommands = [new CommandContextItem(_settingsManager.Settings.SettingsPage)],
        };

        Icon = _timeDateExtensionPage.Icon;
        Settings = _settingsManager.Settings;

        _bandItem = new NowDockBand(_settingsManager);
        _notificationCenterBandItem = new NotificationCenterDockBand();
        _clockDockBand = new WrappedDockItem(
            [_bandItem],
            "com.microsoft.cmdpal.timedate.clock",
            Resources.Microsoft_plugin_timedate_dock_band_title)
        {
            Icon = _timeDateExtensionPage.Icon,
        };
        _notificationCenterDockBand = new WrappedDockItem(
            [_notificationCenterBandItem],
            "com.microsoft.cmdpal.timedate.notificationCenter",
            Resources.timedate_notification_center_band_title);

        // Update the band immediately when the user changes a setting (e.g. the week
        // number mode). Stored as a field so Dispose can unsubscribe from the static
        // settings instance again.
        _settingsChangedHandler = (s, a) => _bandItem.Refresh();
        _settingsManager.Settings.SettingsChanged += _settingsChangedHandler;
    }

    public override void Dispose()
    {
        _settingsManager.Settings.SettingsChanged -= _settingsChangedHandler;
        _bandItem.Dispose();
        base.Dispose();
    }

    private string GetTranslatedPluginDescription()
    {
        // The extra strings for the examples are required for correct translations.
        var timeExample = Resources.Microsoft_plugin_timedate_plugin_description_example_time + "::" + DateTime.Now.ToString("T", CultureInfo.CurrentCulture);
        var dayExample = Resources.Microsoft_plugin_timedate_plugin_description_example_day + "::" + DateTime.Now.ToString("d", CultureInfo.CurrentCulture);
        var calendarWeekExample = Resources.Microsoft_plugin_timedate_plugin_description_example_calendarWeek + "::" + DateTime.Now.ToString("d", CultureInfo.CurrentCulture);
        return string.Format(CultureInfo.CurrentCulture, MicrosoftPluginTimedatePluginDescription, Resources.Microsoft_plugin_timedate_plugin_description_example_day, dayExample, timeExample, calendarWeekExample);
    }

    public override ICommandItem[] TopLevelCommands() => [_command];

    public override IFallbackCommandItem[] FallbackCommands() => [_fallbackTimeDateItem];

    public override ICommandItem[] GetDockBands()
    {
        return [_clockDockBand, _notificationCenterDockBand];
    }
}

#pragma warning disable SA1402 // File may only contain a single type

internal sealed partial class NotificationCenterDockBand : ListItem
{
    public NotificationCenterDockBand()
    {
        Icon = Icons.NotificationCenterIcon; // Notification bell
        Title = Resources.timedate_notification_center_band_title;
        Command = new OpenUrlCommand("ms-actioncenter:")
        {
            Id = "com.microsoft.cmdpal.timedate.notificationCenterBand",
            Name = Resources.timedate_show_notification_center_command_name,
            Result = CommandResult.Dismiss(),
        };
    }
}

#pragma warning restore SA1402 // File may only contain a single type
