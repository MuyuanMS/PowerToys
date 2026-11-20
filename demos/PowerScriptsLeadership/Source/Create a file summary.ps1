param([string[]]$Files)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

if ($Files.Count -lt 1 -or $Files.Count -gt 20) {
    throw 'Select between 1 and 20 files of any type.'
}

function Encode([string]$Value) {
    return [Net.WebUtility]::HtmlEncode($Value)
}

function Format-Size([long]$Bytes) {
    if ($Bytes -ge 1MB) { return ('{0:0.##} MB' -f ($Bytes / 1MB)) }
    if ($Bytes -ge 1KB) { return ('{0:0.##} KB' -f ($Bytes / 1KB)) }
    return "$Bytes bytes"
}

$rows = [Text.StringBuilder]::new()
$types = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$totalBytes = 0L
foreach ($file in $Files) {
    if ([string]::IsNullOrWhiteSpace($file) -or -not [IO.File]::Exists($file)) {
        throw 'Every selection must be an existing, locally available regular file.'
    }

    $stream = [IO.File]::Open($file, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $length = $stream.Length
        $totalBytes += $length
        if ($totalBytes -gt 256MB) {
            throw 'Select files totaling no more than 256 MB for this quick summary.'
        }

        $algorithm = [Security.Cryptography.SHA256]::Create()
        try {
            $checksum = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
        }
        finally {
            $algorithm.Dispose()
        }
    }
    finally {
        $stream.Dispose()
    }

    $name = Encode ([IO.Path]::GetFileName($file))
    $extension = [IO.Path]::GetExtension($file).TrimStart('.').ToUpperInvariant()
    if ([string]::IsNullOrEmpty($extension)) { $extension = 'NO EXTENSION' }
    [void]$types.Add($extension)
    $type = Encode $extension
    $size = Format-Size $length
    [void]$rows.Append("<tr><td><strong>$name</strong><span class=`"type`">$type</span></td><td class=`"size`">$size</td><td><code>$checksum</code></td></tr>")
}

$totalSize = Format-Size $totalBytes
$html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>File summary</title>
<style>
:root{color-scheme:light dark;--bg:#f3f6fb;--paper:#fff;--ink:#17243d;--muted:#58677f;--line:#dce4ef;--accent:#225cbd;--tint:#edf3ff}
*{box-sizing:border-box}
body{margin:0;background:var(--bg);color:var(--ink);font:16px "Segoe UI",sans-serif}
main{max-width:1180px;margin:48px auto;padding:0 28px}
.eyebrow{font-size:13px;font-weight:600;letter-spacing:2px;color:var(--accent)}
h1{font-size:40px;line-height:1.15;letter-spacing:-1px;margin:16px 0}
.subtitle,.footer{color:var(--muted);line-height:1.6}
.cards{display:flex;flex-wrap:wrap;gap:16px;margin:28px 0}
.card{flex:1;min-width:180px;background:var(--paper);border:1px solid var(--line);border-radius:16px;padding:24px}
.number{font-size:32px;font-weight:600;color:var(--accent)}
.label{color:var(--muted);margin-top:4px}
.report{background:var(--paper);border:1px solid var(--line);border-radius:16px;overflow:auto}
table{width:100%;border-collapse:collapse;text-align:left}
th{background:var(--tint);font-size:13px;color:var(--accent);padding:18px 22px}
td{padding:20px 22px;border-top:1px solid var(--line);vertical-align:top}
td:first-child{min-width:210px;overflow-wrap:anywhere}
td:last-child{min-width:240px;max-width:490px}
strong{display:block;font-weight:600}
.type{display:inline-block;margin-top:8px;padding:4px 8px;border-radius:4px;background:var(--tint);color:var(--accent);font-size:12px}
.size{white-space:nowrap;color:var(--muted)}
code{font:13px/1.7 Consolas,monospace;overflow-wrap:anywhere}
.footer{font-size:13px;margin-top:20px}
@media(prefers-color-scheme:dark){:root{--bg:#111827;--paper:#1b2638;--ink:#eef3fc;--muted:#b3c1d6;--line:#34435b;--accent:#a7c7ff;--tint:#263b5c}}
@media(max-width:600px){main{margin:28px auto;padding:0 16px}h1{font-size:32px}}
</style>
</head>
<body><main>
<div class="eyebrow">POWERSCRIPTS / ANY FILE TYPE</div>
<h1>Any file. One useful summary.</h1>
<div class="subtitle">A clear inventory of your selection. No format-specific app needed.</div>
<div class="cards">
<div class="card"><div class="number">$($Files.Count)</div><div class="label">Selected files</div></div>
<div class="card"><div class="number">$($types.Count)</div><div class="label">File types</div></div>
<div class="card"><div class="number">$totalSize</div><div class="label">Total size</div></div>
</div>
<div class="report"><table>
<thead><tr><th>File</th><th>Size</th><th>SHA-256 fingerprint</th></tr></thead>
<tbody>$rows</tbody>
</table></div>
<p class="footer">The fingerprint identifies the file's contents, not its name. Identical contents have the same fingerprint.<br>Processed locally. No network, extra packages or changes to your originals.</p>
</main></body></html>
"@

$workspace = $env:POWERSCRIPTS_MXC_WORKSPACE
if ([string]::IsNullOrWhiteSpace($workspace)) {
    $workspace = [IO.Path]::GetTempPath()
}
$outputDirectory = Join-Path $workspace ('PowerScripts-FileSummary-' + [Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($outputDirectory)
$outputPath = Join-Path $outputDirectory 'File summary.html'
[IO.File]::WriteAllText($outputPath, $html, [Text.UTF8Encoding]::new($false))
@{ files = @($outputPath) } | ConvertTo-Json -Compress
