[CmdletBinding()]
Param(
  [Parameter(Mandatory=$True,Position=1)]
  [string]$versionNumber = "0.0.1",

  [Parameter(Mandatory=$True,Position=2)]
  [AllowEmptyString()]
  [string]$DevEnvironment = "Local",

  [ValidateSet("stable", "preview", "private")]
  [string]$Channel = "stable",

  [string]$SourceCommit = $env:BUILD_SOURCEVERSION,

  [string]$BuildNumber = $env:BUILD_BUILDNUMBER,

  [AllowEmptyString()]
  [string]$BuildDate = "",

  [AllowEmptyString()]
  [string]$DailyVersionSequence = ""
)

Write-Host $PSScriptRoot

function Get-NormalizedVersion {
  param(
    [Parameter(Mandatory = $true)]
    [string]$InputVersion,
    [Parameter(Mandatory = $true)]
    [string]$ReleaseChannel,
    [string]$PipelineBuildNumber
  )

  if ($InputVersion -match "^(?<numeric>\d+\.\d+(?:\.\d+){0,2})-(?<suffix>preview)$") {
    if ($ReleaseChannel -ne "preview") {
      throw "Version suffix '-preview' can only be used with the preview release channel"
    }

    $InputVersion = $matches["numeric"]
  }

  if ($ReleaseChannel -eq "preview") {
    $sequence = $DailyVersionSequence
    if ([string]::IsNullOrWhiteSpace($sequence)) {
      $sequence = if ($PipelineBuildNumber -match "_\d{4}\.\d{2}(?<revision>\d{3})") {
        [int]$matches["revision"]
      } else {
        1
      }
    }

    if ([int]$sequence -lt 1 -or [int]$sequence -gt 9) {
      throw "Preview daily release sequence '$sequence' must be between 1 and 9"
    }

    $metadata = & (Join-Path $PSScriptRoot "..\..\.pipelines\resolveBuildMetadata.ps1") `
      -VersionOverride $InputVersion `
      -SourceBranch "refs/heads/main" `
      -BuildReason "Manual" `
      -BuildNumber $PipelineBuildNumber `
      -BuildDate $BuildDate `
      -DailyVersionSequence ([string]$sequence)
    return $metadata.Version
  }

  if ($InputVersion -match "^(\d+)\.(\d+)\.(\d+)(?:\.(\d+))?$") {
    $versionParts = @([int]::Parse($matches[1]), [int]::Parse($matches[2]), [int]::Parse($matches[3]))
    if ($matches[4]) {
      $versionParts += [int]::Parse($matches[4])
    }

    return $versionParts -join "."
  }

  throw "Build format does not match the expected pattern (w.x, w.x.y, w.x.y.z, or w.x.y.z-preview for preview channel)"
}

$versionNumber = Get-NormalizedVersion -InputVersion $versionNumber -ReleaseChannel $Channel -PipelineBuildNumber $BuildNumber
foreach ($part in ($versionNumber -split '\.')) {
  $value = [int]::Parse($part)
  if ($value -lt 0 -or $value -gt [UInt16]::MaxValue) {
    throw "Version component '$value' is outside the supported Windows version range 0-65535"
  }
}
Write-Host "Version Number" $versionNumber

$verPropWriteFileLocation = $PSScriptRoot + '/../../src/Version.props';
$verPropReadFileLocation = $verPropWriteFileLocation;

[XML]$verProps = Get-Content $verPropReadFileLocation
$verProps.Project.PropertyGroup.Version = $versionNumber;
$verProps.Project.PropertyGroup.VersionChannel = $Channel;
$verProps.Project.PropertyGroup.SourceCommit = if ([string]::IsNullOrWhiteSpace($SourceCommit)) { "" } else { $SourceCommit };
$verProps.Project.PropertyGroup.DevEnvironment = $DevEnvironment;

Write-Host "xml" $verProps.Project.PropertyGroup.Version 
$verProps.Save($verPropWriteFileLocation);
