// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { afterEach, describe, expect, it } from 'vitest';
import type { ICommandProvider } from '../src/types.js';
import { encodeMessage, MessageFramer } from '../src/runtime/framing.js';
import { startJsonRpcServer } from '../src/runtime/server.js';

type Writer = typeof process.stdout.write;

const originalStdoutWrite = process.stdout.write;
const originalStderrWrite = process.stderr.write;
const initialDataListeners = process.stdin.listeners('data');
const initialEndListeners = process.stdin.listeners('end');

afterEach(async () => {
  process.stdin.emit('end');
  await new Promise<void>((resolve) => setImmediate(resolve));
  for (const listener of process.stdin.listeners('data')) {
    if (!initialDataListeners.includes(listener)) {
      process.stdin.removeListener('data', listener as () => void);
    }
  }
  for (const listener of process.stdin.listeners('end')) {
    if (!initialEndListeners.includes(listener)) {
      process.stdin.removeListener('end', listener as () => void);
    }
  }
  process.stdout.write = originalStdoutWrite;
  process.stderr.write = originalStderrWrite;
});

function captureStdout(): string[] {
  const output: string[] = [];
  process.stdout.write = ((chunk: unknown): boolean => {
    output.push(typeof chunk === 'string' ? chunk : String(chunk));
    return true;
  }) as unknown as Writer;
  return output;
}

describe('startJsonRpcServer', () => {
  it('returns a JSON-RPC parse error for malformed request bodies', () => {
    const output = captureStdout();
    const provider: ICommandProvider = {
      id: 'test',
      displayName: 'Test',
      topLevelCommands: () => [],
    };
    startJsonRpcServer(() => provider);

    process.stdin.emit('data', Buffer.from('Content-Length: 1\r\n\r\n{', 'ascii'));

    const framer = new MessageFramer();
    const messages = framer
      .push(Buffer.from(output.join(''), 'utf8'))
      .map((body) => JSON.parse(body));
    expect(messages).toEqual([
      {
        jsonrpc: '2.0',
        id: null,
        error: { code: -32700, message: 'Parse error' },
      },
    ]);
  });

  it('returns an invalid request error for unsupported protocol messages', async () => {
    const output = captureStdout();
    const provider: ICommandProvider = {
      id: 'test',
      displayName: 'Test',
      topLevelCommands: () => [],
    };
    startJsonRpcServer(() => provider);

    process.stdin.emit('data', encodeMessage({ jsonrpc: '1.0', id: 1, method: 'initialize' }));
    await new Promise<void>((resolve) => setTimeout(resolve, 10));

    const framer = new MessageFramer();
    const messages = framer
      .push(Buffer.from(output.join(''), 'utf8'))
      .map((body) => JSON.parse(body));
    expect(messages).toEqual([
      {
        jsonrpc: '2.0',
        id: null,
        error: { code: -32600, message: 'Invalid Request' },
      },
    ]);
  });
});
