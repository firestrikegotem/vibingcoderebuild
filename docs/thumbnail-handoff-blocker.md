# Thumbnail hand-off: where it stands

Avatar and clothing thumbnails render but never reach the server. Everything up
to and including the render works; the last step does not.

## What works

- RCCService (0.256.0.17523 from archive.roblonium.com) starts from the game
  server with `-Console -Content <dir> -Port <n>` and real file handles, not pipes.
- The place loads, the character is built with its body colours, a
  `Scriptable` camera is created when the place has none, and RCC logs
  `ThumbnailGenerator::click() success` with a real PNG payload.
- Clothing shares this pipeline, so it needs no separate work.
- The website accepts the upload route on both port 5000 and port 80
  (`ApplicationGuardMiddleware` bypass + user-agent list + CSRF bypass), and the
  website proxies the request to the game server on 127.0.0.1:5001.

## What fails

`HttpService:PostAsync` in `scripts/player/thumbnail.lua` never returns. The log
ends at `[debug] [player/thumbnail] send post request containing avatar` with no
`post over` line, and the website never logs an inbound POST.

Progress made on the path there, in order:

1. `http://127.0.0.1:5001/...` -> `Trust check failed` (host not trusted).
2. `http://bbblox.org:5000/...` -> `Trust check failed` (any port in the URL
   fails the check).
3. `http://bbblox.org/...` on port 80, hosts file mapping `bbblox.org` to
   127.0.0.1 -> no trust error, but the request hangs before leaving RCC.

The website answers on port 80 over loopback (`curl http://127.0.0.1/` returns
302 from the guard), so the listener is not the problem.

## Next approach to try

Skip HTTP from inside RCC. RCC's scripting host exposes the render job over
SOAP; have the game server call the job with `BatchJobEx`/`OpenJobEx` so the
script returns `ThumbnailGenerator:Click` output as the job result, and let the
game server write the PNG itself. That removes the trust check, the port
question and the hosts file from the pipeline entirely.
