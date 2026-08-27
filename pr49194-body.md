> Mirrored from microsoft/PowerToys PR 49194 for review iteration

>[!WARNING]
>This PR is one in a series of PRs focused on rearchitecting the search/scoring logic of the MainListPage. The full explanation is in PR 49189.
>
> This PR should not be merged until PR 49195 is merged into it.

When you type, CmdPal shows instant results and fallback results whose labels are computed by an out-of-process extension and arrive later.

The old code ranked fallbacks before their labels arrived and did not re-rank them after the labels were populated. This change defers fallback scoring to the render path and re-scores using the current labels, so fallback positions match the displayed labels.

The change also includes focused unit tests for deterministic first paint, late fallback fold-in, no leapfrog over deterministic results, and superseding queries.
