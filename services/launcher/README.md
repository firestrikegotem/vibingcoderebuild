# Bloxyn.exe

The Windows bootstrapper that connects the website to the player client. It is the exe the
Download page hands out.

## What it does

1. **Registers the `roblox-player:` URL scheme** under `HKCU\Software\Classes`, with
   `shell\open\command = "<path to Bloxyn.exe>" -player "%1"`. This is what lets a browser click
   reach it. It also registers `bloxyn-player:`, and repairs the registration on every run, so
   moving the exe does not break Play.
2. **Finds the player client.** No args means install/first run: it reports whether a client was
   found. With a launch URI it starts the client using that URI's launch data.
3. **Shows a way to recover** when no client is installed: a message with the site's `/download`
   URL, which it also opens.

## Where it looks for the client

1. the `BLOXYN_CLIENT` environment variable,
2. `client.txt` next to `Bloxyn.exe` (one line, full path to the client exe),
3. any `.exe` inside a `client\` folder next to `Bloxyn.exe`,
4. the newest `RobloxPlayerBeta.exe` in `%LOCALAPPDATA%\Bloxyn\Versions\*`,
5. the newest `RobloxPlayerBeta.exe` in `%LOCALAPPDATA%\Roblox\Versions\*`.

The client is not in this repository. It is Roblox's own binary, and `.gitignore` excludes `*.exe`.

## Arguments it passes to the client

Both shapes, so a stock client and a client built for this site both work:

```
-player "roblox-player:1+launchmode:play+gameinfo:<ticket>+placelauncherurl:<url>..." -app
-gameinfo "<ticket>" -placelauncherurl "<url>"
--authenticationTicket "<ticket>" --authenticationUrl "<site>/Login/Negotiate.ashx"
--joinScriptUrl "<url>"
```

`-unregister` removes the protocol registration. A log is kept at
`%LOCALAPPDATA%\Bloxyn\launcher.log`.

## Building

```bash
services/launcher/build.sh
```

That publishes a self-contained `win-x64` single-file exe and copies it to
`services/api/public/downloads/Bloxyn.exe`, where the Download page picks it up.

The icon needs one detour on Linux: the SDK only stamps an icon into the app host on Windows
(`NETSDK1074`), so `embed-icon.py` puts the icon into the single-file host from the NuGet pack
first and the build uses that copy via `-p:SingleFileHostSourcePath`. The host carries no overlay
data, which is why editing it is safe; editing the finished single-file exe would break its bundle.
