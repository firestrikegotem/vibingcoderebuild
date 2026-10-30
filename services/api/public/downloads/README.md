# Player builds

Everything in this folder is listed on the site's Download page straight away, with no restart,
config change or rebuild. Served by `GET /apisite/download/v1/clients` and
`GET /apisite/download/v1/client/<file>`. Allowed types: exe, zip, msi, dmg, apk, 7z, rar.

## Bloxyn.exe

The bootstrapper. It registers the `roblox-player:` URL scheme, finds the player client and starts
it with the launch data from the URI. Source, tests and build instructions:
`services/launcher/`. Rebuild and re-copy it there with `services/launcher/build.sh`.

## Adding the player client

The client is Roblox's own binary, so it is not in git (`.gitignore` excludes `*.exe`). To ship it
to players, either:

- drop the installer here as well, for example `BloxynClient.zip`, and it appears as a second
  download item, or
- ship it beside `Bloxyn.exe` inside a `client\` folder, or write its full path into a
  `client.txt` next to `Bloxyn.exe`, which is where the bootstrapper looks first.

Without a client, pressing Play still opens the launcher, which tells the player where to get it.
