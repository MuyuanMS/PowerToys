// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { afterEach, describe, expect, it } from 'vitest';
import { ContentPageBase } from '../src/base/ContentPageBase.js';
import { ListPageBase } from '../src/base/ListPageBase.js';
import { setNotificationSink } from '../src/runtime/notifications.js';

class TestContentPage extends ContentPageBase {
  readonly id = 'content-page';
  readonly name = 'Content';
  readonly title = 'Content';

  getContent() {
    return [];
  }

  refresh(): void {
    this.notifyItemsChanged();
  }
}

class TestListPage extends ListPageBase {
  readonly id = 'list-page';
  readonly name = 'List';
  readonly title = 'List';

  getItems() {
    return [];
  }

  refresh(): void {
    this.notifyItemsChanged();
  }
}

describe('ContentPageBase', () => {
  afterEach(() => {
    setNotificationSink(null);
  });

  it('notifies the host when content changes', () => {
    const sent: Array<{ method: string; params: unknown }> = [];
    setNotificationSink((method, params) => sent.push({ method, params }));

    new TestContentPage().refresh();

    expect(sent).toEqual([
      {
        method: 'contentPage/itemsChanged',
        params: { pageId: 'content-page' },
      },
    ]);
  });
});

describe('ListPageBase', () => {
  afterEach(() => {
    setNotificationSink(null);
  });

  it('notifies the host when list items change', () => {
    const sent: Array<{ method: string; params: unknown }> = [];
    setNotificationSink((method, params) => sent.push({ method, params }));

    new TestListPage().refresh();

    expect(sent).toEqual([
      {
        method: 'listPage/itemsChanged',
        params: { pageId: 'list-page' },
      },
    ]);
  });
});
