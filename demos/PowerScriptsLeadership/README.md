# PowerScripts leadership demo backup

This is a **separate backup branch**, not the active UX-development branch or an official release.
The source snapshot includes the current editor UX work, including uncommitted changes captured without changing the original worktree/index. See `Source snapshot.json` for its base commit and captured-file hashes.

**Start on a clean Windows ARM64 machine:** paste [BACKUP-MACHINE.txt](BACKUP-MACHINE.txt) into your local coding agent. It is a concise end-to-end handoff, including cloning, script/input preparation, opening the input folder and the live Remove hyphens body. [SETUP-PROMPT.txt](SETUP-PROMPT.txt) supplies the detailed build, registration and rehearsal instructions it references.

| Consumer | Script | Contract |
|---|---|---|
| Advanced Paste | Paste a Markdown table | text -> text; no options |
| Advanced Paste | Clean copied PDF text | text -> text; no options |
| File Explorer | Combine CSV reports | 1-20 CSVs -> new HTML file; no options |
| File Explorer | Extract text from image | one PNG/JPEG -> separate OCR TXT; no options |
| File Explorer | Create a file summary | 1-20 files of any type -> HTML inventory + SHA-256; no options; up to 256 MB total |
| Advanced Paste + File Explorer | Rotate image 90 degrees | one PNG/JPEG -> PNG bitmap; fixed clockwise 90 degrees; no options |
| CmdPal + Keyboard Manager | Switch to dark theme | no primary input/options; fixed Light Switch CLI action |
| CmdPal + Keyboard Manager | Switch to light theme | no primary input/options; fixed Light Switch CLI action |

Eight prepared scripts give each consumer at least two relevant choices, with the two no-input theme actions reused across CmdPal and Keyboard Manager. OCR belongs to the Explorer image-selection demo, not a CmdPal file-picker flow.

Start with `SHOW FIRST.txt` for exact sample paths and expected results. `Create from template\Remove hyphens.txt` is the small PowerShell body for the live template-creation segment. `Inputs` contains synthetic CSV/text/image samples, including a mixed-type `General files` folder; `Icons` includes matching custom ICOs.

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
- Creates fresh IDs and leaves all eight packages **untrusted**. The image example narrows the generic template's MIME types to PNG/JPEG input and PNG output, matching its implementation.
- Does not execute scripts, approve trust, change configuration/policies, archive existing packages or create hotkeys.

IDs are written to ignored, machine-local `installed-demo.json`. Never copy another machine's `trust.json`, runtime configuration or keyboard bindings.

## Run order

1. Create **Remove hyphens** live: PowerShell, text -> text, no options; paste the TXT body, use `Icons\hyphens.ico`, save and review normally. Show **Consumer modules > Advanced Paste** activation. Copy the hyphen input and invoke through Advanced Paste.
2. Advanced Paste: copy `Inputs\Clipboard - PDF paragraphs.txt`; choose **Clean copied PDF text**. Show repaired `auto-` + line break + `mation`, preserved paragraph breaks and the unchanged `in-line` hyphen. Then copy the CSV-table input and show **Paste a Markdown table**.
3. Explorer: select `Inputs\Screenshots\Meeting notes.png`; invoke **Extract text from image**. Open the newly exported TXT and show the three recognized action items. Then select **both** report CSVs and invoke **Combine CSV reports**. Open the exported HTML manually: 2 files, 6 rows, 4 columns. For an any-file example, select the three files in `Inputs\General files` and choose **Create a file summary**: show names, sizes, three types and full SHA-256 fingerprints in a new HTML report. It also works with a single file, PDFs, Office files, binary and extensionless files without parsing their formats. Originals stay unchanged.
4. CmdPal: search **Switch to dark theme** and invoke it directly; use **Switch to light theme** for the opposite action. No file picker or transformation form.
5. Keyboard Manager: through its normal UI, bind the SAME dark/light scripts to confirmed-unused shortcuts, e.g. Ctrl+Alt+Shift+D / Ctrl+Alt+Shift+L. Review/approve first. Show that two consumers invoke the same script IDs, not copied implementations. Record the original theme before any test; setting light is not guaranteed to restore an initially mixed app/system theme.

**Visual image option:** `Inputs\Screenshots\sample-arrow.png` is the original blue arrow. Copy its bitmap and choose **Rotate image 90 degrees** in Advanced Paste, then paste into a scratch image-capable app. Or select the PNG in Explorer and export the result. The arrow turns from right (320 x 180) to down (180 x 320), with no angle parameter and no change to the original.

## Two obvious existing CLI modules

**Light Switch:** the dark/light scripts fix `Command` to `dark` / `light`, respectively. They do not expose a confusing action dropdown or change scheduling. **Advanced Paste:** Explorer OCR fixes its CLI action to `image-to-text` and binds the selected image as primary input.

To explain CLI-backed creation, show **New script > PowerToys action**, select the module/action and fix these arguments instead of exposing parameters. The importer uses the same Host scaffolding.

OCR uses **offline Windows OCR by default**, with an optional Azure Computer Vision backend in this demo build's CLI. It is not an LLM placeholder. OCR and theme changes use fixed, approved host CLI recipes; their host actions are **outside MXC**, while the script remains contained. The generic file-transform recipe declares network capability; OCR still fixes its action and has no provider, prompt, custom action or destination path. Keep MXC and its restrictions enabled.

## Optional Azure OCR

Follow [AZURE-OCR.txt](AZURE-OCR.txt). From the clone root, launch the matching native setup window:

```powershell
& .\ARM64\Debug\WinUI3Apps\PowerToys.AdvancedPaste.Cli.exe azure-ocr-setup
```

Enter the Computer Vision resource's **Endpoint** and **Key 1 or Key 2** from Azure Portal's **Keys and Endpoint** page. No separate region field is required. The masked key is stored in Windows Credential Vault; do not put it in a script, descriptor, parameter, prompt, terminal command or this repository. Saving the profile uploads nothing.

With a profile saved, CLI `image-to-text` uploads the selected image to that Azure resource and charges may apply. This is a CLI-wide opt-in, not a change to the regular Advanced Paste UI or an LLM provider. Online-AI policy is honored. Azure errors are explicit; there is no silent offline fallback. **Use offline Windows OCR** in setup removes only the owned demo profile. No profile means the original offline path.

The backend uses Computer Vision **Read 3.2**, rather than relying on Image Analysis 4.0's narrower regional availability. It submits the image once, then polls only the same HTTPS resource with a bounded deadline. The resource must support Read 3.2. Targeted mocked HTTP/routing/CLI tests and real Host offline regression passed on the source x64 machine. A valid user-entered key and an actual Azure call are still needed to verify that resource and cloud accuracy. Do not equate mocked tests or saving a profile with authenticated Azure success.

## Optional sandbox-access diagnostic

[ACCESS-CHECK.txt](ACCESS-CHECK.txt) adds **Check script access** separately, without changing the eight core imports:

```powershell
& .\demos\PowerScriptsLeadership\Create-DemoScripts.ps1 -HostPath .\ARM64\Debug\PowerScripts\PowerScripts.Host.exe -DiagnosticsOnly
```

Select `Inputs\Access checks\Sandbox canary.txt` in Explorer and run it to export a readable HTML report. It separates resolved MXC settings from observed canary/workspace write access, one anonymous Microsoft HTTPS HEAD, and administrator-role status. It does not read clipboard contents, inspect personal files, alter inputs or change policies. Failed connectivity is not automatically labeled an MXC block. Desktop/clipboard access is explicitly **not probed**.

The report describes the contained script, not approved host CLI actions such as Azure OCR. Imports remain untrusted. Diagnostic IDs are saved separately in ignored `installed-diagnostics.json`. Keep MXC enabled; disabling it can reduce local startup but does not accelerate Azure processing or remove service polling.

## What this backup does not claim

Fresh imports are **untrusted and not rehearsed on the destination machine**: normal user approval and execution consent are still required. An ARM64 build has not been run on the original x64 machine. A clean clone is not automatically demo-ready: finish the prompt's build and all four real consumer checks.

Old local demo collections and the private leadership PPTX/PDF are intentionally not in this public branch. The slides have a separate OneDrive backup. No credentials, trust database, personal PowerToys settings or compiled binaries are bundled.
