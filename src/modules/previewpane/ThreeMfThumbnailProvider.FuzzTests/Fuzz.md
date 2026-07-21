# Fuzzing Tests for ThreeMfThumbnailProvider

This project provides a libFuzzer (OneFuzz `libfuzzerDotNet`) target for the 3MF File Explorer
thumbnail provider, which parses untrusted `.3mf` packages (a ZIP/OPC container of XML model parts,
relationship parts and optional embedded images).

### Fuzz target
`ThreeMfThumbnailProvider.FuzzTests.FuzzTests.FuzzGetThumbnail` feeds arbitrary bytes as a candidate
`.3mf` package into `ThreeMfThumbnailProvider.GetThumbnail`, exercising:
- ZIP/OPC extraction and package/part relationship (`.rels`) parsing,
- OPC part-name resolution,
- embedded thumbnail image decoding, and
- 3MF mesh parsing / component (including Production Extension `p:path`) resolution and rendering.

`GetThumbnail` intentionally swallows malformed input and returns `null`; the fuzzer still surfaces the
failures that the loader's resource budgets are designed to prevent — hangs (unbounded loops),
stack overflows (deep component recursion) and memory exhaustion (decompression / geometry bombs).

### Integration
See the general instructions in
[Hosts.FuzzTests/Fuzz.md](../../Hosts/Hosts.FuzzTests/Fuzz.md) and
[AdvancedPaste.FuzzTests/Fuzz.md](../../AdvancedPaste/AdvancedPaste.FuzzTests/Fuzz.md).
`OneFuzzConfig.json` in this folder wires the target for the OneFuzz service. CI ingestion is already
covered by the existing wildcard download pattern (`**/tests/*.FuzzTests/**`) in
`.pipelines/v2/templates/job-fuzz.yml`, so no pipeline change is required for this project.
