// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { describe, expect, it } from 'vitest';
import { isNotification, isRequest, JSONRPC_VERSION } from '../src/runtime/jsonrpc.js';

describe('JSON-RPC message guards', () => {
  it.each([
    { id: 1, method: 'initialize' },
    { jsonrpc: '1.0', id: 1, method: 'initialize' },
  ])('rejects requests without the JSON-RPC 2.0 marker', (message) => {
    expect(isRequest(message)).toBe(false);
  });

  it.each([{ method: 'dispose' }, { jsonrpc: '1.0', method: 'dispose' }])(
    'rejects notifications without the JSON-RPC 2.0 marker',
    (message) => {
      expect(isNotification(message)).toBe(false);
    },
  );

  it('accepts JSON-RPC 2.0 requests and notifications', () => {
    expect(isRequest({ jsonrpc: JSONRPC_VERSION, id: 1, method: 'initialize' })).toBe(true);
    expect(isNotification({ jsonrpc: JSONRPC_VERSION, method: 'dispose' })).toBe(true);
  });
});
