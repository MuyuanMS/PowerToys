// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import type { DirectObservablePropertyName, ObservablePropertyName } from '../types.js';
import { sendNotification } from '../runtime/notifications.js';

const DirectObservablePropertyNames = new Set<ObservablePropertyName>([
  'id',
  'name',
  'icon',
  'title',
  'isLoading',
  'accentColor',
  'searchText',
  'placeholderText',
  'showDetails',
  'filters',
  'gridProperties',
  'hasMoreItems',
  'subtitle',
  'tags',
  'section',
  'textToSuggest',
  'displayTitle',
]);

/** Shared property-change notification support for observable SDK models. */
export abstract class ObservableBase {
  protected abstract readonly notificationId: string;

  /**
   * Tells the host that one of this object's ABI properties changed.
   * The current value is included so the host can update without a round trip.
   */
  protected notifyPropChanged(propertyName: DirectObservablePropertyName): void {
    if (!DirectObservablePropertyNames.has(propertyName)) {
      throw new Error(`Property "${propertyName}" requires an items-changed notification.`);
    }

    sendNotification('command/propChanged', {
      commandId: this.notificationId,
      properties: { [propertyName]: Reflect.get(this, propertyName) },
    });
  }
}
