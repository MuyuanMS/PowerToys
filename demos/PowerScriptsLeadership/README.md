# PowerScripts leadership demo backup

This is a **separate backup branch**, not the active UX-development branch or an official release.
The source snapshot includes the current editor UX work, including uncommitted changes captured without changing the original worktree/index. See `Source snapshot.json` for its base commit and captured-file hashes.

**Start on a clean Windows ARM64 machine:** paste [SETUP-PROMPT.txt](SETUP-PROMPT.txt) into your local coding agent. It covers cloning, native ARM64 prerequisites, the complete build, consumer registration, script creation, normal approval and rehearsal.

| Consumer | Script | Contract |
|---|---|---|
| Advanced Paste | Paste a Markdown table | text -> text; no options |
| File Explorer | Combine CSV reports | 1-20 CSVs -> new HTML file; no options |
| Command Palette | Read a screenshot | no primary input -> text; one screenshot picker |
| Keyboard Manager | Toggle light and dark | no primary input -> CLI status text; visible theme change |

`Create from template\Remove hyphens.txt` is the small PowerShell body for the live template-creation segment. `Inputs` contains synthetic CSV/text/image samples; `Icons` contains five matching custom ICOs.

## Import after building

From the clone root, in PowerShell 7:

```powershell
& .\demos\PowerScriptsLeadership\Check-DemoBuild.ps1 -BuildRoot .\ARM64\Debug
& .\demos\PowerScriptsLeadership\Create-DemoScripts.ps1 -HostPath .\ARM64\Debug\PowerScripts\PowerScripts.Host.exe
```

The importer:

- Scaffolds through the matching Host, so CLI launch paths are generated for **this installation**, not copied from the original x64 machine.
- Respects an explicit `-ScriptsRoot`, then `POWERSCRIPTS_ROOT`, configured `scriptsRoot`, then the normal LocalAppData root.
- Refuses duplicate demo titles rather than overwriting existing packages.
- Creates fresh IDs and leaves all four packages **untrusted**.
- Does not execute scripts, approve trust, change configuration/policies, archive existing packages or create hotkeys.

IDs are written to ignored, machine-local `installed-demo.json`. Never copy another machine's `trust.json`, runtime configuration or keyboard bindings.

## Run order

1. Create **Remove hyphens** live: PowerShell, text -> text, no options; paste the TXT body, use `Icons\hyphens.ico`, save and review normally. Show **Consumer modules > Advanced Paste** activation. Copy the hyphen input and invoke through Advanced Paste.
2. Advanced Paste: copy `Inputs\Clipboard - CSV table.txt`; choose **Paste a Markdown table**. Show the resulting Markdown.
3. Explorer: select **both** CSVs in `Inputs\Reports`; invoke **PowerScripts > Combine CSV reports**. The new HTML is exported alongside the selected files when they share a folder; open it manually. Expect 2 source files, 6 rows, 4 data columns. Originals stay unchanged.
4. CmdPal: search **Read a screenshot**, choose `Inputs\Screenshots\Meeting notes.png`, run and show the recognized action items.
5. Keyboard Manager: use its normal UI to bind **Toggle light and dark** to a confirmed-unused shortcut, e.g. Ctrl+Alt+Shift+D. Review/approve before silent invocation. Test only with permission: once switches theme, again switches back. Check the original app/system themes first; two toggles are not guaranteed to restore a mixed initial theme.

OCR is real **local Windows OCR**, not an LLM placeholder. OCR and theme changes use fixed, approved host CLI recipes; their host actions are **outside MXC**, while the script remains contained. The generic file-transform recipe declares network capability, but this fixed OCR configuration has no provider, prompt, custom action or destination path. Keep MXC and its restrictions enabled.

## What this backup does not claim

The original packages passed discovery/schema/icon/syntax checks. They had **not been executed or rehearsed** because normal user approval and execution consent were pending. An ARM64 build has not been run on the original x64 machine. A clean clone is not automatically demo-ready: finish the prompt's build and all four real consumer checks.

Old local demo collections and the private leadership PPTX/PDF are intentionally not in this public branch. The slides have a separate OneDrive backup. No credentials, trust database, personal PowerToys settings or compiled binaries are bundled.
