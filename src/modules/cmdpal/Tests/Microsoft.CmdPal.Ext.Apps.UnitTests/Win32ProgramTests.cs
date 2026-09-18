// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.CmdPal.Ext.Apps.Programs;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.CmdPal.Ext.Apps.UnitTests;

[TestClass]
public class Win32ProgramTests
{
    private static readonly MethodInfo ProgramPathsMethod = typeof(Win32Program).GetMethod("ProgramPaths", BindingFlags.NonPublic | BindingFlags.Static)!;

    [TestMethod]
    public async Task ProgramPaths_IncludesFilesUnderDirectorySymlink()
    {
        using var tempRoot = new TemporaryDirectory();
        var scanRoot = tempRoot.CreateSubdirectory("ScanRoot");
        var targetDirectory = tempRoot.CreateSubdirectory("ExternalTarget");
        var expectedPath = Path.Combine(scanRoot, "AppsLink", "OutsideApp.exe");
        File.WriteAllText(Path.Combine(targetDirectory, "OutsideApp.exe"), string.Empty);

        CreateDirectorySymlink(Path.Combine(scanRoot, "AppsLink"), targetDirectory);

        var results = await InvokeProgramPathsAsync(scanRoot);

        CollectionAssert.AreEquivalent(new[] { expectedPath }, results);
    }

    [TestMethod]
    public async Task ProgramPaths_ShortCircuitsCircularDirectorySymlink()
    {
        using var tempRoot = new TemporaryDirectory();
        var scanRoot = tempRoot.CreateSubdirectory("ScanRoot");
        var childDirectory = Directory.CreateDirectory(Path.Combine(scanRoot, "Child")).FullName;
        var rootApp = Path.Combine(scanRoot, "RootApp.exe");
        var childApp = Path.Combine(childDirectory, "ChildApp.exe");
        File.WriteAllText(rootApp, string.Empty);
        File.WriteAllText(childApp, string.Empty);

        CreateDirectorySymlink(Path.Combine(childDirectory, "LoopToRoot"), scanRoot);

        var programPathsTask = InvokeProgramPathsAsync(scanRoot);
        var results = await programPathsTask.WaitAsync(TimeSpan.FromSeconds(5));

        CollectionAssert.AreEquivalent(new[] { rootApp, childApp }, results);
    }

    private static async Task<string[]> InvokeProgramPathsAsync(string directory)
    {
        return await Task.Run(() =>
        {
            var result = (IEnumerable<string>)ProgramPathsMethod.Invoke(null, new object[] { directory, new[] { "exe" }, true })!;
            return result.ToArray();
        });
    }

    private static void CreateDirectorySymlink(string linkPath, string targetPath)
    {
        try
        {
            System.IO.Directory.CreateSymbolicLink(linkPath, targetPath);
        }
        catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is PlatformNotSupportedException || ex is UnauthorizedAccessException)
        {
            Assert.Inconclusive($"Directory symbolic links are not available in this test environment: {ex.Message}");
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"{nameof(Win32ProgramTests)}_{Guid.NewGuid():N}");

        public TemporaryDirectory()
        {
            Directory.CreateDirectory(_root);
        }

        public string CreateSubdirectory(string relativePath)
        {
            var path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
    }
}
