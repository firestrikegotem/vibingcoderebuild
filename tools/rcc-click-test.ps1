# Minimal ThumbnailGenerator test: opens a job on a running RCC and calls Click on an
# empty place. Nothing from the avatar pipeline is involved, so a failure here means the
# renderer itself cannot draw on this machine, not that our avatar job script is wrong.
#
# The console window running RCC is the important part: a failing job prints its Lua
# traceback there. This script just drives the SOAP call with curl.exe, which returns the
# reply body faithfully (Invoke-WebRequest hands back an empty stream for RCC's 500s).
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
-- CFrame.new takes 3 or 12 numbers, never 6; passing 6 made RCC answer
-- "Invalid number of arguments: 6" before the click was ever reached.
local block = Instance.new('Part', workspace)
block.Anchored = true
block.Position = Vector3.new(0, 5, 0)
local cam = Instance.new('Camera', workspace)
cam.CameraType = Enum.CameraType.Scriptable
cam.CoordinateFrame = CFrame.new(Vector3.new(0, 10, 20), Vector3.new(0, 5, 0))
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

$tmp = Join-Path ([System.IO.Path]::GetTempPath()) "rcc-click-$jobId.xml"
[System.IO.File]::WriteAllText($tmp, $xml, (New-Object System.Text.UTF8Encoding($false)))

$url = "http://127.0.0.1:$Port/"
Write-Host "[test] posting OpenJobEx to $url (job $jobId, graphics mode $GraphicsMode)"
Write-Host "[test] reply body follows. If it is empty, read the RCC console window for the Lua traceback."

& curl.exe -s -S -o - -w "`n[test] http=%{http_code}`n" -H "Content-Type: text/xml; charset=utf-8" --data-binary "@$tmp" $url
Write-Host "[test] done"
Remove-Item $tmp -ErrorAction SilentlyContinue
