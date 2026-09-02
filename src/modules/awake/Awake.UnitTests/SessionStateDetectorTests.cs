// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Awake.Core.Native;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Awake.UnitTests;

[SupportedOSPlatform("windows")]
[TestClass]
public class SessionStateDetectorTests
{
    private const int SessionInfoSize = 20;
    private const int LevelOffset = 0;
    private const int SessionFlagsOffset = 16;

    [TestMethod]
    public void TryReadLockState_WhenSessionIsLocked_ReturnsLocked()
    {
        using SessionInfoBuffer buffer = CreateSessionInfoBuffer(level: 1, sessionFlags: 0);

        bool known = SessionStateDetector.TryReadLockState(buffer.Pointer, SessionInfoSize, out var isLocked);

        Assert.IsTrue(known);
        Assert.IsTrue(isLocked);
    }

    [TestMethod]
    public void TryReadLockState_WhenSessionIsUnlocked_ReturnsUnlocked()
    {
        using SessionInfoBuffer buffer = CreateSessionInfoBuffer(level: 1, sessionFlags: 1);

        bool known = SessionStateDetector.TryReadLockState(buffer.Pointer, SessionInfoSize, out var isLocked);

        Assert.IsTrue(known);
        Assert.IsFalse(isLocked);
    }

    [TestMethod]
    public void TryReadLockState_WhenLevelIsUnknown_ReturnsUnknown()
    {
        using SessionInfoBuffer buffer = CreateSessionInfoBuffer(level: 2, sessionFlags: 0);

        bool known = SessionStateDetector.TryReadLockState(buffer.Pointer, SessionInfoSize, out var isLocked);

        Assert.IsFalse(known);
        Assert.IsFalse(isLocked);
    }

    [TestMethod]
    public void TryReadLockState_WhenBufferIsUndersized_ReturnsUnknown()
    {
        using SessionInfoBuffer buffer = CreateSessionInfoBuffer(level: 1, sessionFlags: 0);

        bool known = SessionStateDetector.TryReadLockState(buffer.Pointer, SessionInfoSize - 1, out var isLocked);

        Assert.IsFalse(known);
        Assert.IsFalse(isLocked);
    }

    private static SessionInfoBuffer CreateSessionInfoBuffer(int level, int sessionFlags)
    {
        var buffer = new SessionInfoBuffer(SessionInfoSize);
        Marshal.WriteInt32(buffer.Pointer, LevelOffset, level);
        Marshal.WriteInt32(buffer.Pointer, SessionFlagsOffset, sessionFlags);
        return buffer;
    }

    private sealed class SessionInfoBuffer : IDisposable
    {
        public SessionInfoBuffer(int size)
        {
            Pointer = Marshal.AllocHGlobal(size);
            Span<byte> empty = stackalloc byte[size];
            Marshal.Copy(empty.ToArray(), 0, Pointer, size);
        }

        public IntPtr Pointer { get; }

        public void Dispose()
        {
            Marshal.FreeHGlobal(Pointer);
        }
    }
}
