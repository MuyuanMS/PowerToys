// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { describe, expect, it, vi } from 'vitest';
import { NoOpCommand, OpenUrlCommand, CopyTextCommand } from '../src/index.js';
import { serializeCommandResult } from '../src/runtime/commandResult.js';

describe('NoOpCommand', () => {
  it('keeps the palette open', () => {
    expect(new NoOpCommand().invoke()).toEqual({ kind: 'keepOpen' });
  });
});

describe('OpenUrlCommand', () => {
  it('opens the URL through the injected opener and dismisses', () => {
    const opener = vi.fn();
    const command = new OpenUrlCommand('https://example.com/?a=1&b=2', 'Example', opener);

    const result = command.invoke();

    expect(opener).toHaveBeenCalledTimes(1);
    expect(opener).toHaveBeenCalledWith('https://example.com/?a=1&b=2');
    expect(result).toEqual({ kind: 'dismiss' });
  });

  it('does not lose the URL to a dropped result argument', () => {
    // Regression: the URL is delivered by the opener side effect, not by a
    // result argument that the dismiss serialization would discard.
    const opener = vi.fn();
    const command = new OpenUrlCommand('https://example.com/', undefined, opener);

    const wire = serializeCommandResult(command.invoke());

    expect(wire).toEqual({ Kind: 0 });
    expect(opener).toHaveBeenCalledWith('https://example.com/');
  });

  it('keeps the target URL out of the generated id and default name', () => {
    const url = 'https://example.com/?token=secret';
    const command = new OpenUrlCommand(url, undefined, vi.fn());

    expect(command.id).toMatch(/^open-url:[0-9a-f]{16}$/);
    expect(command.id).not.toContain(url);
    expect(command.name).toBe('Open');
  });

  it('derives stable, distinct ids from the full target URL', () => {
    const first = new OpenUrlCommand('https://example.com/?value=one', undefined, vi.fn());
    const same = new OpenUrlCommand('https://example.com/?value=one', undefined, vi.fn());
    const different = new OpenUrlCommand('https://example.com/?value=two', undefined, vi.fn());

    expect(first.id).toBe(same.id);
    expect(first.id).not.toBe(different.id);
  });
});

describe('CopyTextCommand id derivation', () => {
  it('gives two different strings that share their first 24 characters distinct ids', () => {
    // Both strings share the same leading 24 characters; a slice-based id would
    // collide, but hashing the full payload must keep them distinct.
    const prefix = 'the-same-first-24-charss';
    const a = new CopyTextCommand(`${prefix}-alpha`);
    const b = new CopyTextCommand(`${prefix}-beta`);

    expect(prefix.length).toBe(24);
    expect(a.id).not.toBe(b.id);
  });

  it('is stable for identical payloads', () => {
    const a = new CopyTextCommand('hello', 'Copy hello');
    const b = new CopyTextCommand('hello', 'Copy hello');
    expect(a.id).toBe(b.id);
  });

  it('honors an explicit author-supplied id', () => {
    const command = new CopyTextCommand('hello', 'Copy', 'Copied', 'my-stable-id');
    expect(command.id).toBe('my-stable-id');
  });
});
