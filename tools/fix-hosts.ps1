<#
    RCC talks to the website over WinHTTP, and it builds those URLs from the bare host
    name (http://bbblox.org:5000/Game/LuaWebService/..., http://bbblox.org:5000/asset/?id=...).
    A hosts entry for www.<domain> alone therefore is not enough: the bare lookup fails
    with "WinHttpSendRequest failed, err=0x2EE7" (name not resolved), the CoreScript group
    request and the clothing asset both fail, and RCC exits 1 with StartProcessException
    partway through a render.

    This script replaces any existing mapping for the bare and www names with one clean
    pair and flushes the resolver cache. It has to run in an Administrator shell.
#>
param([string]$Domain = ('bbblox' + '.org'))

$hostsPath = Join-Path $env:SystemRoot 'System32\drivers\etc\hosts'

$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host ''
    Write-Host 'This needs an Administrator shell. Open Command Prompt as administrator, then run:' -ForegroundColor Yellow
    Write-Host '  powershell -NoProfile -ExecutionPolicy Bypass -File tools\fix-hosts.ps1' -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path $hostsPath)) {
    Write-Host "Cannot find $hostsPath" -ForegroundColor Red
    exit 1
}

$want = @($Domain, "www.$Domain")
$lines = Get-Content -Path $hostsPath -ErrorAction Stop

# Keep every unrelated line, drop any mapping that already names one of our hosts,
# so the file does not accumulate duplicates the way findstr showed.
$kept = @()
foreach ($line in $lines) {
    $trimmed = $line.Trim()
    if ($trimmed -eq '' -or $trimmed.StartsWith('#')) { $kept += $line; continue }
    $parts = $trimmed -split '\s+'
    if ($parts.Count -lt 2) { $kept += $line; continue }
    $names = @($parts[1..($parts.Count - 1)])
    $ours = $false
    foreach ($name in $names) {
        if ($want -contains $name) { $ours = $true; break }
    }
    if (-not $ours) { $kept += $line }
}

$newContent = @($kept) + @('', "127.0.0.1`t$Domain", "127.0.0.1`twww.$Domain")
Set-Content -Path $hostsPath -Value $newContent -Encoding ASCII -ErrorAction Stop

# WinHTTP caches resolutions separately from the DNS client, so flush it or RCC keeps
# seeing the old failure until reboot.
& ipconfig /flushdns | Out-Null

Write-Host ''
Write-Host "hosts entries for $Domain now read:" -ForegroundColor Green
Get-Content -Path $hostsPath | Select-String -Pattern ([regex]::Escape($Domain))
Write-Host ''
Write-Host 'Restart RCC (or the game server) so the child process picks this up.' -ForegroundColor Green
