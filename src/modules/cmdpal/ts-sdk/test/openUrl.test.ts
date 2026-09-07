// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { EventEmitter } from 'node:events';
import { afterEach, describe, expect, it, vi } from 'vitest';

const spawnMock = vi.hoisted(() => vi.fn());

vi.mock('node:child_process', () => ({
  spawn: spawnMock,
}));

const { openUrlInDefaultBrowser } = await import('../src/runtime/openUrl.js');

describe('openUrlInDefaultBrowser', () => {
  afterEach(() => {
    vi.restoreAllMocks();
    spawnMock.mockReset();
  });

  it('handles asynchronous launcher failures without throwing', () => {
    const child = new EventEmitter() as EventEmitter & { unref: () => void };
    child.unref = vi.fn();
    spawnMock.mockReturnValue(child);
    const stderr = vi.spyOn(process.stderr, 'write').mockImplementation(() => true);

    openUrlInDefaultBrowser('https://example.com/');

    expect(child.unref).toHaveBeenCalledOnce();
    expect(() => child.emit('error', new Error('spawn failed'))).not.toThrow();
    expect(stderr).toHaveBeenCalledWith('Failed to launch URL opener: spawn failed\n');
  });
});
