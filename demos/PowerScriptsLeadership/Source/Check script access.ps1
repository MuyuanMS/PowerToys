param([string[]]$Files)

$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.UTF8Encoding]::new($false)
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'PowerShell 7 or later is required.' }
$timer = [Diagnostics.Stopwatch]::StartNew()

if (@($Files).Count -ne 1 -or [IO.Path]::GetFileName($Files[0]) -ne 'Sandbox canary.txt') {
    throw 'Select only the supplied Sandbox canary.txt, not a personal file.'
}
$canary = [IO.Path]::GetFullPath($Files[0])
$expected = "PowerScripts sandbox access canary.`nSynthetic input only. This file must stay unchanged."
if ((Get-Item -LiteralPath $canary).Length -gt 1024 -or
    [IO.File]::ReadAllText($canary).Replace("`r`n", "`n").TrimEnd("`n") -cne $expected) {
    throw 'This is not the supplied synthetic canary. No probes were performed.'
}

function Read-Policy {
    try {
        $path = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\PowerScripts\config.json'
        $settings = ([IO.File]::ReadAllText($path) | ConvertFrom-Json).mxc
        if ($settings.enabled -isnot [bool]) { throw 'No explicit MXC preference.' }
        $known = @('filesystem', 'network', 'ui', 'leastPrivilege')
        $disabled = @($known | Where-Object { $_ -in @($settings.disabledPolicies) })
        $accepted = $settings.riskAccepted -is [bool] -and $settings.riskAccepted
        $fallback = (-not $settings.enabled -or $disabled.Count -gt 0) -and -not $accepted
        return @{
            known = $true
            enabled = $fallback -or $settings.enabled
            restricted = @($known | Where-Object { $fallback -or $_ -notin $disabled })
            note = if ($fallback) { 'Unaccepted weaker preferences resolve to all restrictions ON.' } else { 'Resolved from current local settings; not a security attestation.' }
        }
    }
    catch {
        return @{ known = $false; enabled = $null; restricted = @(); note = 'Settings unavailable or platform-default MXC: configured state is unknown.' }
    }
}

function Test-Write([string]$Directory) {
    $stream = $null
    try {
        $path = Join-Path $Directory ('.powerscripts-access-' + [guid]::NewGuid().ToString('N') + '.tmp')
        $stream = [IO.FileStream]::new($path, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write,
            [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
        $stream.WriteByte(42)
        return @{ state = 'ALLOWED'; detail = 'Created a one-byte probe; deleted automatically on close.' }
    }
    catch {
        $cause = $_.Exception
        while ($cause.InnerException) { $cause = $cause.InnerException }
        $denied = $cause -is [UnauthorizedAccessException] -or ($cause.HResult -band 0xffff) -eq 5
        return @{
            state = if ($denied) { 'DENIED' } else { 'UNAVAILABLE' }
            detail = if ($denied) { 'Write access was refused by the OS or policy; no file was created.' } else { 'The write probe failed; that alone does not establish a policy block.' }
        }
    }
    finally {
        if ($stream) { $stream.Dispose() }
    }
}

function Test-Network {
    $handler = [Net.Http.HttpClientHandler]::new()
    $handler.AllowAutoRedirect = $false
    $handler.UseCookies = $false
    $handler.UseDefaultCredentials = $false
    $client = [Net.Http.HttpClient]::new($handler)
    $client.Timeout = [TimeSpan]::FromSeconds(3)
    $request = [Net.Http.HttpRequestMessage]::new([Net.Http.HttpMethod]::Head, 'https://www.microsoft.com/')
    $response = $null
    try {
        $response = $client.SendAsync($request, [Net.Http.HttpCompletionOption]::ResponseHeadersRead).GetAwaiter().GetResult()
        return @{ state = 'REACHABLE'; detail = 'Anonymous HTTPS HEAD received HTTP ' + [int]$response.StatusCode + '; no response body was read.' }
    }
    catch {
        $cause = $_.Exception
        $denied = $false
        while ($cause) {
            if ($cause -is [Net.Sockets.SocketException] -and $cause.SocketErrorCode -eq [Net.Sockets.SocketError]::AccessDenied) {
                $denied = $true
            }
            $cause = $cause.InnerException
        }
        return @{
            state = if ($denied) { 'DENIED' } else { 'UNAVAILABLE' }
            detail = if ($denied) { 'The OS refused the outbound socket.' } else { 'No HTTPS response within the probe limit: policy, DNS, TLS, proxy or connectivity may be responsible.' }
        }
    }
    finally {
        if ($response) { $response.Dispose() }
        $request.Dispose()
        $client.Dispose()
    }
}

function Encode([object]$Value) { return [Net.WebUtility]::HtmlEncode([string]$Value) }

$policy = Read-Policy
$workspace = [IO.Path]::GetTempPath()
$workspaceWrite = Test-Write $workspace
$outsideWrite = Test-Write ([IO.Path]::GetDirectoryName($canary))
$network = Test-Network
$admin = 'UNKNOWN'
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    try {
        $principal = [Security.Principal.WindowsPrincipal]::new($identity)
        $admin = if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { 'ADMIN' } else { 'NOT ADMIN' }
    }
    finally { $identity.Dispose() }
}
catch { }

$cards = [Text.StringBuilder]::new()
foreach ($item in @(
    @{ name = 'MXC'; key = '' },
    @{ name = 'Filesystem'; key = 'filesystem' },
    @{ name = 'Network'; key = 'network' },
    @{ name = 'Desktop / clipboard'; key = 'ui' },
    @{ name = 'Least privilege'; key = 'leastPrivilege' }
)) {
    $active = $policy.enabled -and (-not $item.key -or $item.key -in $policy.restricted)
    $state = if (-not $policy.known) { 'UNKNOWN' } elseif ($active) { 'ON' } else { 'OFF' }
    $tone = if ($state -eq 'ON') { 'good' } else { 'warn' }
    [void]$cards.Append("<div class='card'><span>$(Encode $item.name)</span><strong class='$tone'>$state</strong></div>")
}

$rows = @(
    @{ name = 'Selected synthetic file: read'; state = 'READABLE'; detail = 'Canary contents verified. The input is never opened for writing.' },
    @{ name = 'Temporary workspace: write'; state = $workspaceWrite.state; detail = $workspaceWrite.detail },
    @{ name = 'Beside the selected file: write'; state = $outsideWrite.state; detail = $outsideWrite.detail },
    @{ name = 'Network: www.microsoft.com:443'; state = $network.state; detail = $network.detail },
    @{ name = 'Current process: administrator'; state = $admin; detail = 'A token-role check, not proof of every least-privilege control. Standard-user launching also applies with MXC off.' },
    @{ name = 'Desktop / clipboard: live access'; state = 'NOT PROBED'; detail = 'No clipboard reads, desktop capture, UI injection or user-window interaction.' }
)
$table = [Text.StringBuilder]::new()
foreach ($row in $rows) {
    [void]$table.Append("<tr><td>$(Encode $row.name)</td><td><b>$(Encode $row.state)</b></td><td>$(Encode $row.detail)</td></tr>")
}
$timer.Stop()
$marker = if ($env:POWERSCRIPTS_MXC_WORKSPACE) { 'present' } else { 'absent' }
$observedCards = [Text.StringBuilder]::new()
foreach ($item in @(
    @{ name = 'Workspace writes'; state = $workspaceWrite.state },
    @{ name = 'Other-folder writes'; state = $outsideWrite.state },
    @{ name = 'Internet probe'; state = if ($network.state -eq 'UNAVAILABLE') { 'NO RESPONSE' } else { $network.state } },
    @{ name = 'Process token'; state = $admin }
)) {
    $tone = if ($item.state -in @('ALLOWED', 'DENIED', 'NOT ADMIN')) { 'good' } else { 'warn' }
    [void]$observedCards.Append("<div class='card'><span>$(Encode $item.name)</span><strong class='$tone'>$(Encode $item.state)</strong></div>")
}
$html = @"
<!doctype html><html lang="en"><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>PowerScripts access check</title>
<style>
:root{color-scheme:light dark}body{font:16px 'Segoe UI',sans-serif;max-width:1120px;margin:48px auto;padding:0 28px;background:#f4f7fb;color:#14243a}
h1{font-size:38px;margin-bottom:8px}h2{font-size:21px;margin-top:30px}.sub,.note{color:#51627a;line-height:1.55}.cards{display:flex;flex-wrap:wrap;gap:12px;margin:24px 0}
.card{background:white;border:1px solid #dce5ef;border-radius:12px;padding:18px;flex:1;min-width:145px}.card span{display:block;color:#51627a;font-size:14px}.card strong{display:block;font-size:25px;margin-top:8px}
.good{color:#137449}.warn{color:#a25400}table{width:100%;border-collapse:collapse;background:white;border-radius:12px}th,td{text-align:left;padding:16px;border-bottom:1px solid #dce5ef;vertical-align:top}th{font-size:13px;text-transform:uppercase;letter-spacing:.05em}
td:first-child{width:29%}td:nth-child(2){white-space:nowrap}footer{margin-top:24px;font-size:13px;color:#51627a;line-height:1.6}
@media(prefers-color-scheme:dark){body{background:#111b2b;color:#e5edf8}.card,table{background:#19263a;border-color:#344760}th,td{border-color:#344760}.sub,.note,.card span,footer{color:#b6c4d9}.good{color:#77dfab}.warn{color:#ffc270}}
</style>
<p class="sub">POWERSCRIPTS / LIVE DIAGNOSTICS</p><h1>What can this script access?</h1>
<p class="sub">Small, real probes. No original files changed. MXC workspace marker: $marker (not a security attestation).</p>
<div class="cards">$observedCards</div>
<h2>Observed inside this script</h2><table><thead><tr><th>Probe</th><th>Observed</th><th>What it means</th></tr></thead><tbody>$table</tbody></table>
<details><summary>Configured restrictions, if visible to this process</summary><div class="cards">$cards</div>
<p class="note">$(Encode $policy.note) Host settings may themselves be outside the sandbox's readable paths. UNKNOWN does not mean protection is off.</p></details>
<footer>Filesystem restriction does not mean all reads are blocked. Only the supplied synthetic canary and its folder are tested; personal files are not inspected.
Network failures can have causes other than MXC, and one reachable destination does not prove access to every host or port.
The anonymous HEAD sends no file contents, keys or credentials, follows no redirects and reads no body.
Settings are read during execution, not a signed per-run policy snapshot. Approved host CLI actions such as Azure OCR run outside MXC; these contained probes do not describe their access.
<br>$(Encode ([DateTimeOffset]::Now.ToString('yyyy-MM-dd HH:mm:ss zzz'))) / Probe execution: $([math]::Round($timer.Elapsed.TotalSeconds, 2))s, excluding Host/container startup. No settings or policies were changed.</footer>
</html>
"@
$report = Join-Path $workspace ('powerscripts-access-' + [guid]::NewGuid().ToString('N') + '.html')
[IO.File]::WriteAllText($report, $html, [Text.UTF8Encoding]::new($false))
@{ files = @($report) } | ConvertTo-Json -Compress
