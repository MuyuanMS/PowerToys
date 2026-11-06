function Test-ReleaseNotesContainsH1 {
    param([AllowEmptyString()][string]$Text)

    $atxH1 = '^ {0,3}#(?:[ \t]+|$)'
    $setextH1 = '^ {0,3}\S[^\r\n]*\r?\n {0,3}=+[ \t]*\r?$'
    return $Text -match "(?m)(?:$atxH1|$setextH1)"
}

function Remove-LeadingReleaseNotesH1 {
    param(
        [Parameter(Mandatory)][string]$Text,
        [Parameter(Mandatory)][string]$Marker
    )

    $markerPrefix = '\A' + [regex]::Escape($Marker) + '\r?\n(?:[ \t]*\r?\n)*'
    $atxH1 = ' {0,3}#(?:[ \t]+[^\r\n]*|(?=\r?\n|$))(?:\r?\n|$)(?:[ \t]*\r?\n)*'
    $setextH1 = ' {0,3}\S[^\r\n]*\r?\n {0,3}=+[ \t]*(?:\r?\n|$)(?:[ \t]*\r?\n)*'
    $pattern = $markerPrefix + '(?:' + $atxH1 + '|' + $setextH1 + ')'
    $markerNewLine = [regex]::Match(
        $Text,
        '\A' + [regex]::Escape($Marker) + '(?<newline>\r?\n)'
    ).Groups['newline'].Value
    return [regex]::new($pattern).Replace($Text, $Marker + $markerNewLine, 1)
}
