// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.CropAndLock.TestApp;
using Microsoft.PowerToys.UITest.Next;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Windows.ApplicationModel;
using Windows.Management.Deployment;

namespace Microsoft.CropAndLock.UITests
{
    internal sealed class PackagedCropSource : CropSource
    {
        private const string PackageName = "Microsoft.PowerToys.CropAndLock.TestApp";
        private const string Publisher = "CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US";
        private const string ProcessName = "CropAndLock.TestApp";

        private readonly PackageManager packageManager = new();
        private bool ownsRegistration;
        private string? packageFullName;
        private Process? process;
        private global::Windows.Foundation.IAsyncOperationWithProgress<DeploymentResult, DeploymentProgress>? installationOperation;
        private Task<DeploymentResult>? installation;

        internal override void Open(TestContext context)
        {
            var packagePath = Path.Combine(AppContext.BaseDirectory, "CropAndLock.TestApp.msix");
            Assert.IsTrue(File.Exists(packagePath), $"The signed packaged fixture was not staged: {packagePath}.");
            Assert.HasCount(0, RegisteredPackages(), $"Remove the pre-existing {PackageName} registration before running this isolated suite.");
            Assert.HasCount(0, NativeMethods.ProcessIds(ProcessName), "The fixture app is already running; refusing to adopt an unrelated process.");

            context.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Installing signed fixture for the current user: {packagePath}");
            ownsRegistration = true;
            installationOperation = packageManager.AddPackageAsync(new Uri(packagePath), [], DeploymentOptions.None);
            installation = installationOperation.AsTask();
            var deployment = installation.WaitAsync(TimeSpan.FromSeconds(90)).GetAwaiter().GetResult();
            Assert.IsNull(deployment.ExtendedErrorCode, $"Fixture installation failed: {deployment.ErrorText}. Activity: {deployment.ActivityId}.");
            var packages = RegisteredPackages();
            Assert.HasCount(1, packages, "Expected exactly one current-user fixture registration.");
            var package = packages[0];
            packageFullName = package.Id.FullName;
            Assert.AreEqual(Publisher, package.Id.Publisher, "The fixture publisher does not match the shared test signing setup.");
            Assert.AreEqual(
                RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(),
                package.Id.Architecture.ToString().ToLowerInvariant(),
                "The fixture package architecture must match the test executable.");
            var applications = package.GetAppListEntriesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(15)).GetAwaiter().GetResult();
            var application = applications.Single(entry => entry.AppUserModelId == $"{package.Id.FamilyName}!App");
            context.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] Activating packaged fixture: {application.AppUserModelId}");
            Assert.IsTrue(
                application.LaunchAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult(),
                "Windows did not activate the installed fixture application.");

            var started = WaitHelper.WaitForStable(
                () => NativeMethods.ProcessIds(ProcessName),
                ids => ids?.Count == 1,
                timeoutMS: 20_000,
                requiredConsecutiveMatches: 3);
            Assert.IsTrue(started.Succeeded, "The packaged fixture did not start exactly one process.");
            process = Process.GetProcessById(started.LastObservation!.Single());
            Assert.AreEqual(package.Id.FullName, NativeMethods.PackageFullName(process.Id), "The source process did not receive the installed fixture's package identity.");
            Assert.IsFalse(ElevationHelper.IsProcessElevated(process.Id) ?? true, "The packaged source must run non-elevated.");

            var ready = WaitHelper.WaitForStable(
                () => WindowControl.EnumerateProcessWindows([process.Id])
                    .FirstOrDefault(window => window.IsVisible && window.Title.StartsWith(CropSourceForm.WindowTitlePrefix, StringComparison.Ordinal)),
                window => window.Hwnd != IntPtr.Zero && !WindowHelper.IsWindowCloaked(window.Hwnd),
                timeoutMS: 20_000,
                requiredConsecutiveMatches: 3);
            Window = ready.LastObservation.Hwnd;
            Assert.IsTrue(ready.Succeeded, "The packaged fixture did not expose its deterministic source window.");
            Assert.AreEqual(process.Id, NativeMethods.ProcessId(Window), "The source HWND must belong to the verified packaged process.");
            SetFixtureGeometry();
            context.WriteLine($"Packaged source: HWND=0x{Window:X}, PID={process.Id}, package={package.Id.FullName}, class={NativeMethods.ClassName(Window)}.");
        }

        public override void Dispose()
        {
            try
            {
                // Also discover a process whose activation succeeded just before Open failed.
                // Never close an unpackaged namesake or a process from a different package.
                foreach (var processId in NativeMethods.ProcessIds(ProcessName))
                {
                    Process owned;
                    try
                    {
                        owned = Process.GetProcessById(processId);
                    }
                    catch (ArgumentException)
                    {
                        continue;
                    }

                    using (owned)
                    {
                        if (owned.HasExited || packageFullName is null || NativeMethods.PackageFullName(processId) != packageFullName)
                        {
                            continue;
                        }

                        foreach (var window in WindowControl.EnumerateProcessWindows([processId]))
                        {
                            WindowControl.TryCloseWindow(window.Hwnd.ToInt64());
                        }

                        if (!owned.WaitForExit(5_000))
                        {
                            owned.Kill(entireProcessTree: true);
                            Assert.IsTrue(owned.WaitForExit(5_000), "The test-owned packaged source did not exit.");
                        }
                    }
                }
            }
            finally
            {
                process?.Dispose();
                if (ownsRegistration)
                {
                    if (installation is { IsCompleted: false })
                    {
                        // A timed-out install must not register its package after cleanup has finished.
                        installationOperation!.Cancel();
                        try
                        {
                            installation.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
                        }
                        catch (OperationCanceledException)
                        {
                        }
                    }

                    foreach (var package in RegisteredPackages().Where(package => package.Id.Publisher == Publisher))
                    {
                        var removal = packageManager.RemovePackageAsync(package.Id.FullName)
                            .AsTask().WaitAsync(TimeSpan.FromSeconds(90)).GetAwaiter().GetResult();
                        Assert.IsNull(removal.ExtendedErrorCode, $"Fixture removal failed: {removal.ErrorText}. Activity: {removal.ActivityId}.");
                    }

                    Assert.HasCount(0, RegisteredPackages(), "The fixture package remained registered after cleanup.");
                }
            }
        }

        private Package[] RegisteredPackages() =>
            packageManager.FindPackagesForUser(string.Empty).Where(package => package.Id.Name == PackageName).ToArray();
    }
}
