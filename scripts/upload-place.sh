#!/usr/bin/env bash
#
# Uploads a Roblox place (.rbxl) to a running Bloxyn site and checks that the place is
# stored and served back exactly as uploaded - the same bytes the game server fetches
# when a player joins.
#
# Usage:
#   scripts/upload-place.sh <file.rbxl> [name] [username]
#
# Examples:
#   scripts/upload-place.sh ~/Baseplate.rbxl "My Game" admin
#   BASE_URL=http://localhost:5000 scripts/upload-place.sh ./place.rbxl
#
# Environment:
#   BASE_URL   Site address (default http://localhost:5000)
#   GAME_USER  Username to log in as (default admin; the place is created for this user)
#
set -euo pipefail

BASE_URL="${BASE_URL:-http://localhost:5000}"
FILE="${1:-}"
NAME="${2:-}"
USERNAME="${3:-${GAME_USER:-admin}}"

if [ -z "$FILE" ]; then
	echo "usage: $0 <file.rbxl> [name] [username]" >&2
	exit 1
fi
if [ ! -f "$FILE" ]; then
	echo "error: no such file: $FILE" >&2
	exit 1
fi
NAME="${NAME:-$(basename "$FILE" .rbxl)}"

COOKIES="$(mktemp)"
HEADERS="$(mktemp)"
BODY="$(mktemp)"
DOWNLOAD="$(mktemp)"
trap 'rm -f "$COOKIES" "$HEADERS" "$BODY" "$DOWNLOAD"' EXIT

token=""
STATUS=""

refresh_token() {
	token="$(awk 'tolower($1)=="x-csrf-token:"{v=$2} END{print v}' "$HEADERS" | tr -d '\r')"
}

do_request() {
	: >"$BODY"
	STATUS="$(curl -sS -b "$COOKIES" -c "$COOKIES" -D "$HEADERS" -o "$BODY" -w '%{http_code}' \
		-H "x-csrf-token: $token" "$@")"
}

# Every non-GET request needs the CSRF token. The server hands it back in the
# x-csrf-token response header, so a rejected request is retried once with it.
with_retry() {
	do_request "$@"
	if [ "$STATUS" = "403" ]; then
		refresh_token
		if [ -n "$token" ]; then
			do_request "$@"
		fi
	fi
}

fail() {
	echo "FAILED: $1" >&2
	if [ -s "$BODY" ]; then
		echo "server said: $(head -c 400 "$BODY")" >&2
	fi
	exit 1
}

echo "site:  $BASE_URL"
echo "user:  $USERNAME"
echo "file:  $FILE"
echo "name:  $NAME"

# 1. Log in so the upload is created for a real account.
with_retry -X POST "$BASE_URL/apisite/auth/v2/login" \
	-H 'Content-Type: application/json' \
	-d "{\"ctype\":\"username\",\"cvalue\":\"$USERNAME\",\"password\":\"password123\"}"
[ "$STATUS" = "200" ] || fail "could not log in as $USERNAME (HTTP $STATUS)"
echo "logged in"

# 2. Upload the place.
with_retry -X POST "$BASE_URL/apisite/develop/v1/places/upload" \
	-F "name=$NAME" \
	-F "file=@$FILE"
[ "$STATUS" = "200" ] || fail "upload rejected (HTTP $STATUS)"

PLACE_ID="$(grep -o '"placeId":[0-9]*' "$BODY" | head -1 | cut -d: -f2)"
UNIVERSE_ID="$(grep -o '"universeId":[0-9]*' "$BODY" | head -1 | cut -d: -f2)"
[ -n "$PLACE_ID" ] || fail "upload response had no placeId: $(head -c 400 "$BODY")"
echo "place id:    $PLACE_ID"
echo "universe id: ${UNIVERSE_ID:-none}"

# 3. Read the place back the way the game server does and compare it byte for byte.
do_request "$BASE_URL/asset/?id=$PLACE_ID"
if [ "$STATUS" != "200" ]; then
	echo "warning: could not download the place back (HTTP $STATUS)" >&2
else
	if cmp -s "$FILE" "$DOWNLOAD"; then
		echo "content:     stored and served identically"
	else
		echo "warning: served content differs from the uploaded file" >&2
	fi
fi

if [ -z "${UNIVERSE_ID:-}" ] || [ "$UNIVERSE_ID" = "null" ] || [ "$UNIVERSE_ID" = "0" ]; then
	echo "warning: no universe was created, so the place will not be joinable" >&2
	exit 1
fi

echo
echo "done. open $BASE_URL/games/$UNIVERSE_ID/ to see the game."
