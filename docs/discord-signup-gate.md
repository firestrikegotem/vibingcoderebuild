# Discord signup gate

Players must be a member of the Bloxyn Discord server before they can create an account.

Flow:

1. `/auth/signup` renders the usual form but with a "link your Discord" step and the submit button
   disabled.
2. "Connect Discord" goes to `/auth/discord/oauth`, which sends the player to Discord with the
   `identify guilds` scopes, a random `state` cookie (15 minutes) and a PKCE challenge (S256).
3. Discord returns to `/auth/discord/callback`. The site exchanges the code for a token, reads the
   player's guilds, and checks for `Discord:GuildId`. If the player is not in the server they are sent
   back with `?error=not_member` and no account can be created.
4. On success the site sets a signed, HttpOnly cookie (`bloxyn_discord_verify`, 30 minutes) that the
   signup handler requires. The signature is HMAC-SHA256, so the browser cannot forge membership.
5. `Signup.OnPost` re-checks the cookie, refuses to create an account without it, and refuses a
   Discord account that is already attached to another site account. On success the link is stored in
   `user_discord`.

The gate needs only the client id and the guild id. PKCE means **no client secret is required**: the
verifier proves the exchange came from the same browser. If a client secret *is* configured and Discord
rejects it, the site logs a warning and retries the exchange with PKCE only, so a stale secret cannot
lock everyone out.

The gate fails open when Discord is not configured (empty client id or guild id), so a misconfigured
install can never lock everyone out of signup. Set `Discord:SignupGateEnabled` to `false` to turn it
off deliberately.

## Do I need a second Discord application?

No. Reuse the existing **Bloxyn Service** application (app id `1565790396091731998`). It is already a
bot in the Bloxyn server, and an application can serve both the bot and the OAuth2 login. Creating a
separate app only makes sense if you want the login consent screen to show a different name.

## Setup steps

1. Open https://discord.com/developers/applications and pick **Bloxyn Service**.
2. **OAuth2** page, copy the **Client ID**.
3. On the same page click **Reset Secret** and copy the new **Client Secret**. Copy it now, Discord
   only shows it once.
4. Still on **OAuth2**, under **Redirects**, add every URL the site will use:
   - `http://127.0.0.1:5000/auth/discord/callback` (local testing)
   - `https://<your-domain>/auth/discord/callback` (production, exact scheme and host)
   Save changes.
5. Optional but recommended: on the **Bot** page, **Reset Token** and copy it. The site uses it as a
   fallback membership check that works even when a player has hidden their guild list. The bot must
   already be in the Bloxyn server (it is).
6. Fill in `services/Roblox/Roblox.Website/appsettings.json`. `ClientId` is already set to
   `1565790396091731998` and `GuildId`/`InviteUrl` are already filled in, so normally you only need
   the secret:

```json
"Discord": {
  "ClientId": "1565790396091731998",
  "ClientSecret": "paste-the-client-secret",
  "BotToken": "paste-the-bot-token-or-leave-empty",
  "GuildId": "1564144657967878224",
  "InviteUrl": "https://discord.gg/8dt3QVWd3j",
  "RedirectUri": "",
  "VerificationKey": "",
  "SignupGateEnabled": "true"
}
```

`appsettings.json` is gitignored, so these values stay on your machine. Any blank value falls
through to an environment variable, which is the recommended way to supply secrets:
`DISCORD_CLIENT_ID`, `DISCORD_CLIENT_SECRET`, `DISCORD_BOT_TOKEN`, `DISCORD_GUILD_ID`,
`DISCORD_REDIRECT_URI`.

A blank `RedirectUri` means the site derives it from the incoming request, so the same build works on
`http://127.0.0.1:5000` and on `https://bloxyn.net`. If you put the site behind a reverse proxy that
does not forward the original scheme, set `RedirectUri` explicitly to
`https://bloxyn.net/auth/discord/callback` or Discord will reject the exchange.

The **client secret** (OAuth2 page) and the **bot token** (Bot page) look almost identical: both are
about 72 characters with two dots. Pasting the bot token into the secret field produces
`invalid_client` from Discord, which the site shows as "Discord rejected this site's client secret".
The same message appears in the log with Discord's own error code.

Leaving `ClientSecret` empty is the recommended setup: PKCE covers it, and there is no secret to leak
or rotate. Set it only if you want a confidential client.

7. Apply the database change once, from `services/api`:

```bash
npx knex migrate:latest
```

This creates `user_discord` (one Discord identity per site account, and vice versa).

8. Restart the site. Visit `/auth/discord`: if the page shows "Connect Discord" and sends you to
   discord.com, the credentials are wired correctly.

## Notes

- `VerificationKey` is optional. When blank the site signs verification cookies with
  `GameServerAuthorization`, so cookies survive restarts as long as that value does not change.
- Signed-in players can link or unlink from `/auth/discord`; the unlink button posts to
  `/auth/discord/unlink`.
- Membership is checked against Discord on every link attempt. A player who leaves the server keeps
  their existing account but cannot create another one.
- The three new paths (`/auth/discord/oauth`, `/auth/discord/callback`, `/auth/discord/unlink`) are
  allowlisted in `ApplicationGuardMiddleware` and `CsrfMiddleware`; add any new Discord route in both
  places or the guard will bounce it to `/auth/captcha`.
