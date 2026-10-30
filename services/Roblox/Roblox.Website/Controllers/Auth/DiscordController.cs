using Microsoft.AspNetCore.Mvc;
using Roblox.Libraries.Discord;
using Roblox.Logging;
using Roblox.Services.Exceptions;

namespace Roblox.Website.Controllers.Auth;

/// <summary>
/// Discord OAuth flow that proves a player is a member of the Bloxyn Discord server before they are
/// allowed to create an account. The result of a successful check is a short-lived signed cookie,
/// which the signup page then requires. Membership is always checked by Discord, never by the browser.
/// </summary>
[Route("/auth/discord")]
public class DiscordController : ControllerBase
{
    /// <summary>
    /// Redirect URI registered with Discord. When no URI is configured the current host is used, so
    /// the local build and https://bloxyn.net both work without touching config.
    /// </summary>
    private string GetRedirectUri() =>
        string.IsNullOrWhiteSpace(DiscordApi.redirectUri)
            ? $"{Request.Scheme}://{Request.Host}/auth/discord/callback"
            : DiscordApi.redirectUri;

    /// <summary>
    /// Kicks off the OAuth handshake. A random state value is stored in a cookie and compared when
    /// Discord sends the player back.
    /// </summary>
    [HttpGet("oauth")]
    public IActionResult StartOAuth([FromQuery] string? returnUrl)
    {
        if (!DiscordApi.IsConfigured)
        {
            Writer.Info(LogGroup.SignUp, "Discord signup gate is not configured (client id/secret/guild id)");
            return Redirect("/auth/discord?error=not_configured");
        }

        var state = DiscordApi.CreateStateToken();
        var verifier = DiscordApi.CreateCodeVerifier();
        Response.Cookies.Append(DiscordApi.StateCookieName, state, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(15),
            Path = "/",
        });

        if (string.IsNullOrEmpty(verifier))
        {
            return Redirect("/auth/discord?error=start_failed");
        }

        Response.Cookies.Append(DiscordApi.VerifierCookieName, verifier, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(15),
            Path = "/",
        });

        if (!string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"))
        {
            Response.Cookies.Append("bloxyn_discord_return", returnUrl, new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = TimeSpan.FromMinutes(15),
                Path = "/",
            });
        }

        return Redirect(DiscordApi.GetAuthorizeUrl(state, GetRedirectUri(), DiscordApi.CreateCodeChallenge(verifier)));
    }

    /// <summary>
    /// Handles the Discord redirect. On success the player gets a verification cookie and is sent back
    /// to the signup page; otherwise they go back to the Discord page with an explanation.
    /// </summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? code, [FromQuery] string? state,
        [FromQuery] string? error, [FromQuery] string? error_description)
    {
        var savedState = Request.Cookies[DiscordApi.StateCookieName];
        var verifier = Request.Cookies[DiscordApi.VerifierCookieName];
        Response.Cookies.Delete(DiscordApi.StateCookieName, new CookieOptions { Path = "/" });
        Response.Cookies.Delete(DiscordApi.VerifierCookieName, new CookieOptions { Path = "/" });

        if (!string.IsNullOrWhiteSpace(error))
        {
            Writer.Info(LogGroup.SignUp, "Discord OAuth was rejected by the player: {0}", error);
            return Redirect("/auth/discord?error=denied");
        }

        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state) ||
            string.IsNullOrWhiteSpace(savedState) || savedState != state)
        {
            Writer.Info(LogGroup.SignUp, "Discord OAuth callback failed state validation");
            return Redirect("/auth/discord?error=state");
        }

        if (!DiscordApi.IsConfigured)
            return Redirect("/auth/discord?error=not_configured");

        var accessToken = await DiscordApi.ExchangeCode(code, GetRedirectUri(), verifier);
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            Writer.Info(LogGroup.SignUp, "Discord OAuth token exchange failed");
            return Redirect(DiscordApi.lastTokenError == "invalid_client"
            ? "/auth/discord?error=credentials"
            : "/auth/discord?error=token");
        }

        var discordUser = await DiscordApi.GetCurrentUser(accessToken);
        if (discordUser == null || string.IsNullOrWhiteSpace(discordUser.id) ||
            !long.TryParse(discordUser.id, out var discordId))
        {
            Writer.Info(LogGroup.SignUp, "Discord OAuth could not read the player's account");
            return Redirect("/auth/discord?error=user");
        }

        var isMember = await DiscordApi.IsMemberOfGuild(accessToken, discordUser.id);
        Writer.Info(LogGroup.SignUp, "Discord OAuth finished. discordId={0} member={1}", discordUser.id, isMember);
        if (!isMember)
            return Redirect("/auth/discord?error=not_member");

        var verification = new DiscordVerification
        {
            discordId = discordId,
            username = discordUser.username,
            expiresAt = DateTimeOffset.UtcNow.Add(DiscordApi.VerificationLifetime).ToUnixTimeSeconds(),
        };
        Response.Cookies.Append(DiscordApi.VerificationCookieName, DiscordApi.CreateVerificationToken(verification),
            new CookieOptions
            {
                HttpOnly = true,
                Secure = Request.IsHttps,
                IsEssential = true,
                SameSite = SameSiteMode.Lax,
                MaxAge = DiscordApi.VerificationLifetime,
                Path = "/",
            });

        // Signed in players linking from their settings page go back to /auth/discord, everyone else
        // continues to whatever page asked for the link.
        if (userSession != null)
        {
            try
            {
                await services.users.LinkDiscordAccount(userSession.userId, discordUser.id, discordUser.username,
                    true);
            }
            catch (DiscordAlreadyLinkedException)
            {
                return Redirect("/auth/discord?error=already_linked");
            }

            return Redirect("/auth/discord?linked=1");
        }

        var returnUrl = Request.Cookies["bloxyn_discord_return"];
        Response.Cookies.Delete("bloxyn_discord_return", new CookieOptions { Path = "/" });
        if (string.IsNullOrEmpty(verifier))
        {
            return Redirect("/auth/discord?error=start_failed");
        }

        Response.Cookies.Append(DiscordApi.VerifierCookieName, verifier, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            IsEssential = true,
            SameSite = SameSiteMode.Lax,
            MaxAge = TimeSpan.FromMinutes(15),
            Path = "/",
        });

        if (!string.IsNullOrWhiteSpace(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//"))
            return Redirect(returnUrl);

        return Redirect("/auth/signup");
    }

    /// <summary>
    /// Removes the Discord link from the signed in player's account.
    /// </summary>
    [HttpPost("unlink")]
    public async Task<IActionResult> Unlink()
    {
        if (userSession == null) return Unauthorized();
        await services.users.UnlinkDiscordAccount(userSession.userId);
        return Redirect("/auth/discord?unlinked=1");
    }
}
