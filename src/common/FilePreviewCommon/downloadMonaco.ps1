$ErrorActionPreference = "Stop"

$repoRoot = (Get-Item -Path $PSScriptRoot).Parent.Parent.Parent.FullName
$tempRoot = Join-Path $env:TEMP "monaco-editor"
$pkgVersion = "0.52.2"
$pkgIntegrity = "sha512-GEQWEZmfkOGLdd3XK8ryrfWz3AIP8YymVXiPHEdewrUq7mh0qrKrfHLNCXcbB6sTnMLnOZ3ztSiKcciFUkIJwQ=="
$tgzPath = Join-Path $tempRoot "monaco-editor-$pkgVersion.tgz"
$extractPath = Join-Path $tempRoot "extract"
$srcMin = Join-Path $extractPath "package\min"
$dstRoot = Join-Path $repoRoot "src\Monaco\monacoSRC"
$dstMin = Join-Path $dstRoot "min"
$stampPath = Join-Path $dstRoot ".monaco-version"
$expectedStamp = "$pkgVersion`n$pkgIntegrity"

if ((Test-Path $dstMin) -and (Test-Path $stampPath) -and ((Get-Content -Path $stampPath -Raw) -eq $expectedStamp)) {
	exit 0
}

if (Test-Path $tempRoot) {
	Remove-Item $tempRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $tempRoot, $extractPath | Out-Null

# Download package tarball from npm registry
Invoke-WebRequest -Uri "https://registry.npmjs.org/monaco-editor/-/monaco-editor-$pkgVersion.tgz" -OutFile $tgzPath

$sha512 = [System.Security.Cryptography.SHA512]::Create()
try {
	$actualIntegrity = "sha512-" + [Convert]::ToBase64String($sha512.ComputeHash([System.IO.File]::ReadAllBytes($tgzPath)))
}
finally {
	$sha512.Dispose()
}

if ($actualIntegrity -ne $pkgIntegrity) {
	throw "The downloaded Monaco Editor package integrity '$actualIntegrity' did not match the expected value '$pkgIntegrity'."
}

# Extract tgz (tar in modern Windows)
tar -xzf $tgzPath -C $extractPath
if ($LASTEXITCODE -ne 0) {
	throw "Failed to extract Monaco Editor package. tar exited with code $LASTEXITCODE."
}

if (-not (Test-Path $srcMin)) {
	throw "The Monaco Editor package did not contain the expected package\min directory."
}

$stageRoot = Join-Path $tempRoot "monacoSRC"
$stageMin = Join-Path $stageRoot "min"
New-Item -ItemType Directory -Path $stageMin -Force | Out-Null
Copy-Item -Path (Join-Path $srcMin "*") -Destination $stageMin -Recurse -Force
$expectedLoader = Join-Path $stageMin "vs\loader.js"
if (-not (Test-Path $expectedLoader)) {
	throw "The Monaco Editor package did not produce the expected min\vs\loader.js file."
}

Set-Content -Path (Join-Path $stageRoot ".monaco-version") -Value $expectedStamp -NoNewline
if (Test-Path $dstRoot) {
	Remove-Item -Path $dstRoot -Recurse -Force
}
Move-Item -Path $stageRoot -Destination $dstRoot
Remove-Item -Path $tempRoot -Recurse -Force