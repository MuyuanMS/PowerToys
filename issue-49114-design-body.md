> Mirrored from a PowerToys issue for AI-assisted fixing. Upstream reference: Issue 49114 (plain text, intentionally not linked).

## Original report

- PowerToys version: 0.100.2, installed by auto-update.
- Area: Advanced Paste.
- Reproduction: open Advanced Paste with the recorded shortcut, then click the Clipboard history button.
- Expected: the clipboard history view opens and remains usable.
- Actual: Advanced Paste freezes and must be force-closed; Windows reported an application hang.
- Environment evidence: the supplied recording shows the freeze immediately after the Clipboard history click. The diagnostic bundle contains repeated `Application Hang` / `AppHangB1` events for `PowerToys.AdvancedPaste.exe` versions 0.100.1 and 0.100.2.

## Key details from the discussion

- No upstream comments, assignee, linked pull request, or duplicate was found during triage.
- The diagnostic event is an application hang rather than a managed exception, which is consistent with a blocking WinRT clipboard call.
- The local evidence and source history identify clipboard-history loading as the failing path.

## Fix design (converged after 2 adversary rounds)

### Inferred root cause

`MainPage.LoadClipboardHistoryEvent` originally dispatched `LoadClipboardHistoryAsync` through `Task.Run`. That moves `Clipboard.GetHistoryItemsAsync` and `DataPackageView` reads such as `GetTextAsync` from the WinUI page's STA/UI thread to a thread-pool MTA thread. WinRT clipboard history APIs require STA affinity and can block when invoked from MTA, producing the reported Advanced Paste application hang when the history view is opened.

Evidence: `src/modules/AdvancedPaste/AdvancedPaste/AdvancedPasteXAML/Pages/MainPage.xaml.cs` contains the `Task.Run` wrapper immediately before the calls to `Clipboard.GetHistoryItemsAsync` and `item.Content.GetTextAsync`; the diagnostic bundle records repeated `AppHangB1` events at the same user action.

### Fix plan

1. Keep all WinRT clipboard-history reads on the page's STA/UI thread.
2. In `LoadClipboardHistoryEvent`, call `LoadClipboardHistoryAsync` directly when `_dispatcherQueue.HasThreadAccess` is true; otherwise marshal the call with `_dispatcherQueue.TryEnqueue`.
3. Leave the existing dispatcher handoff for collection and bitmap UI updates in place.
4. Preserve the existing exception handling and history-disabled behavior. Do not add background work around the WinRT clipboard calls.
5. Keep the change atomic in `MainPage.xaml.cs`; use the existing Advanced Paste UI test path to validate the behavior.

Risk is low: this changes only the apartment in which clipboard reads execute. The asynchronous method still yields during WinRT reads, and UI collection updates remain marshaled to the dispatcher. A slow clipboard provider may make loading occur on the UI thread, but it is the required apartment and avoids the indefinite MTA hang.

### How to verify

1. On a Windows test machine with clipboard history enabled, launch Advanced Paste and click Clipboard history repeatedly with text and image history entries present.
2. Confirm the history list opens, populates, and remains responsive; selecting and deleting an item still works.
3. Trigger a clipboard-history change while the page is open and confirm the list refreshes without freezing.
4. Confirm the disabled-history path remains non-failing.
5. Build the Advanced Paste project and run the existing Advanced Paste UI tests where the environment permits.

### Confidence

High — the supplied WER evidence is an application hang, the failing code path performs STA-required WinRT clipboard operations from `Task.Run`, and the fork's current `main` already contains the dispatcher-based correction.

### Adversary sign-off

No blocking objections after 2 rounds. Non-blocking note: the `HistoryChanged` callback's originating thread is not assumed; the dispatcher-access check and fallback enqueue cover either callback context.

## Task for Copilot

Implement the fix plan above. Diagnose from the evidence; keep the change atomic and buildable.
