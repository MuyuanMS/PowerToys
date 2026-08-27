## Sanitized mirror

This issue mirrors source report 49704 for design work (plain text, intentionally not linked). No private attachments or personal information are included.

### Problem

With Grab and Move enabled and the Windows key selected as the activation modifier, holding Win, dragging a window, releasing the mouse button first, and then releasing Win can open the Windows Start menu. Releasing Win before the mouse button does not open Start.

- Reported version: PowerToys 0.100.2
- Installation: WinGet
- Area: Grab and Move

### Reproduction / expected behavior

1. Select Win as the Grab and Move activation modifier.
2. Hold Win, press and drag a window, release the mouse button, then release Win.
3. Observe whether Start opens.
4. Repeat while releasing Win before the mouse button.

Expected: completing a Grab and Move drag must not invoke the shell's Win-key action, regardless of release order.
Actual: the mouse-first sequence can leave the shell seeing a Win release and opens Start.

### Inferred root cause

The low-level keyboard hook absorbs the Win key-down and uses `g_winAbsorbed`, `g_dragConsumedAlt`, and the live interaction flags to decide whether the later Win key-up must be swallowed. The mouse-first path ends the drag before the Win key-up is processed. That makes the correctness of the final key-up depend on transient state surviving foreground/overlay transitions (`WinEventProc`) and on the absorbed-modifier bookkeeping remaining synchronized with the physical key state. If that state is cleared or no longer classifies the completed drag as modifier-consuming, the real Win key-up is forwarded to Explorer, which opens Start.

Relevant paths (current fork main):

- `src/modules/GrabAndMove/GrabAndMove/main.cpp`: `KeyboardProc` Win-key handling.
- Same file: `MouseProc` drag completion and `EndInteraction`.
- Same file: `WinEventProc` foreground-change recovery and absorbed-key reset (around lines 220-261).`r`n- `KeyboardProc` Win key-up classification (around lines 1100-1156).`r`n- `MouseProc` non-modifier recovery and drag completion (around lines 1360-1400 and 1450-1570).

### Proposed design

1. Introduce an explicit per-hold `g_winInteractionConsumed` (or equivalent state-machine state) that is set when a Win-modified drag/resize is promoted past the drag threshold and remains set until the physical Win key-up is observed.
2. In the Win key-up path, swallow the key-up whenever this per-hold consumed state is set, even if the mouse interaction has already ended or foreground recovery cleared transient drag flags.
3. Keep the existing normal-click passthrough behavior: if no drag/resize was committed, replay the absorbed Win key-down and allow exactly one real Win key-up through; do not synthesize a shell action.
4. Make foreground-change recovery end the mouse interaction and clear pending mouse state, but not discard the per-hold consumed-modifier state while `GetAsyncKeyState` still reports either Win key held. Clear it only on the matching physical Win key-up (or a definitive session/input reset).
5. Handle LWIN/RWIN consistently and reset the state on module shutdown or a definitive lost-input recovery path.
6. Add focused unit-testable transition coverage for: drag then mouse-up then Win-up; drag then Win-up then mouse-up; click-only passthrough; resize; LWIN/RWIN; and foreground-change during drag. Manual verification remains required because Start-menu behavior is shell/UI integration.

This design does not change activation semantics, click passthrough, target exclusion, or overlay behavior.

### Verify / acceptance criteria

- Build the Grab and Move target and its maintained tests, if available.
- With Win selected, perform at least 20 repetitions of both release orders on a normal top-level window; Start never opens after a committed drag or resize.
- Repeat for LWIN and RWIN, maximized and restored windows, and a foreground/overlay transition during the drag.
- Confirm click-only Win+left-click still reaches the target and the Win key-up is not stuck or duplicated.
- Confirm Alt mode behavior and Win+right-click resize behavior are unchanged.
- Collect a short trace/log or video demonstrating both release orders and the click-only path.

### Confidence

Medium-high. The failure is release-order dependent, matching the split ownership between `MouseProc` and `KeyboardProc`; the current implementation has no durable “this Win hold already consumed a drag” state independent of the mouse interaction lifetime. Exact runtime confirmation of the foreground-change/reset timing is still needed on Windows 11 with PowerToys 0.100.2.

### Adversary review — round 1

- Objection: the proposed state must not be cleared merely because the drag ended; otherwise the original race remains.
- Objection: normal click passthrough must remain distinct from a committed drag, or Win key-down replay can be duplicated.
- Objection: both LWIN and RWIN and resize need explicit coverage.

Resolution: the design now uses a per-modifier-hold consumed bit, clears it only on matching key-up/definitive reset, and lists click-only, resize, and both Win keys in acceptance coverage.

### Adversary review — round 2

- Objection: foreground-change recovery can still leave stale state if the physical key-up is lost (for example, secure desktop/session switch).
- Objection: relying on a single boolean is unsafe if LWIN/RWIN transitions occur within one hold.

Resolution: define the recovery boundary as a definitive lost-input/session reset, track the absorbed Win virtual key (or an equivalent left/right-held mask), and test foreground changes plus both key variants. No blocking design objection remains.

### Design approval

Approved for implementation in the fork only. Do not modify or comment on the upstream issue until implementation and validation are separately approved.

