// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { afterEach, describe, expect, it } from 'vitest';
import { ObservableBase } from '../src/base/ObservableBase.js';
import { setNotificationSink } from '../src/runtime/notifications.js';

class TestObservable extends ObservableBase {
  protected readonly notificationId = 'observable';
  title = 'Title';

  publishTitle(): void {
    this.notifyPropChanged('title');
  }

  publishCommand(): void {
    this.notifyPropChanged('command' as never);
  }
}

describe('ObservableBase', () => {
  afterEach(() => {
    setNotificationSink(null);
  });

  it('sends direct property values in property-change notifications', () => {
    const sent: Array<{ method: string; params: unknown }> = [];
    setNotificationSink((method, params) => sent.push({ method, params }));

    new TestObservable().publishTitle();

    expect(sent).toEqual([
      {
        method: 'command/propChanged',
        params: { commandId: 'observable', properties: { title: 'Title' } },
      },
    ]);
  });

  it('rejects complex properties that require serializer registration', () => {
    expect(() => new TestObservable().publishCommand()).toThrow(
      'Property "command" requires an items-changed notification.',
    );
  });
});
