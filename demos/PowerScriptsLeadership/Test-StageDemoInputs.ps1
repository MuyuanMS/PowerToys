param(
    [Parameter(Mandatory)][string]$ResultsPath
)

$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('PowerScripts-stage-test-' + [guid]::NewGuid().ToString('N'))
$stage = Join-Path $PSScriptRoot 'Stage-DemoInputs.ps1'
$results = @()
[void][IO.Directory]::CreateDirectory($testRoot)
try {
    $destination = Join-Path $testRoot 'PowerScripts-Demo'
    [void](& $stage -DestinationRoot $destination)
    $files = @(Get-ChildItem -LiteralPath $destination -Recurse -File)
    if ($files.Count -ne 11 -or
        @(Get-ChildItem -LiteralPath $destination -Recurse -Directory | Where-Object { $_.Name.Contains(' ') }).Count -or
        @($files | Where-Object Extension -In @('.json', '.ps1', '.md', '.html')).Count) {
        throw 'The staged folder contains unexpected files or folder names.'
    }
    $results += @{ name = 'Only 11 inputs in no-space demo folders'; status = 'PASS' }

    $before = @{}
    foreach ($file in $files) {
        $before[$file.FullName] = @{
            hash = (Get-FileHash -LiteralPath $file.FullName).Hash
            timestamp = $file.LastWriteTimeUtc.Ticks
        }
    }
    [void](& $stage -DestinationRoot $destination)
    foreach ($path in $before.Keys) {
        if ((Get-FileHash -LiteralPath $path).Hash -ne $before[$path].hash -or
            (Get-Item -LiteralPath $path).LastWriteTimeUtc.Ticks -ne $before[$path].timestamp) {
            throw 'An idempotent rerun rewrote an existing input.'
        }
    }
    $results += @{ name = 'Idempotent without rewriting inputs'; status = 'PASS' }

    $collision = Join-Path $testRoot 'Existing-Demo'
    [void][IO.Directory]::CreateDirectory($collision)
    $original = Join-Path $collision 'Clipboard-PDF.txt'
    [IO.File]::WriteAllText($original, 'Existing file: preserve me.')
    $originalHash = (Get-FileHash -LiteralPath $original).Hash
    $refused = $false
    try { [void](& $stage -DestinationRoot $collision) }
    catch {
        if ($_.Exception.Message -notlike 'Existing input differs*') { throw }
        $refused = $true
    }
    if (-not $refused -or (Get-FileHash -LiteralPath $original).Hash -ne $originalHash -or
        @(Get-ChildItem -LiteralPath $collision -Recurse -File).Count -ne 1) {
        throw 'A conflicting destination was overwritten or partially populated.'
    }
    $results += @{ name = 'Conflicts refused before copying, originals preserved'; status = 'PASS' }
}
finally {
    [IO.Directory]::Delete($testRoot, $true)
    $results | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $ResultsPath -Encoding utf8
}

Write-Output 'PASS: clean input staging, idempotence and existing-file preservation.'
