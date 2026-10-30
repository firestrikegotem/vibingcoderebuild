# Uploading a place (.rbxl)

A place file saved from Roblox Studio can be uploaded on the site. The file you upload is stored as
the place's content, so the game people join is the exact place you saved.

## Where to upload

**Players** - Develop page (`/develop`): type a name, choose the `.rbxl`, press *Upload place*.

**Staff** - Admin panel, Catalog → *Upload Place*. Leaving *Place ID* empty creates a new place;
filling it in saves the file as a new version of that place. *Owner User ID* creates the place for
someone else.

## What happens to the file

1. The upload is written to a temporary file and checked (see below).
2. `AssetsService.CreateAsset` stores the bytes under `Configuration.AssetDirectory` using the
   SHA-256 of the content as the file name, then inserts:
   - an `asset` row (`asset_type` = place, moderation status approved)
   - an `asset_version` row whose `content_url` points at the stored file
   - an `asset_place` row with the default server settings
3. `GamesService.CreateUniverse` inserts the `universe` (creator copied from the asset) and the
   `universe_asset` link, which is what makes the place show up as a game and become joinable.
4. When a player joins, the game server fetches the place through `GET /asset/?id=<placeId>`, which
   serves the stored file.

Because the place is created through the normal services, it appears in the creator's games list and
on the home/games pages without any extra step.

## Endpoints

| Endpoint | Who | Purpose |
| --- | --- | --- |
| `POST /apisite/develop/v1/places/upload` | logged-in user | Multipart `name`, `description` (optional), `file`. Creates a place + universe. |
| `POST /apisite/develop/v1/places/{placeId}/upload` | place owner | Same fields. Saves the file as a new version of that place. |
| `POST /admin-api/api/place/upload` | staff with `CreateAsset` | Same, plus optional `placeId` (new version) and `creatorId` (owner). |

Limits: 100 MB per file, 10 new places per user per hour.

## Validation

Two layers, so a broken place is caught before it reaches the game server:

1. `PlaceFileInspector` reads the file header and accepts the two real formats - binary
   (`<roblox!` + `89 FF 0D 0A 1A 0A`) and XML (`<roblox ...>`). The file must also contain a
   `Workspace` and a `Lighting`, otherwise it is rejected as "not a playable place".
2. `AssetValidationServiceV2` (`POST /api/v1/validate-place`) parses the file with the rbxfile reader
   and checks the same two services. It is authoritative when it is running; if it is unreachable the
   upload continues on the layer 1 checks, with a warning in the log.

## Smoke test

`scripts/upload-place.sh` logs in, uploads a file, downloads it back through `/asset/?id=`, and
compares the bytes:

```bash
scripts/upload-place.sh ~/Baseplate.rbxl "My Game" admin
```

It prints the new place and universe ids, and the game URL to open. Use `BASE_URL` to point it at a
different address.
