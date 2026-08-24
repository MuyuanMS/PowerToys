// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

import { CopyTextCommand, ExtensionHost, InvokableCommandBase, ListItemBase, ListPageBase, NoOpCommand } from '@microsoft/cmdpal-sdk';
import type { CommandResult, IListItem } from '@microsoft/cmdpal-sdk';
import { icon } from '../util.js';

class CopyCurrentTimestampCommand extends InvokableCommandBase {
  readonly id = 'copy-current-timestamp';
  readonly name = 'Copy timestamp';

  override invoke(): CommandResult {
    ExtensionHost.copyToClipboard(new Date().toLocaleString());
    return { kind: 'showToast', args: { message: 'Copied timestamp' } };
  }
}

/**
 * A demo of clipboard integration. Mirrors the C# `SampleDataTransferPage`.
 *
 * The native C# sample attaches a `DataPackage` to each list item to enable
 * drag and drop (including delayed and image payloads). The JS protocol still
 * lacks a `DataPackage` surface on `IListItem`, so this parity sample keeps the
 * existing text scenarios by exposing copy-to-clipboard commands instead.
 */
export class SampleDataTransferPage extends ListPageBase {
  readonly id = 'sample-data-transfer-page';
  readonly name = 'Open';
  readonly title = 'Clipboard and Drag-and-Drop Demo';

  override icon = icon('\uE8C8');

  override getItems(): IListItem[] {
    return [
      new ListItemBase({
        command: new CopyTextCommand('Text data in the Data Package', 'Copy text', 'Copied text'),
        title: 'Item with plain text',
        subtitle: 'Copy plain text to the clipboard (drag and drop is not supported from JS)',
      }),
      new ListItemBase({
        command: new CopyCurrentTimestampCommand(),
        title: 'Item with a lazily rendered plain text',
        subtitle: 'The C# sample renders this lazily on drag; here it is copied when invoked',
      }),
      new ListItemBase({
        command: new NoOpCommand('data-transfer-image'),
        title: 'Item with an image',
        subtitle: 'The C# sample drags a bitmap and a file; image payloads are not supported from JS',
        icon: icon('\uEB9F'),
      }),
    ];
  }
}
