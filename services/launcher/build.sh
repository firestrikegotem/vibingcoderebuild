#!/usr/bin/env bash
#
# Builds Bloxyn.exe (the Windows bootstrapper) and copies it to the site's download folder.
#
# The only unusual step is the icon. The .NET SDK stamps an icon into the host only when the
# build runs on Windows, so on Linux the icon goes into the single-file host first and the
# build is pointed at that copy with -p:SingleFileHostSourcePath.
#
# Requirements: dotnet SDK 6+, python3, `pip install lief`.
#
set -euo pipefail
cd "$(dirname "$0")"

HOST_PACK="$(ls -d "$HOME"/.nuget/packages/microsoft.netcore.app.host.win-x64/*/runtimes/win-x64/native 2>/dev/null | tail -1 || true)"
if [ -z "$HOST_PACK" ]; then
	echo "error: the win-x64 host pack is not in the NuGet cache yet." >&2
	echo "Run 'dotnet publish BloxynLauncher.csproj -r win-x64' once to fetch it, then rerun." >&2
	exit 1
fi

PATCHED_HOST="$(mktemp -d)/singlefilehost-bloxyn.exe"
python3 embed-icon.py "$HOST_PACK/singlefilehost.exe" bloxyn.ico "$PATCHED_HOST"

rm -f obj/Release/net6.0/win-x64/Bloxyn.exe
dotnet publish BloxynLauncher.csproj -c Release -o out \
	-p:SingleFileHostSourcePath="$PATCHED_HOST" \
	-p:EnableWindowsTargeting=true

cp out/Bloxyn.exe ../api/public/downloads/Bloxyn.exe
echo
echo "built $(du -h out/Bloxyn.exe | cut -f1) Bloxyn.exe"
echo "copied it to services/api/public/downloads/ - it is on the Download page immediately"
