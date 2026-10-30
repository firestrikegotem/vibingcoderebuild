# Running Bloxyn on Windows

End-to-end checklist for a single Windows machine. It covers the parts that are not in git: the
RCC build and the player client.

## 1. Folder layout

```
services\
  RCCService\            RCCService.exe, content\, platformcontent\, shaders\, VC90 dlls
  Roblox\Roblox.Website\ the website (.NET, port 5000)
  game-server\           Node service that starts RCC and hosts the games (port 5001)
  AssetValidationServiceV2\
  2016-roblox-main\      Next.js frontend (port 3000)
```

`services\runall.bat` opens one window per service and warns if `RCCService.exe` is missing. It works
from any folder name.

## 2. `services\game-server\config.json`

Copy `config.example.json` to `config.json` (git ignores the copy) and set:

| Key | Value |
| --- | --- |
| `authorization` | one shared key, used for website to game-server calls **and** as the RCC `accessKey` |
| `websiteBotAuth` | same value as `BotAuthorization` in `appsettings.json` |
| `port` | `5001` |
| `rccPort` | `53640` (first port of the range the website hands to clients) |
| `rcc` | full path to the `RCCService` folder, ending in a backslash |
| `content` | full path to `RCCService\content` |
| `dockerDisabled` | `true` (Windows runs `RCCService.exe` directly, not Docker) |

## 3. Website config

`appsettings.json` and `game-servers.json` are both gitignored, so a fresh clone has neither and the
site will not start. Copy the examples:

```
cd services\Roblox\Roblox.Website
copy appsettings.example.json appsettings.json
copy game-servers.example.json game-servers.json
```

Then set the Postgres password and the `Directories` paths in `appsettings.json`.

`Twitter:Bearer` only has to be non-empty: a blank value throws `Bad token` at startup and kills the
site before it listens. It is only used by the social-link verification page, so any placeholder works
locally. `Discord:SignupGateEnabled` set to `"false"` keeps signup open without linking a Discord
account; delete the key to require the link again.

Two keys must agree, or joining fails with an authorization error:

- `GameServerAuthorization` and `RccAuthorization` must be the **same string**, and that string must be
  `authorization` in the game server's `config.json`. The website sends it to the game server, and the
  game server injects it into every script as `AccessKey`, which the website then checks against
  `RccAuthorization`.

`game-servers.json` is read by `Program.cs`, separately from `appsettings.json`. A `GameServers` block
inside `appsettings.json` is ignored:

```json
{
  "GameServers": [
    { "ip": "127.0.0.1:5001", "domain": "127.0.0.1:53640", "maxServerCount": 10 }
  ]
}
```

`ip` is the game server's HTTP API and must match its `port` in `config.json`. `domain` is what the
client is told to connect to, so it has to be the port RCC listens on. Both files are optional at
startup now: with no `game-servers.json` the site reports no game servers instead of crashing.

## 4. RCC

The game server spawns `RCCService.exe -Console -PlaceId:1818 -port <free port from 53640> -Content <content folder>`
itself, once per running game. Do not start a second copy by hand on the same port.

- Ports: 53640 to 53649, matching `GameServerPorts` in `Roblox.Services/Games/GameServer.cs`.
- One game running = one RCC process. Five players in the same game share it.
- If it dies immediately, run it by hand to see the error:
  `RCCService.exe -Console -PlaceId:1818 -port 53640 -Content content`
- The `Microsoft.VC90.*` folders are the Visual C++ 2008 runtime it needs; keep them next to the exe.

## 5. Start order

1. Postgres and Redis.
2. Website: `services\Roblox\Roblox.Website\run.bat` (port 5000).
3. Game server: `services\game-server\run.bat` (port 5001).
4. Asset validation: `services\AssetValidationServiceV2\run.bat`.
5. Frontend: `services\2016-roblox-main\run.bat` (port 3000).
6. Upload a place on Develop, open it, press Play. RCC starts on demand.

## 6. What success looks like

- Website log: `placelauncher.ashx` then `Game/join.ashx` for the same player.
- Game server window: `[info] found port for rcc!` followed by the RCC console output.
- Client: `Bloxyn.exe` launches, the 2016 client loads the place and the character spawns.

| Symptom | Cause |
| --- | --- |
| `placelauncher.ashx` logs, `join.ashx` never does | ticket expired (5 minutes) or an IP mismatch |
| Join script returns a `WebsocketAddress` but the client drops | RCC is not listening on that port |
| `Unexpected statusCode` from the game server | `authorization` in `config.json` differs from `GameServerAuthorization` |
| Grey mannequin / no textures | asset URLs must use `127.0.0.1`, not `localhost` (RCC TrustCheck) |
