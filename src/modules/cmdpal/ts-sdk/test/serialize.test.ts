// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { describe, expect, it, vi } from 'vitest';
import type { Content, ContextItem, IFallbackCommandItem, IListItem } from '../src/types.js';
import { WireSerializer } from '../src/runtime/serialize.js';

describe('WireSerializer.contextItems', () => {
  it('serializes a flat context item without a moreCommands field', () => {
    const items: ContextItem[] = [{ command: { id: 'copy', name: 'Copy' }, title: 'Copy' }];

    const wire = new WireSerializer().contextItems(items);

    expect(wire).toHaveLength(1);
    expect(wire[0]).not.toHaveProperty('moreCommands');
    expect(wire[0]?.command).toMatchObject({ id: 'copy', name: 'Copy' });
  });

  it('serializes nested moreCommands recursively two levels deep', () => {
    const items: ContextItem[] = [
      {
        command: { id: 'share', name: 'Share' },
        title: 'Share',
        moreCommands: [
          {
            command: { id: 'share-email', name: 'Email' },
            title: 'Email',
            moreCommands: [{ command: { id: 'share-email-work', name: 'Work' }, title: 'Work' }],
          },
          { command: { id: 'share-link', name: 'Copy link' }, title: 'Copy link' },
        ],
      },
    ];

    const wire = new WireSerializer().contextItems(items);

    const level1 = wire[0]?.moreCommands as Array<Record<string, unknown>>;
    expect(level1).toHaveLength(2);
    expect(level1[0]).toMatchObject({
      command: { id: 'share-email', name: 'Email' },
      title: 'Email',
    });

    const level2 = level1[0]?.moreCommands as Array<Record<string, unknown>>;
    expect(level2).toHaveLength(1);
    expect(level2[0]).toMatchObject({
      command: { id: 'share-email-work', name: 'Work' },
      title: 'Work',
    });
    expect(level2[0]).not.toHaveProperty('moreCommands');

    // The sibling without nested commands omits the moreCommands field entirely.
    expect(level1[1]).not.toHaveProperty('moreCommands');
  });

  it('registers every nested command so the host can invoke it later', () => {
    const registered: string[] = [];
    const items: ContextItem[] = [
      {
        command: { id: 'outer', name: 'Outer' },
        title: 'Outer',
        moreCommands: [{ command: { id: 'inner', name: 'Inner' }, title: 'Inner' }],
      },
    ];

    new WireSerializer((command) => registered.push(command.id)).contextItems(items);

    expect(registered).toContain('outer');
    expect(registered).toContain('inner');
  });
});

describe('WireSerializer.listItem', () => {
  it('serializes textToSuggest when set', () => {
    const item: IListItem = {
      command: { id: 'pick', name: 'Pick' },
      title: 'Person 1',
      textToSuggest: '@Person 1 ',
    };

    const wire = new WireSerializer().listItem(item);

    expect(wire.textToSuggest).toBe('@Person 1 ');
  });

  it('omits textToSuggest when not set', () => {
    const item: IListItem = { command: { id: 'plain', name: 'Plain' }, title: 'Plain' };

    const wire = new WireSerializer().listItem(item);

    expect(wire).not.toHaveProperty('textToSuggest');
  });
});

describe('WireSerializer.commandItem fallback ids', () => {
  it('serializes an explicit fallback item id separately from its command id', () => {
    const item: IFallbackCommandItem = {
      id: 'fallback-item',
      command: { id: 'fallback-command', name: 'Search' },
      title: 'Search',
    };

    const wire = new WireSerializer().commandItem(item);

    expect(wire).toMatchObject({
      id: 'fallback-item',
      command: { id: 'fallback-command' },
    });
  });

  describe('WireSerializer.content', () => {
    it('assigns generated form ids in traversal order for async tree children', async () => {
      let resolveFirst!: (children: Content[]) => void;
      let resolveSecond!: (children: Content[]) => void;
      const firstChildren = new Promise<Content[]>((resolve) => {
        resolveFirst = resolve;
      });
      const secondChildren = new Promise<Content[]>((resolve) => {
        resolveSecond = resolve;
      });
      const content: Content = {
        type: 'tree',
        rootContent: { type: 'plainText', text: 'root' },
        getChildren: () => [
          {
            type: 'tree',
            rootContent: { type: 'plainText', text: 'first' },
            getChildren: () => firstChildren,
          },
          {
            type: 'tree',
            rootContent: { type: 'plainText', text: 'second' },
            getChildren: () => secondChildren,
          },
        ],
      };
      const serializer = new WireSerializer();
      let nextId = 0;
      const collector = {
        reserve: vi.fn(),
        nextId: vi.fn(() => {
          const id = `form-${String(nextId)}`;
          nextId += 1;
          return id;
        }),
        register: vi.fn(),
      };

      const resultPromise = serializer.content(content, collector);
      resolveSecond([
        {
          type: 'form',
          templateJson: '{}',
          dataJson: '{}',
          submitForm: () => ({ kind: 'dismiss' }),
        },
      ]);
      await new Promise<void>((resolve) => setImmediate(resolve));
      resolveFirst([
        {
          type: 'form',
          templateJson: '{}',
          dataJson: '{}',
          submitForm: () => ({ kind: 'dismiss' }),
        },
      ]);

      const result = (await resultPromise) as {
        children: Array<{ children: Array<{ formId: string }> }>;
      };
      expect(result.children[0]?.children[0]?.formId).toBe('form-0');
      expect(result.children[1]?.children[0]?.formId).toBe('form-1');
    });
  });

  it('uses the command id when a fallback item id is omitted', () => {
    const item: IFallbackCommandItem = {
      command: { id: 'fallback-command', name: 'Search' },
      title: 'Search',
    };

    expect(new WireSerializer().commandItem(item).id).toBe('fallback-command');
  });
});
