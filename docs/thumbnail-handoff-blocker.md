# Thumbnail hand-off: where it stands

Avatar and clothing thumbnails render but never reach the server. Everything up
to and including the render works; the last step does not.

## Attempted fix: collect the render over SOAP

HTTP from inside RCC is out of the picture, so the render no longer has to upload
anything. Each render script now builds its JSON payload, parks it on the job
(`_G.__bloxynThumbnail` plus a `__BloxynThumbnail` StringValue in ServerStorage)
*before* attempting its post, and then posts as before. The game server polls
`BatchJobEx` on the job every 3 seconds, reads the parked payload out of the
SOAP response, and feeds it to the same callback the HTTP upload used to reach.
The old upload path is still there, so an RCC build whose HTTP works keeps
working.

What to look for in the game server console after a redraw:

- `[info] collected render result over SOAP for job <id>` - the payload came
  back and the usual resize/compression ran on it. Thumbnails are fixed.
- `[info] no render result on the job yet, will keep checking` followed by
  nothing - this RCC build rejects `BatchJobEx` (or runs each script in a fresh
  `_G`), which is where the next round of work starts.

## Previous notes

The sections below describe how the HTTP route was narrowed down before the
SOAP collector was added.

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

## Notes for whoever picks this up

The hosts file entries and the port-80 bindings are no longer needed by the
render path, but leave them: the 2016 client and the website both use
`bbblox.org` on port 80, and a leftover `::1 bbblox.org` line should be removed
if it is present (it makes `bbblox.org` resolve to a dead IPv6 listener).
