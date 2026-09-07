// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

/**
 * Opens a URL in the user's default browser. Command Palette runs on Windows,
 * so the default opener invokes the registered URL protocol handler; macOS and Linux fallbacks
 * are provided for local development on other platforms.
 */

import { spawn } from 'node:child_process';

/** Opens the given URL. Injected into {@link OpenUrlCommand} for testing. */
export type UrlOpener = (url: string) => void;

function hasControlCharacters(value: string): boolean {
  for (let index = 0; index < value.length; index += 1) {
    if (value.charCodeAt(index) < 0x20) {
      return true;
    }
  }
  return false;
}

/**
 * Opens a URL in the default browser via the platform launcher.
 *
 * @throws Error when the URL contains a double quote or a control character.
 */
export const openUrlInDefaultBrowser: UrlOpener = (url) => {
  if (url.includes('"') || hasControlCharacters(url)) {
    throw new Error(`Refusing to open a URL with quote or control characters: ${url}`);
  }

  if (process.platform === 'win32') {
    // FileProtocolHandler dispatches through the registered URL handler without
    // exposing the URL to cmd.exe expansion.
    const child = spawn('rundll32.exe', ['url.dll,FileProtocolHandler', url], {
      detached: true,
      stdio: 'ignore',
      windowsHide: true,
    });
    child.unref();
    return;
  }

  const launcher = process.platform === 'darwin' ? 'open' : 'xdg-open';
  const child = spawn(launcher, [url], { detached: true, stdio: 'ignore' });
  child.unref();
};
