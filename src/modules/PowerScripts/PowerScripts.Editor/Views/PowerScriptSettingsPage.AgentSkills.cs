// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Globalization;
using ManagedCommon;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PowerScripts.Core.AgentSkills;
using PowerScripts.UI.Helpers;
using Windows.ApplicationModel.DataTransfer;

namespace PowerScripts.Editor.Views;

public sealed partial class PowerScriptSettingsPage
{
    private void CopyAgentSkillInstructions_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var resources = ResourceLoaderInstance.ResourceLoader;
            var instructions = string.Format(
                CultureInfo.CurrentCulture,
                resources.GetString("PowerScripts_AgentSetupInstructions"),
                AgentSkillBundle.GetDirectory(AppContext.BaseDirectory),
                _context.Script.EntryFullPath,
                _context.Script.EntryFullPath + ".tool.json");
            var data = new DataPackage();
            data.SetText(instructions);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            AgentSkillFeedback.Severity = InfoBarSeverity.Success;
            AgentSkillFeedback.Message = resources.GetString("PowerScripts_AgentInstructionsCopied");
        }
        catch (Exception exception)
        {
            Logger.LogError("Unable to copy the PowerScripts agent skill setup instructions.", exception);
            AgentSkillFeedback.Severity = InfoBarSeverity.Error;
            AgentSkillFeedback.Message = exception.Message;
        }

        AgentSkillFeedback.IsOpen = true;
    }
}
