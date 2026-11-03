# Minimal ThumbnailGenerator test: opens a job on a running RCC and calls Click on an
# empty place. Nothing from the avatar pipeline is involved, so a failure here means the
# renderer itself cannot draw on this machine, not that our job script is wrong.
#
# Usage:  powershell -NoProfile -ExecutionPolicy Bypass -File tools\rcc-click-test.ps1
#         powershell -NoProfile -ExecutionPolicy Bypass -File tools\rcc-click-test.ps1 -Port 53640 -GraphicsMode 3

param(
    [int]$Port = 53640,
    [int]$GraphicsMode = 3
)

$jobId = [guid]::NewGuid().ToString()

$lua = @"
local tg = game:GetService('ThumbnailGenerator')
tg.GraphicsMode = $GraphicsMode
print('[test] graphics mode', tg.GraphicsMode)
local cam = Instance.new('Camera', workspace)
cam.CameraType = Enum.CameraType.Scriptable
cam.CoordinateFrame = CFrame.new(0, 10, 20, 0, 0, 0)
workspace.CurrentCamera = cam
local ok, res = pcall(function() return tg:Click('png', 420, 420, true, true) end)
print('[test] click ok', ok, 'type', type(res), 'len', tostring(res) and #tostring(res) or 0)
if not ok then print('[test] click error', tostring(res)) end
return 'click-test-done'
"@

$xml = @"
<?xml version="1.0" encoding="utf-8"?>
<soap:Envelope xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema" xmlns:soap="http://schemas.xmlsoap.org/soap/envelope/">
  <soap:Body>
    <OpenJobEx xmlns="http://roblox.com/">
        <job>
            <id>$jobId</id>
            <category>0</category>
            <cores>1</cores>
            <expirationInSeconds>60</expirationInSeconds>
        </job>
        <script>
            <name>GameStart</name>
            <script><![CDATA[
$lua
            ]]></script>
        </script>
    </OpenJobEx>
  </soap:Body>
</soap:Envelope>
"@

$url = "http://127.0.0.1:$Port/"
Write-Host "[test] posting OpenJobEx to $url (job $jobId, graphics mode $GraphicsMode)"
try {
    $r = Invoke-WebRequest -Uri $url -Method POST -ContentType 'text/xml; charset=utf-8' -Body $xml -UseBasicParsing -TimeoutSec 120
    Write-Host "[test] status $($r.StatusCode)"
    Write-Host "[test] reply:"
    Write-Host $r.Content
} catch {
    $resp = $_.Exception.Response
    if ($resp -ne $null) {
        # A 500 is a SOAP fault, which means RCC is alive and the Lua ran: the body holds the
        # real error and the RCC console prints it too.
        Write-Host "[test] status $([int]$resp.StatusCode) - reading fault body"
        try {
            $stream = $resp.GetResponseStream()
            $reader = New-Object System.IO.StreamReader($stream)
            Write-Host "[test] fault body:"
            Write-Host $reader.ReadToEnd()
        } catch {
            Write-Host "[test] could not read fault body: $($_.Exception.Message)"
        }
    } else {
        Write-Host "[test] request failed: $($_.Exception.Message)"
        Write-Host "[test] if this is a connection error, RCC died during the click (process crash), not a Lua error."
    }
}
