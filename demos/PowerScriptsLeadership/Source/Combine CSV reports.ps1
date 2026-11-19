param([string[]]$Files)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

if ($Files.Count -lt 1 -or $Files.Count -gt 20) {
    throw 'Select between 1 and 20 CSV reports with matching columns.'
}

$rows = [Collections.Generic.List[object]]::new()
$headers = $null
foreach ($file in $Files) {
    if ([IO.Path]::GetExtension($file) -ine '.csv') {
        throw 'Only CSV files are supported.'
    }

    $records = @(Import-Csv -LiteralPath $file -Encoding utf8)
    if ($records.Count -eq 0) {
        throw "The CSV has no data rows: $([IO.Path]::GetFileName($file))"
    }

    $columns = @($records[0].PSObject.Properties.Name)
    if ($null -eq $headers) {
        $headers = $columns
    }
    elseif (($headers -join [char]31) -cne ($columns -join [char]31)) {
        throw 'All selected CSV reports must have the same columns in the same order.'
    }

    foreach ($record in $records) {
        $rows.Add([pscustomobject]@{
            Source = [IO.Path]::GetFileName($file)
            Record = $record
        })
    }
    if ($rows.Count -gt 2000) {
        throw 'Select reports containing no more than 2000 data rows in total.'
    }
}

function Encode([string]$Value) {
    return [Net.WebUtility]::HtmlEncode($Value)
}

$table = [Text.StringBuilder]::new()
[void]$table.Append('<thead><tr><th>Source file</th>')
foreach ($header in $headers) {
    [void]$table.Append('<th>').Append((Encode $header)).Append('</th>')
}
[void]$table.Append('</tr></thead><tbody>')
foreach ($row in $rows) {
    [void]$table.Append('<tr><td class="source">').Append((Encode $row.Source)).Append('</td>')
    foreach ($header in $headers) {
        [void]$table.Append('<td>').Append((Encode ([string]$row.Record.$header))).Append('</td>')
    }
    [void]$table.Append('</tr>')
}
[void]$table.Append('</tbody>')

$created = [DateTime]::Now.ToString('dd MMM yyyy, HH:mm', [Globalization.CultureInfo]::InvariantCulture)
$html = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Combined CSV report</title>
<style>
body{margin:0;background:#f3f5fa;color:#17233c;font:16px "Segoe UI",sans-serif}
main{max-width:1080px;margin:56px auto;padding:0 28px}
.eyebrow{font-size:13px;font-weight:700;letter-spacing:2px;color:#5162d9}
h1{font-size:42px;letter-spacing:-1.5px;margin:12px 0}
.subtitle,.footer{color:#637087;line-height:1.6}
.cards{display:flex;gap:18px;margin:30px 0}
.card{flex:1;background:white;border-radius:16px;padding:24px;border:1px solid #e4e8f1}
.number{font-size:36px;font-weight:650;color:#5162d9}
.label{color:#637087;margin-top:4px}
.report{background:white;border:1px solid #e4e8f1;border-radius:16px;overflow:auto}
table{width:100%;border-collapse:collapse;text-align:left;white-space:nowrap}
th{background:#edf0ff;color:#3443a3;font-size:13px;padding:18px 22px}
td{padding:17px 22px;border-top:1px solid #edf0f5}
tbody tr:nth-child(even){background:#fafbfe}
.source{color:#637087;font-size:14px}
.footer{font-size:13px;margin-top:20px}
</style>
</head>
<body><main>
<div class="eyebrow">POWERSCRIPTS / FILE EXPLORER</div>
<h1>One selection. One clear report.</h1>
<div class="subtitle">Combined locally from the selected CSV files. Originals unchanged.</div>
<div class="cards">
<div class="card"><div class="number">$($Files.Count)</div><div class="label">Source files</div></div>
<div class="card"><div class="number">$($rows.Count)</div><div class="label">Combined records</div></div>
<div class="card"><div class="number">$($headers.Count)</div><div class="label">Data columns</div></div>
</div>
<div class="report"><table>$table</table></div>
<div class="footer">Created $created. No network or additional packages.</div>
</main></body></html>
"@

$workspace = $env:POWERSCRIPTS_MXC_WORKSPACE
if ([string]::IsNullOrWhiteSpace($workspace)) {
    $workspace = Join-Path ([IO.Path]::GetTempPath()) ('PowerScripts-Report-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($workspace)
}
$outputPath = Join-Path $workspace 'Combined CSV report.html'
[IO.File]::WriteAllText($outputPath, $html, [Text.UTF8Encoding]::new($false))
@{ files = @($outputPath) } | ConvertTo-Json -Compress
