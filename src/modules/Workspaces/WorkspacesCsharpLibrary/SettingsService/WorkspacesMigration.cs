// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.IO;
using System.Security.Principal;
using System.Text.Json;
using System.Threading;
using WorkspacesCsharpLibrary.Data;

namespace WorkspacesCsharpLibrary.SettingsService;

/// <summary>
/// One-shot legacy migration, called by explicit Workspaces bootstrap paths
/// (idempotent).
/// The service has no "migrate" concept: migration is
/// simply "read the legacy %LocalAppData% file once and PutBlob it through the
/// service".  A sentinel under %LocalAppData% short-circuits subsequent calls.
/// </summary>
public static class WorkspacesMigration
{
    private static string MigrationMutexName =>
        @"Global\PowerToys_Workspaces_SettingsMigration_" +
        (WindowsIdentity.GetCurrent().User?.Value ?? "Unknown");

    public enum Outcome
    {
        AlreadyMigrated,
        NothingToMigrate,
        Migrated,
        SkippedServiceUnavailable,
        SkippedLegacyUnreadable,
        SkippedServerRejected,
    }

    public static Outcome Run()
    {
        using var migrationMutex = new Mutex(false, MigrationMutexName);
        bool lockTaken;
        try
        {
            lockTaken = migrationMutex.WaitOne(TimeSpan.FromSeconds(30));
        }
        catch (AbandonedMutexException)
        {
            lockTaken = true;
        }

        if (!lockTaken)
        {
            return Outcome.SkippedServiceUnavailable;
        }

        try
        {
            return RunLocked();
        }
        finally
        {
            migrationMutex.ReleaseMutex();
        }
    }

    private static Outcome RunLocked()
    {
        var sentinel = SettingsPaths.MigrationSentinel();

        // If the service already holds a blob for this user, another runner
        // invocation migrated it; drop the sentinel and stop.
        var probe = PTSettingsClient.GetBlob(out var existing);
        if (probe == PTSettingsClient.Result.Ok && existing.Length > 0)
        {
            TryWriteSentinel(sentinel);
            return Outcome.AlreadyMigrated;
        }

        if (probe == PTSettingsClient.Result.Unavailable)
        {
            return Outcome.SkippedServiceUnavailable;
        }

        // probe is NotFound (no blob yet) or a transient error — proceed only
        // when we positively know there is nothing yet.
        if (probe != PTSettingsClient.Result.NotFound)
        {
            return Outcome.SkippedServerRejected;
        }

        var legacy = SettingsPaths.LegacyWorkspacesFile();
        if (!File.Exists(legacy))
        {
            TryWriteSentinel(sentinel);
            return Outcome.NothingToMigrate;
        }

        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(legacy);
        }
        catch (IOException)
        {
            return Outcome.SkippedLegacyUnreadable;
        }
        catch (System.UnauthorizedAccessException)
        {
            return Outcome.SkippedLegacyUnreadable;
        }

        try
        {
            var parsed = JsonSerializer.Deserialize(
                bytes,
                WorkspacesStorageJsonContext.Default.WorkspacesFile);
            if (parsed?.Workspaces == null)
            {
                return Outcome.SkippedLegacyUnreadable;
            }

            bytes = JsonSerializer.SerializeToUtf8Bytes(
                parsed,
                WorkspacesStorageJsonContext.Default.WorkspacesFile);
        }
        catch (System.Exception)
        {
            return Outcome.SkippedLegacyUnreadable;
        }

        var put = PTSettingsClient.PutBlob(bytes);
        switch (put)
        {
            case PTSettingsClient.Result.Ok:
                // Keep the legacy file as a backup for one release; the service
                // blob is the authority going forward.
                TryWriteSentinel(sentinel);
                return Outcome.Migrated;

            case PTSettingsClient.Result.Unavailable:
                return Outcome.SkippedServiceUnavailable;

            default:
                return Outcome.SkippedServerRejected;
        }
    }

    private static void TryWriteSentinel(string sentinel)
    {
        try
        {
            var dir = Path.GetDirectoryName(sentinel);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(sentinel, System.DateTime.UtcNow.ToString("o"));
        }
        catch (IOException)
        {
            // Best-effort: if we can't write the sentinel we simply re-probe
            // next time, which is cheap and idempotent.
        }
        catch (System.UnauthorizedAccessException)
        {
        }
    }
}
