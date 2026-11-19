param([string]$Text)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)

if ([string]::IsNullOrWhiteSpace($Text)) {
    throw 'Copy some plain prose from a PDF first. This action is not intended for code or lists.'
}

$clean = $Text.Replace("`r`n", "`n").Replace("`r", "`n")
$clean = $clean -replace '\u00A0', ' '
$clean = $clean -replace '(?<=\p{L})[-\u00AD\u2010][ \t]*\n[ \t]*(?=\p{L})', ''
$clean = $clean -replace '\u00AD', ''
if ([string]::IsNullOrWhiteSpace($clean)) {
    throw 'The copied text contains no readable paragraphs.'
}

$paragraphs = foreach ($paragraph in [regex]::Split($clean.Trim(), '\n[ \t]*\n+')) {
    ($paragraph -replace '[ \t]*\n[ \t]*', ' ' -replace '[ \t]{2,}', ' ').Trim()
}

[Console]::Write(($paragraphs -join ([Environment]::NewLine + [Environment]::NewLine)))
