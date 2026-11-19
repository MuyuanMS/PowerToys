param([string]$Text)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

if ([string]::IsNullOrWhiteSpace($Text)) {
    throw 'Copy a CSV table with a header and at least one data row.'
}

$rows = @($Text | ConvertFrom-Csv)
if ($rows.Count -eq 0 -or $rows.Count -gt 2000) {
    throw 'Use a CSV table containing between 1 and 2000 data rows.'
}

$headers = @($rows[0].PSObject.Properties.Name)
if ($headers.Count -lt 2) {
    throw 'Copy a comma-separated table with at least two columns.'
}

function Format-Cell([string]$Value) {
    $encoded = [Net.WebUtility]::HtmlEncode($Value)
    return ($encoded.Replace('\', '\\').Replace('|', '\|') -replace '\r?\n', '<br>')
}

$lines = [Collections.Generic.List[string]]::new()
$lines.Add('| ' + (($headers | ForEach-Object { Format-Cell $_ }) -join ' | ') + ' |')
$lines.Add('| ' + (($headers | ForEach-Object { '---' }) -join ' | ') + ' |')
foreach ($row in $rows) {
    $cells = foreach ($header in $headers) {
        Format-Cell ([string]$row.$header)
    }
    $lines.Add('| ' + ($cells -join ' | ') + ' |')
}

[Console]::Write(($lines -join [Environment]::NewLine))
