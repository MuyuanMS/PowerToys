> Mirrored from microsoft/PowerToys PR 49700 for review iteration

<!-- Enter a brief description/summary of your PR here. What does it fix/what does it change/how was it tested (even manually, if necessary)? -->
## Summary of the Pull Request

Fixes delayed Always On Top sound cues by dispatching each asynchronous cue immediately after a successful pin or unpin state change, before synchronous border creation or destruction.

<!-- Please review the items on the PR checklist before submitting-->
## PR Checklist

- [ ] Closes: #xxx
<!--  - [ ] Closes: #yyy (add separate lines for additional resolved issues) -->
- [x] **Communication:** I've discussed this with core contributors already. If the work hasn't been agreed, this work might be rejected
- [ ] **Tests:** Added/updated and all pass
- [x] **Localization:** All end-user-facing strings can be localized
- [ ] **Dev docs:** Added/updated
- [ ] **New binaries:** Added on the required places
   - [ ] [JSON for signing](https://github.com/microsoft/PowerToys/blob/main/.pipelines/ESRPSigning_core.json) for new binaries
   - [ ] [WXS for installer](https://github.com/microsoft/PowerToys/blob/main/installer/PowerToysSetup/Product.wxs) for new binaries and localization folder
   - [ ] [YML for CI pipeline](https://github.com/microsoft/PowerToys/blob/main/.pipelines/ci/templates/build-powertoys-steps.yml) for new test projects
   - [ ] [YML for signed pipeline](https://github.com/microsoft/PowerToys/blob/main/.pipelines/release.yml)
- [ ] **Documentation updated:** If checked, please file a pull request on [our docs repo](https://github.com/MicrosoftDocs/windows-uwp/tree/docs/hub/powertoys) and link it here: #xxx

<!-- Provide a more detailed description of the PR, other things fixed, or any additional comments/features here -->
## Detailed Description of the Pull Request / Additional comments

`AlwaysOnTop::ProcessCommand` previously called `AssignBorder` before playing the On cue and erased the tracked border before playing the Off cue. Those synchronous DWM/D2D border operations could delay dispatch even though `Sound::Play` already uses asynchronous `PlaySound`.

This change moves playback into the successful pin and unpin branches, immediately after the window state transition succeeds and before border work begins. The existing sound-enabled check remains in place, so failed state changes and disabled sound still produce no cue, while successful transitions produce exactly one On or Off cue. Existing telemetry, system-menu, transparency, and border behavior is unchanged.

<!-- Describe how you validated the behavior. Add automated tests wherever possible, but list manual validation steps taken as well -->
## Validation Steps Performed

- Restored repository build prerequisites with `tools\build\build-essentials.cmd`.
- Built `src\modules\alwaysontop\AlwaysOnTop` in Release x64 using `tools\build\build.ps1 -Platform x64 -Configuration Release`.
- No focused unit-test seam currently exists for `AlwaysOnTop::ProcessCommand`; no interactive latency measurement was performed.

