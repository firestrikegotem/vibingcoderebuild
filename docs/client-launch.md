# How the player gets downloaded and launched

There are two binaries involved, and they do different jobs:

| Binary | Job |
| --- | --- |
| Bootstrapper (Roblox's `RobloxPlayerLauncher.exe`) | Installs the client, registers the `roblox-player:` URL scheme, then starts the client with the launch arguments |
| Player (`RobloxPlayerBeta.exe`, inside the `version-<hash>` folder) | Turns the launch arguments into a join script request, connects to the game server and runs the place |

The site only ever talks to them through HTTP endpoints and one custom URL scheme, which is why the
exe files are not in this repository (`*.exe` is in `.gitignore`).

## 1. The bootstrapper

A stock Roblox bootstrapper does three things:

1. **Installs the client** into `%LOCALAPPDATA%\Roblox\Versions\version-<hash>\`. On a real Roblox
   install it downloads that folder; a revival's bootstrapper has to ship the folder or fetch it
   from the revival's own site, because there is no Roblox CDN to ask.
2. **Registers the URL scheme.** Browsers cannot start an exe directly, so the bootstrapper writes:

   ```
   HKEY_CLASSES_ROOT\roblox-player
     URL Protocol = ""
     shell\open\command = "<path to launcher>" -player "%1"
   ```

   `URL Protocol` is what makes Windows treat it as a protocol handler, and `%1` is the URI the
   browser passes through (see *Registering an Application to a URI Scheme* in the Microsoft docs).
3. **Starts the player**, either with the URI it received or with nothing (first run), which is why
   the same exe is both the installer and the launch entry point.

## 2. What the site does

Clicking **Play** on a game runs `launchGame` in `services/2016-roblox-main/services/games.js`:

```
1. GET  /game/get-join-script?placeId=<id>          -> { joinScriptUrl, retroArgs }
2. build roblox-player:1+launchmode:play+gameinfo:<ticket>+launchtime:<now>
       +placelauncherurl:<joinScriptUrl>+browsertrackerid:0+robloxLocale:en_us+gameLocale:en_us
3. click it, so Windows hands the URI to the registered handler
```

The handler (bootstrapper, then player) continues the chain against this site:

```
4. GET  /placelauncher.ashx?ticket=<ticket>         -> status, job, joinScriptUrl, authenticationUrl
5. GET  /Game/join.ashx?ticket=<ticket>&job=<job>   -> the join script
6. WS   <WebsocketAddress>                          -> connect to the game server with ClientTicket
7. GET  /Asset/CharacterFetch.ashx?userId=<id>      -> what the avatar wears, then the place loads
```

The join script in `BypassController.JoinGame` is the piece the client needs most:

- `WebsocketAddress` - `domain:port` of the allocated RCC instance, ports 53640+ from
  `GameServer.Ports`, where `domain` is what the RCC reports when it registers with `/gs/`.
- `ClientTicket` - the same ticket, checked again by the game server.
- `MachineAddress` / `ServerPort` - deliberately fake ("IP and Port don't serve a purpose anymore"),
  kept so old client code has values to read.
- `BaseUrl`, `CharacterAppearance`, `UserId`, `MembershipType`, `AccountAge` - the identity and asset
  base the client uses for everything after the join.
- `Game/join.ashx` is only served when the ticket's hashed IP still matches the requester, and the
  ticket expires after 5 minutes, so the launch has to happen promptly from the same machine.

## 3. What a revival has to change, and what this repo already does

| Piece | Real Roblox | A revival |
| --- | --- | --- |
| Where the client goes for the launch script | `roblox.com` | This site: the `placelauncherurl` is `Configuration.BaseUrl + /placelauncher.ashx`, already correct |
| Login | `Login/Negotiate.ashx` returns a security token | The endpoint exists but returns an empty body, so the client must take its identity from `-gameinfo` / `authenticationTicket`, which is what `retroArgs` provides |
| Asset and avatar URLs | `www.roblox.com` | `BaseUrl` in the join script, plus `/Asset/...` handled by `BypassController` |
| Anything still hardcoded in the exe | - | Patch the strings in the exe, or map the Roblox domains to this server in the hosts file |
| Client version | Always current | Must match the RCC build, and must speak the websocket join this join script describes |

## 4. Wiring the two exes you have

1. Put the bootstrapper in `services/api/public/downloads/`. It shows up on the Download tab
   automatically (`GET /apisite/download/v1/clients`).
2. Run it once and confirm it registers `roblox-player` in the registry, pointing at itself with
   `-player "%1"`.
3. Confirm the client folder it installs is the version your `services/RCCService` build expects.
   A mismatch fails at the websocket join, not at the launch, so the browser will look fine and the
   client will drop out.
4. Start the pieces the join depends on: RCC through `services/game-server/start-rcc.sh <port>`
   (Docker, ports 53640+), the game server itself, and the website. `WebsocketAddress` points at
   `localhost:53640` in DEBUG builds of the website, so check which configuration you are running.
5. Click Play on a game and watch the website log for `placelauncher.ashx` followed by
   `Game/join.ashx`. If the first appears and the second does not, the IP check or the 5 minute
   ticket lifetime is the usual cause.

## 5. What is still missing here

The exe files and the patched client are not in this repository, and the client strings have to be
patched to point at your site rather than Roblox's, unless you run the whole thing behind a hosts
file that maps the Roblox domains to your server. The Download tab, the endpoints it uses and the
whole server half of the handshake are done.
