> Mirrored from microsoft/PowerToys PR 49325 for review iteration

> [!WARNING]
> Part of a 7-PR stack adding JavaScript/TypeScript extension support to Command Palette (PR 49321 -> PR 49323 -> PR 49324 -> PR 49325 -> PR 49326 -> PR 49329 -> PR 49364). This one targets `dev/mjolley/phase-3-cmdpal-adapters`, not `main`, so don't merge it before PR 49324 lands.

> [!NOTE]
> Want to try the whole thing end to end? Run the branch from PR 49364.

## What's going on

Phase 4 is making this work come alive. This layer spawns Node, handles the initialize handshake, finds installed extensions, and plugs them into the host.

