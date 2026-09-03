// Copyright (c) Microsoft Corporation
// The Microsoft Corporation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

/**
 * Opens a URL in the user's default browser. Command Palette runs on Windows,
 * so the default opener uses the shell `start` verb; macOS and Linux fallbacks
 * are provided so the SDK behaves during local development on other platforms.
 */

import { spawn, type ChildProcess } from 'node:child_process';

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

function detachLauncher(child: ChildProcess): void {
  child.on('error', (error) => {
    process.stderr.write(`Failed to launch URL opener: ${error.message}\n`);
  });
  child.unref();
}

/**
 * Opens a URL in the default browser via the platform launcher.
 *
 * @throws Error when the URL contains a double quote or a control character,
 * which could break out of the launcher command line.
 */
export const openUrlInDefaultBrowser: UrlOpener = (url) => {
  if (url.includes('"') || hasControlCharacters(url)) {
    throw new Error(`Refusing to open a URL with quote or control characters: ${url}`);
  }

  if (process.platform === 'win32') {
    const child = spawn('rundll32.exe', ['url.dll,FileProtocolHandler', url], {
      detached: true,
      stdio: 'ignore',
      windowsHide: true,
    });
    detachLauncher(child);
    return;
  }

  const launcher = process.platform === 'darwin' ? 'open' : 'xdg-open';
  const child = spawn(launcher, [url], { detached: true, stdio: 'ignore' });
  detachLauncher(child);
};
