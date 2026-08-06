// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.IO.Abstractions;

using Microsoft.PowerToys.Settings.UI.Library.Utilities;

namespace Microsoft.PowerToys.Settings.UI.Library
{
    public class SettingPath
    {
        private const string DefaultFileName = "settings.json";

        private readonly IDirectory _directory;

        private readonly IPath _path;

        public SettingPath(IDirectory directory, IPath path)
        {
            _directory = directory ?? throw new ArgumentNullException(nameof(directory));
            _path = path ?? throw new ArgumentNullException(nameof(path));
        }

        public SettingPath()
            : this(new FileSystem().Directory, new FileSystem().Path)
        {
        }

        private string GetModuleFolderPath(string powertoy = "") =>
            string.IsNullOrWhiteSpace(powertoy)
                ? _path.Combine(Helper.LocalApplicationDataFolder(), "Microsoft", "PowerToys")
                : _path.Combine(Helper.LocalApplicationDataFolder(), "Microsoft", "PowerToys", NormalizeRelativePath(powertoy, nameof(powertoy), true));

        private static string NormalizeRelativePath(string value, string parameterName, bool trimLeadingSeparators = false)
        {
            if (trimLeadingSeparators)
            {
                value = value.TrimStart(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar);
            }

            if (System.IO.Path.IsPathRooted(value))
            {
                throw new ArgumentException("The path must be relative to the PowerToys settings folder.", parameterName);
            }

            var segments = value.Split(new[] { System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < segments.Length; i++)
            {
                segments[i] = segments[i].TrimEnd(' ', '.');
                if (string.IsNullOrEmpty(segments[i]) || segments[i] == "." || segments[i] == "..")
                {
                    throw new ArgumentException("The path must not contain traversal segments.", parameterName);
                }
            }

            if (segments.Length == 0)
            {
                throw new ArgumentException("The path must not be empty.", parameterName);
            }

            return string.Join(System.IO.Path.DirectorySeparatorChar.ToString(), segments);
        }

        public bool SettingsFolderExists(string powertoy)
        {
            return _directory.Exists(GetModuleFolderPath(powertoy));
        }

        public void CreateSettingsFolder(string powertoy)
        {
            _directory.CreateDirectory(GetModuleFolderPath(powertoy));
        }

        public void DeleteSettings(string powertoy = "")
        {
            _directory.Delete(GetModuleFolderPath(powertoy));
        }

        /// <summary>
        /// Get path to the json settings file.
        /// </summary>
        /// <returns>string path.</returns>
        public string GetSettingsPath(string powertoy, string fileName = DefaultFileName)
        {
            if (string.IsNullOrEmpty(fileName))
            {
                return GetModuleFolderPath(powertoy) + System.IO.Path.DirectorySeparatorChar;
            }

            return _path.Combine(GetModuleFolderPath(powertoy), NormalizeRelativePath(fileName, nameof(fileName)));
        }
    }
}
