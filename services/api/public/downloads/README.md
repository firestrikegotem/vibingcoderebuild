# Player builds

Drop the player installer in this folder (for example `BloxynPlayer.exe` or a `.zip`).
It shows up on the Download page immediately, with no restart or config change.

Served by `GET /apisite/download/v1/clients` and `/apisite/download/v1/client/<file>`.
Allowed types: exe, zip, msi, dmg, apk, 7z, rar.
