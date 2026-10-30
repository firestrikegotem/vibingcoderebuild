# Putting Bloxyn on a real domain

This is the deployment path for `bloxyn.net` (swap in whatever domain you own). It assumes
**one Windows Server machine** running everything, which is the simplest setup that can
actually launch games: `RCCService.exe` is Windows-only, and the game server spawns it
locally, so keeping the web stack on the same box avoids cross-machine auth and firewall
puzzles.

Linux works for everything except RCC. If you want to split it later, run the web stack on
Linux and keep a Windows box for the game server + RCC, then point the game server's
`baseUrl` at the public site URL.

---

## 1. What you need before you start

| Thing | Notes |
| --- | --- |
| Windows Server 2019/2022 VPS | 4 GB RAM minimum; RCC likes more. 8 GB is comfortable for a small community. |
| Your domain | `bloxyn.net` in this guide. You need access to its DNS records. |
| The repo | `git clone https://github.com/firestrikegotem/vibingcoderebuild` |
| Your `RCCService` folder | You already have this from the previous project. Copy it to `services/RCCService`. |
| The Roblox 2016 client + `Bloxyn.exe` | Client binaries go in `services/Roblox/Roblox.Website/public/downloads/`. |

Install these on the server (all 64-bit):

- **.NET 6 SDK** (needed to build; the ASP.NET Core 6 runtime ships with it)
- **Node.js 18 LTS**
- **PostgreSQL 15** (13+ works)
- **Redis** — on Windows use [Memurai](https://www.memurai.com/) (Redis-compatible Windows
  service) or run Redis in WSL. The site needs it for sessions and caches.
- **Go 1.18+** (only to build the asset validation service, `services/AssetValidationServiceV2`)
- **Caddy** (reverse proxy + automatic HTTPS) — `caddy.exe` from caddyserver.com
- **NSSM** (optional but recommended) to run each service as a Windows service

Add `git`, and make sure `dotnet`, `node`, `npm`, `psql`, `go` are on PATH for the account
you deploy with.

---

## 2. DNS

Point these at the server's public IP:

| Type | Name | Value |
| --- | --- | --- |
| A | `@` (bloxyn.net) | `<server ip>` |
| A | `www` | `<server ip>` |
| A | `*.game` | `<server ip>` |

The wildcard is what makes joins work. In a Release build the website hands the client a
per-server address like `gs1-1.game.bloxyn.net` (built from the `GameServerDomain` setting),
so every game server subdomain must resolve to the same machine.

Do not use a wildcard on the bare domain (`*.bloxyn.net`) — keep game subdomains under
`game.` so your main site cookies stay scoped to the site only.

DNS needs to be live before you request the TLS certificate in step 10.

---

## 3. Database

Open `psql` as the Postgres superuser:

```sql
CREATE USER bloxyn WITH PASSWORD 'pick-a-strong-one';
CREATE DATABASE bloxyn OWNER bloxyn;
```

Create `services/api/config.json` (gitignored, copy from the README example):

```json
{
  "knex": {
    "client": "pg",
    "connection": {
      "host": "127.0.0.1",
      "user": "bloxyn",
      "password": "pick-a-strong-one",
      "database": "bloxyn"
    }
  }
}
```

Run the migrations:

```powershell
cd services\api
npm install
npx knex migrate:latest
```

Expect all migrations to apply. Re-run this command after any `git pull` that adds one.

---

## 4. Website configuration

Copy `services/Roblox/Roblox.Website/appsettings.example.json` to `appsettings.json` (it is
gitignored, so it never lands in the repo) and go through it. The values that matter for a
public server:

```jsonc
{
  "Urls": "http://127.0.0.1:5000",          // Caddy terminates TLS and forwards here
  "BaseUrl": "https://bloxyn.net",           // must be the public https URL
  "CorsMiddlewareUrl": "http://127.0.0.1:3000",
  "GameServerDomain": "game.bloxyn.net",     // matches the *.game wildcard record

  "Postgres": "Host=127.0.0.1; Database=bloxyn; Username=bloxyn; Password=...; Maximum Pool Size=20",
  "Redis": "127.0.0.1:6379",

  "Authorization": "<random>",               // site <-> asset validation shared secret
  "BotAuthorization": "<random>",            // must equal websiteBotAuth in game-server config.json
  "GameServerAuthorization": "<random>",     // must equal authorization in game-server config.json
  "RccAuthorization": "<random>",            // must be the SAME value as GameServerAuthorization
  "Jwt": { "Sessions": "<long random string>" },

  "AssetValidation": { "BaseUrl": "http://127.0.0.1:4300", "Authorization": "<same as Authorization>" },
  "Render": { "BaseUrl": "ws://127.0.0.1:5002", "Authorization": "<random>" },

  "OwnerUserId": "1",
  "ShowErrorDetails": false,                 // never turn this on in production
  "AllowedHosts": "bloxyn.net;www.bloxyn.net;*.game.bloxyn.net",

  "HCaptcha": { "Public": "<your site key>", "Private": "<your secret>" },

  "Discord": {
    "ClientId": "1565790396091731998",
    "ClientSecret": "<your secret>",
    "GuildId": "1564144657967878224",
    "InviteUrl": "https://discord.gg/8dt3QVWd3j",
    "RedirectUri": "https://bloxyn.net/auth/discord/callback",
    "SignupGateEnabled": "true"
  }
}
```

Three things bite people:

1. **`GameServerAuthorization` and `RccAuthorization` must be identical.** The value is
   handed to the game server and then injected into the game scripts as the RCC access key,
   and the website compares what RCC sends back against `RccAuthorization`. Different values
   mean every join is rejected.
2. **`GameServerAuthorization` must also match `authorization` in the game server's
   `config.json`**, and `BotAuthorization` must match its `websiteBotAuth`.
3. **The shared paths in `Directories` keep the trailing slash.** They now resolve relative
   to the repo, so you can leave the defaults, but keep the backslashes and trailing `\` on
   Windows.

Also register the production redirect URI in the Discord developer portal
(OAuth2 → Redirects): `https://bloxyn.net/auth/discord/callback`. The localhost one can stay
for testing.

---

## 5. Frontend

```powershell
cd services\2016-roblox-main
copy config.example.json config.json     # then edit if you need non-default flags
npm install
$env:NODE_OPTIONS="--openssl-legacy-provider"
npm run build
npm run start -- -p 3000
```

Two things to know:

- `NODE_OPTIONS=--openssl-legacy-provider` is mandatory on Node 17+. Without it the webpack
  build dies on the legacy hash function.
- Always serve the built app (`npm run build` then `npm run start`), never `npm run dev`, in
  production.

The frontend is **not** exposed to the internet. The .NET site proxies every non-API request
to `127.0.0.1:3000`, so visitors only ever talk to `bloxyn.net`. In the browser the frontend
calls the API on its own origin (`/apisite/...`); server-side rendering uses
`http://127.0.0.1:5000`. Set `NEXT_PUBLIC_BACKEND_URL` / `BACKEND_URL` only if you split the
frontend onto its own host.

---

## 6. Admin panel

```powershell
cd services\admin
npm install
npm run build
```

The build lands in `services/admin/public`, which `Directories.AdminBundle` points at. The
panel is served at `https://bloxyn.net/admin`. Rebuild and restart the site after changing it.

---

## 7. Asset validation service

```powershell
cd services\AssetValidationServiceV2
go build -o AssetValidationServiceV2.exe .
.\AssetValidationServiceV2.exe
```

It listens on `127.0.0.1:4300` and must share the `Authorization` token from
`appsettings.json`. Uploads (shirts, pants, t-shirts, places, audio, images) fail without it.

---

## 8. Game server and RCC

RCC first, once, to confirm it runs:

```powershell
cd services\RCCService
.\RCCService.exe -console -placeid:1818
```

If it starts, close it. The game server launches its own instances as needed.

Then create `services/game-server/config.json` from `config.example.json`:

```jsonc
{
  "authorization": "<same as GameServerAuthorization / RccAuthorization>",
  "websiteBotAuth": "<same as BotAuthorization>",
  "baseUrl": "http://127.0.0.1:5000",
  "port": 5001,
  "rccPort": 53640,
  "thumbnailWebsocketPort": 5002,
  "rcc": "C:\\path\\to\\repo\\services\\RCCService\\",
  "content": "C:\\path\\to\\repo\\services\\RCCService\\content",
  "dockerDisabled": true
}
```

```powershell
cd services\game-server
npm install
npm run build
npm run start
```

Leave `dockerDisabled: true` on Windows. The thumbnail websocket on 5002 is also the render
service, so avatar thumbnails start working once it is up.

---

## 9. Firewall and ports

| Port | Exposure | Service |
| --- | --- | --- |
| 80 | public | Caddy (redirects to 443) |
| 443 | public | Caddy → website `:5000` |
| 5000, 3000, 4300 | localhost only | website, frontend, asset validation |
| 5001 | localhost only | game server control API (the game server calls the site, not the reverse) |
| 5002 | localhost only | thumbnail/render websocket |
| 53640+ | **public** | RCC instances players connect to |

Publish only 80, 443 and the RCC port range. Everything else should be blocked by Windows
Firewall; the site reaches the other services over loopback.

---

## 10. Reverse proxy and HTTPS

Session cookies are `Secure`, so **the site must be served over HTTPS** — plain HTTP will
look like it silently refuses to log you in.

Caddy is the least fiddly option on Windows. `C:\Caddy\Caddyfile`:

```
bloxyn.net, www.bloxyn.net {
    encode zstd gzip
    reverse_proxy 127.0.0.1:5000 {
        header_up X-Forwarded-Proto {scheme}
        header_up X-Forwarded-For {remote_host}
    }
}
```

```powershell
cd C:\Caddy
.\caddy.exe run
```

Caddy fetches and renews Let's Encrypt certificates on its own once DNS points at the box.
Forwarded headers matter: without `X-Forwarded-Proto` the site thinks it is on HTTP.

IIS alternative: install URL Rewrite + Application Request Routing, create a site for
`bloxyn.net`, add a reverse-proxy rule to `http://127.0.0.1:5000`, and bind an HTTPS
certificate (win-acme handles the certificate).

---

## 11. Keep it running

Use NSSM so nothing dies when you close the window (`nssm install <name>` → set Application
path, Startup directory, and environment variables).

| Service | Application | Start directory |
| --- | --- | --- |
| BloxynSite | `dotnet` (`run --no-build --project Roblox.Website` or the published exe) | `services\Roblox\Roblox.Website` |
| BloxynFrontend | `cmd /c npm run start -- -p 3000` | `services\2016-roblox-main` |
| BloxynAssetValidation | `AssetValidationServiceV2.exe` | `services\AssetValidationServiceV2` |
| BloxynGameServer | `cmd /c npm run start` | `services\game-server` |
| BloxynProxy | `C:\Caddy\caddy.exe run` | `C:\Caddy` |

Dependency order: PostgreSQL and Redis first, then site, frontend, asset validation, game
server, finally Caddy. For production use `dotnet publish -c Release` and run the published
output.

---

## 12. First account and first game

1. Visit `https://bloxyn.net/auth/application` and submit an application. The **name must be
   `ROBLOX` in all caps** (owner account). If yours is not auto-accepted, set the auto-accept
   flag in `services/Roblox/Roblox.Services/Users/ApplicationProcessorService.cs` (line ~99)
   to `true`, rebuild, and re-apply.
2. That account becomes user 1 (`OwnerUserId`). Sign in, then open `/admin`.
3. In the admin panel create the two maintenance accounts the upstream setup expects:
   user id 2 named `UGC` (null password), and a `BadDecisions` account that you permanently
   ban. Both names are matched literally by the code.
4. Upload a place from `/develop`, then launch it from its game page.

---

## 13. Go-live checklist

Run these in order after everything is started:

- [ ] `https://bloxyn.net/` loads the landing page (logged out)
- [ ] `https://bloxyn.net/login` renders and a login succeeds
- [ ] `https://bloxyn.net/home`, `/catalog`, `/develop`, `/users/1/profile` all render
- [ ] Sign up works and the Discord gate accepts a linked account
- [ ] `https://bloxyn.net/asset/?id=1` returns XML (send a `Requester: client` header)
- [ ] `https://bloxyn.net/Asset/CharacterFetch.ashx?userId=1` returns XML
- [ ] Shirt/pants upload from `/develop` succeeds and renders on the avatar
- [ ] `.rbxl` upload from `/develop` succeeds (asset validation must be up)
- [ ] A place launches and the client actually connects
- [ ] `https://bloxyn.net/download` lists the client installer you dropped in `public/downloads`
- [ ] Admin panel loads at `/admin` and lists users

---

## 14. Troubleshooting

| Symptom | Cause |
| --- | --- |
| Blank 500 on every page | Next.js is not running on `:3000`. The site proxies pages to it. |
| Login does nothing, no error | You are on plain HTTP. Cookies are `Secure`; use HTTPS. |
| "Game server could not be started" | RCC path in `game-server/config.json` is wrong, or the game server is not running. Check its log for the real reason. |
| Joins rejected / "bad token" | `GameServerAuthorization`, `RccAuthorization` and the game server's `authorization` disagree. |
| Uploads fail | Asset validation service is down or its `Authorization` token differs from `appsettings.json`. |
| Avatars show a placeholder | Render websocket (`:5002`) is down; that is the game server. |
| `gs1-1.game.bloxyn.net` does not resolve | Missing `*.game` wildcard A record, or `GameServerDomain` is still the default `gameserver.com`. |
| Pages render but lists are empty | Frontend cannot reach the site API: check `NEXT_PUBLIC_BACKEND_URL`/`BACKEND_URL` and that the site is on `:5000`. |
