$repoRoot = (Get-Item -Path $PSScriptRoot).Parent.Parent.Parent.FullName
$tempRoot = Join-Path $env:TEMP "monaco-editor"
$pkgVersion = "0.52.2"
$tgzPath = Join-Path $tempRoot "monaco-editor-$pkgVersion.tgz"
$extractPath = Join-Path $tempRoot "extract"
$srcMin = Join-Path $extractPath "package\min"
$dstRoot = Join-Path $repoRoot "src\Monaco\monacoSRC"
$dstMin = Join-Path $dstRoot "min"

if (Test-Path $dstMin) {
	exit 0
}

if (Test-Path $tempRoot) {
	Remove-Item $tempRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $tempRoot, $extractPath | Out-Null

# Download package tarball from npm registry
Invoke-WebRequest -Uri "https://registry.npmjs.org/monaco-editor/-/monaco-editor-$pkgVersion.tgz" -OutFile $tgzPath

# Extract tgz (tar in modern Windows)
tar -xzf $tgzPath -C $extractPath

New-Item -ItemType Directory -Path $dstMin -Force | Out-Null
Copy-Item -Path (Join-Path $srcMin "*") -Destination $dstMin -Recurse -Force
Remove-Item -Path $tempRoot -Recurse -Force