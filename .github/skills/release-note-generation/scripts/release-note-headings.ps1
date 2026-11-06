function Test-ReleaseNotesContainsH1 {
    param([AllowEmptyString()][string]$Text)

    return $Text -match '(?m)^ {0,3}#(?:[ \t]+|$)|^ {0,3}\S[^\r\n]*\r?\n {0,3}=+[ \t]*\r?$'
}

function Remove-LeadingReleaseNotesH1 {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Marker
    )

    $pattern = '\A' + [regex]::Escape($Marker) + '\r?\n(?:[ \t]*\r?\n)*' +
        '(?: {0,3}#(?:[ \t]+[^\r\n]*|(?=\r?\n|$))(?:\r?\n|$)(?:[ \t]*\r?\n)*|' +
        ' {0,3}\S[^\r\n]*\r?\n {0,3}=+[ \t]*(?:\r?\n|$)(?:[ \t]*\r?\n)*)'
    return [regex]::new($pattern).Replace($Text, "$Marker`n", 1)
}
