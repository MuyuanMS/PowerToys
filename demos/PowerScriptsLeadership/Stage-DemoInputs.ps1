param(
    [string]$DestinationRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PowerScripts-Demo')
)

$ErrorActionPreference = 'Stop'
$sourceRoot = Join-Path $PSScriptRoot 'Inputs'
$DestinationRoot = [IO.Path]::GetFullPath($DestinationRoot)
$files = @(
    @{ Source = 'Clipboard - PDF paragraphs.txt'; Target = 'Clipboard-PDF.txt' }
    @{ Source = 'Clipboard - CSV table.txt'; Target = 'Clipboard-CSV.txt' }
    @{ Source = 'Clipboard - remove hyphens.txt'; Target = 'Clipboard-Hyphens.txt' }
    @{ Source = 'Screenshots\Meeting notes.png'; Target = 'Images\Meeting notes.png' }
    @{ Source = 'Screenshots\sample-arrow.png'; Target = 'Images\sample-arrow.png' }
    @{ Source = 'Reports\North.csv'; Target = 'Reports\North.csv' }
    @{ Source = 'Reports\South.csv'; Target = 'Reports\South.csv' }
    @{ Source = 'General files\Project brief.txt'; Target = 'General-files\Project brief.txt' }
    @{ Source = 'General files\North.csv'; Target = 'General-files\North.csv' }
    @{ Source = 'General files\Meeting notes.png'; Target = 'General-files\Meeting notes.png' }
    @{ Source = 'Access checks\Sandbox canary.txt'; Target = 'Access-checks\Sandbox canary.txt' }
)

foreach ($file in $files) {
    $source = Join-Path $sourceRoot $file.Source
    $target = Join-Path $DestinationRoot $file.Target
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Required synthetic input is missing: $($file.Source)"
    }

    $directory = [IO.Path]::GetDirectoryName($target)
    while ($directory) {
        if (Test-Path -LiteralPath $directory) {
            $item = Get-Item -LiteralPath $directory -Force
            if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Destination must be a regular directory: $directory"
            }
        }

        $directory = [IO.Path]::GetDirectoryName($directory)
    }

    if (Test-Path -LiteralPath $target) {
        $item = Get-Item -LiteralPath $target -Force
        if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or
            (Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
            throw "Existing input differs; nothing will overwrite it: $target"
        }
    }
}

foreach ($file in $files) {
    $source = Join-Path $sourceRoot $file.Source
    $target = Join-Path $DestinationRoot $file.Target
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    if (-not (Test-Path -LiteralPath $target)) {
        $inputStream = [IO.File]::OpenRead($source)
        try {
            $outputStream = [IO.File]::Open($target, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write)
            try {
                $inputStream.CopyTo($outputStream)
            }
            finally {
                $outputStream.Dispose()
            }
        }
        finally {
            $inputStream.Dispose()
        }
    }

    if ((Get-FileHash -LiteralPath $source).Hash -ne (Get-FileHash -LiteralPath $target).Hash) {
        throw "Input copy verification failed: $target"
    }
}

Write-Output $DestinationRoot
