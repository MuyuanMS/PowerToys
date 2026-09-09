// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { afterEach, describe, expect, it, vi } from 'vitest';
import type { ICommandProvider } from '../src/types.js';
import { ExtensionRuntime } from '../src/runtime/runtime.js';
import { JsonRpcErrorCode, JSONRPC_VERSION, type JsonRpcMessage } from '../src/runtime/jsonrpc.js';
import { PROTOCOL_VERSION } from '../src/runtime/protocol.js';
import { encodeMessage, MessageFramer } from '../src/runtime/framing.js';
import { startJsonRpcServer } from '../src/runtime/server.js';

interface Harness {
  runtime: ExtensionRuntime;
  sent: JsonRpcMessage[];
  fatal: ReturnType<typeof vi.fn>;
}

function createHarness(): Harness {
  const sent: JsonRpcMessage[] = [];
  const fatal = vi.fn();
  const runtime = new ExtensionRuntime({
    send: (message) => sent.push(message),
    reportFatal: fatal,
  });
  return { runtime, sent, fatal };
}

function responseFor(sent: JsonRpcMessage[], id: number): Record<string, unknown> | undefined {
  return sent.find((m) => 'id' in m && (m as { id?: unknown }).id === id) as
    Record<string, unknown> | undefined;
}

type Writer = typeof process.stdout.write;

const originalStdoutWrite = process.stdout.write;
const originalStderrWrite = process.stderr.write;
const originalExitCode = process.exitCode;
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
  process.exitCode = originalExitCode;
  vi.restoreAllMocks();
});

function captureStdout(): string[] {
  const output: string[] = [];
  process.stdout.write = ((chunk: unknown): boolean => {
    output.push(typeof chunk === 'string' ? chunk : String(chunk));
    return true;
  }) as unknown as Writer;
  return output;
}

const provider: ICommandProvider = {
  id: 'ext',
  displayName: 'Ext',
  topLevelCommands() {
    return [];
  },
};

describe('initialization failure propagation', () => {
  it('answers initialize with an error when provider creation rejects', async () => {
    const { runtime, sent, fatal } = createHarness();
    runtime.beginInitialization(Promise.reject(new Error('creation boom')));

    await runtime.handleRequest({ jsonrpc: JSONRPC_VERSION, id: 1, method: 'initialize' });

    const response = responseFor(sent, 1);
    expect(response?.error).toMatchObject({ message: 'creation boom' });
    expect(fatal).toHaveBeenCalledWith(1);
  });

  it('answers initialize with an error when initialization throws', async () => {
    const { runtime, sent, fatal } = createHarness();
    runtime.beginInitialization(
      (async (): Promise<ICommandProvider> => {
        throw new Error('init threw');
      })(),
    );

    await runtime.handleRequest({ jsonrpc: JSONRPC_VERSION, id: 1, method: 'initialize' });

    expect(responseFor(sent, 1)?.error).toMatchObject({ message: 'init threw' });
    expect(fatal).toHaveBeenCalledWith(1);
  });

  it('rejects later requests instead of serving a broken provider', async () => {
    const { runtime, sent } = createHarness();
    runtime.beginInitialization(Promise.reject(new Error('creation boom')));

    await runtime.handleRequest({
      jsonrpc: JSONRPC_VERSION,
      id: 2,
      method: 'command/invoke',
      params: { commandId: 'x' },
    });

    expect(responseFor(sent, 2)?.error).toMatchObject({ message: 'creation boom' });
  });

  it('still answers initialize before server shutdown when provider creation fails early', async () => {
    const output = captureStdout();

    startJsonRpcServer(() => Promise.reject(new Error('creation boom')));
    await new Promise<void>((resolve) => setImmediate(resolve));

    process.stdin.emit(
      'data',
      encodeMessage({ jsonrpc: JSONRPC_VERSION, id: 1, method: 'initialize' }),
    );
    await new Promise<void>((resolve) => setImmediate(resolve));

    const frames = new MessageFramer()
      .push(Buffer.from(output.join(''), 'utf8'))
      .map((body) => JSON.parse(body) as Record<string, unknown>);

    expect(frames).toContainEqual(
      expect.objectContaining({
        id: 1,
        error: expect.objectContaining({ message: 'creation boom' }),
      }),
    );
    expect(process.exitCode).toBe(1);
  });
});

describe('handshake and version negotiation', () => {
  it('answers a compatible host with protocol and sdk versions', async () => {
    const { runtime, sent } = createHarness();
    runtime.setProvider(provider);

    await runtime.handleRequest({
      jsonrpc: JSONRPC_VERSION,
      id: 1,
      method: 'initialize',
      params: { protocolVersion: PROTOCOL_VERSION, hostVersion: '1.2.3' },
    });

    const result = responseFor(sent, 1)?.result as Record<string, unknown>;
    expect(result.protocolVersion).toBe(PROTOCOL_VERSION);
    expect(typeof result.sdkVersion).toBe('string');
    expect(result.capabilities).toEqual(['commands']);
    expect(runtime.negotiatedHostProtocolVersion).toBe(PROTOCOL_VERSION);
    expect(runtime.negotiatedHostVersion).toBe('1.2.3');
  });

  it('treats a missing host protocol version as a compatible legacy host', async () => {
    const { runtime, sent, fatal } = createHarness();
    runtime.setProvider(provider);

    await runtime.handleRequest({ jsonrpc: JSONRPC_VERSION, id: 1, method: 'initialize' });

    const response = responseFor(sent, 1);
    expect(response?.error).toBeUndefined();
    expect((response?.result as Record<string, unknown>).protocolVersion).toBe(PROTOCOL_VERSION);
    expect(runtime.negotiatedHostProtocolVersion).toBeUndefined();
    expect(fatal).not.toHaveBeenCalled();
  });

  it('rejects an incompatible major protocol version', async () => {
    const { runtime, sent, fatal } = createHarness();
    runtime.setProvider(provider);

    await runtime.handleRequest({
      jsonrpc: JSONRPC_VERSION,
      id: 1,
      method: 'initialize',
      params: { protocolVersion: PROTOCOL_VERSION + 1 },
    });

    const error = responseFor(sent, 1)?.error as { code: number; message: string } | undefined;
    expect(error?.code).toBe(JsonRpcErrorCode.InvalidRequest);
    expect(error?.message).toContain('Incompatible protocol version');
    expect(fatal).toHaveBeenCalledWith(1);
  });

  it('rejects a present-but-malformed protocol version distinctly from an absent one', async () => {
    const { runtime, sent, fatal } = createHarness();
    runtime.setProvider(provider);

    await runtime.handleRequest({
      jsonrpc: JSONRPC_VERSION,
      id: 1,
      method: 'initialize',
      params: { protocolVersion: 'not-a-number' },
    });

    const error = responseFor(sent, 1)?.error as { code: number; message: string } | undefined;
    expect(error?.code).toBe(JsonRpcErrorCode.InvalidRequest);
    expect(error?.message).toContain('Invalid protocol version');
    expect(error?.message).not.toContain('Incompatible protocol version');
    expect(fatal).toHaveBeenCalledWith(1);
  });

  it('rejects a non-integer numeric protocol version', async () => {
    const { runtime, sent, fatal } = createHarness();
    runtime.setProvider(provider);

    await runtime.handleRequest({
      jsonrpc: JSONRPC_VERSION,
      id: 1,
      method: 'initialize',
      params: { protocolVersion: 1.5 },
    });

    const error = responseFor(sent, 1)?.error as { code: number; message: string } | undefined;
    expect(error?.code).toBe(JsonRpcErrorCode.InvalidRequest);
    expect(error?.message).toContain('Invalid protocol version');
    expect(fatal).toHaveBeenCalledWith(1);
  });
});
