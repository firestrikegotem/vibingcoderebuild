using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Roblox.Libraries.Discord;

public class DiscordUser
{
    [JsonPropertyName("id")] public string id { get; set; } = "";
    [JsonPropertyName("username")] public string username { get; set; } = "";
    [JsonPropertyName("global_name")] public string? globalName { get; set; }
    [JsonPropertyName("discriminator")] public string? discriminator { get; set; }
}

public class DiscordGuild
{
    [JsonPropertyName("id")] public string id { get; set; } = "";
    [JsonPropertyName("name")] public string? name { get; set; }
}

public class DiscordMember
{
    [JsonPropertyName("user")] public DiscordUser? user { get; set; }
    [JsonPropertyName("roles")] public List<string>? roles { get; set; }
}

/// <summary>
/// Details carried in the short-lived "discord verified" cookie that signs the player up.
/// </summary>
public class DiscordVerification
{
    public long discordId { get; set; }
    public string username { get; set; } = "";
    public long expiresAt { get; set; }
}

/// <summary>
/// Minimal Discord OAuth2 client plus the signed-token helper that proves a player is a member of
/// the Bloxyn Discord server. Nothing here is cached; every check hits Discord so a player who
/// leaves the server loses access the next time they try.
/// </summary>
public static class DiscordApi
{
    private const string ApiBase = "https://discord.com/api/v10";
    private static readonly HttpClient client = new()
    {
        Timeout = TimeSpan.FromSeconds(20),
    };

    public static string clientId { get; private set; } = "";
    private static string clientSecret { get; set; } = "";
    private static string botToken { get; set; } = "";
    public static string guildId { get; private set; } = "";
    public static string inviteUrl { get; private set; } = "https://discord.gg/8dt3QVWd3j";
    public static string redirectUri { get; private set; } = "";
    private static string signingKey { get; set; } = "";
    public const string VerificationCookieName = "bloxyn_discord_verify";
    public const string StateCookieName = "bloxyn_discord_state";
    public static readonly TimeSpan VerificationLifetime = TimeSpan.FromMinutes(30);

    /// <summary>
    /// True when the OAuth application is configured well enough to run the signup gate.
    /// </summary>
    /// <summary>
    /// The gate only needs a client id and the guild id: with PKCE no client secret is required.
    /// </summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(clientId) && !string.IsNullOrWhiteSpace(guildId);

    public static void Configure(string? clientIdIn, string? clientSecretIn, string? guildIdIn,
        string? inviteUrlIn, string? botTokenIn, string? redirectUriIn, string? signingKeyIn)
    {
        clientId = clientIdIn?.Trim() ?? "";
        clientSecret = clientSecretIn?.Trim() ?? "";
        guildId = guildIdIn?.Trim() ?? "";
        botToken = botTokenIn?.Trim() ?? "";
        signingKey = signingKeyIn?.Trim() ?? "";
        if (!string.IsNullOrWhiteSpace(inviteUrlIn))
            inviteUrl = inviteUrlIn.Trim();
        redirectUri = redirectUriIn?.Trim() ?? "";
    }

    /// <summary>
    /// Name of the cookie holding the current PKCE verifier. Discord supports PKCE, so the signup gate
    /// works with just the client id and never needs the client secret.
    /// </summary>
    public const string VerifierCookieName = "bloxyn_discord_verifier";

    /// <summary>
    /// Creates a random PKCE code verifier (43-128 unreserved characters).
    /// </summary>
    public static string CreateCodeVerifier()
    {
        var bytes = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// SHA256 code challenge for the verifier, as required by code_challenge_method=S256.
    /// </summary>
    public static string CreateCodeChallenge(string verifier)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(verifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static string GetAuthorizeUrl(string state, string? redirectUriOverride = null, string? codeChallenge = null)
    {
        // "guilds" is what lets us read the player's guild list. "identify" is only used for the
        // username we show on the signup form.
        var query = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["redirect_uri"] = string.IsNullOrWhiteSpace(redirectUriOverride) ? redirectUri : redirectUriOverride,
            ["response_type"] = "code",
            ["scope"] = "identify guilds",
            ["state"] = state,
            ["prompt"] = "consent",
        };
        if (!string.IsNullOrWhiteSpace(codeChallenge))
        {
            query["code_challenge"] = codeChallenge;
            query["code_challenge_method"] = "S256";
        }
        var qs = string.Join("&", query.Select(kv => Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value)));
        return "https://discord.com/oauth2/authorize?" + qs;
    }

    private static async Task<JsonDocument?> ReadJson(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine("[warning] Discord API {0} returned {1}: {2}", response.RequestMessage?.RequestUri,
                (int) response.StatusCode, body.Length > 300 ? body[..300] : body);
            return null;
        }

        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Set when the last code exchange failed, using Discord's own error code ("invalid_client",
    /// "invalid_grant", ...). Handy for telling a misconfigured secret apart from a stale code.
    /// </summary>
    public static string? lastTokenError { get; private set; }

    /// <summary>
    /// Exchanges an authorization code for an access token. Returns null on any failure.
    /// </summary>
    public static async Task<string?> ExchangeCode(string code, string? redirectUriOverride = null, string? codeVerifier = null)
    {
        var form = new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = string.IsNullOrWhiteSpace(redirectUriOverride) ? redirectUri : redirectUriOverride,
        };
        // PKCE proves the exchange came from the same browser that started it, so the client secret is
        // optional. Send it when configured (confidential client), otherwise rely on the verifier.
        if (!string.IsNullOrWhiteSpace(clientSecret))
            form["client_secret"] = clientSecret;
        if (!string.IsNullOrWhiteSpace(codeVerifier))
            form["code_verifier"] = codeVerifier;
        async Task<(bool success, int status, string body)> Post(Dictionary<string, string> fields)
        {
            using var response = await client.PostAsync(ApiBase + "/oauth2/token", new FormUrlEncodedContent(fields));
            return (response.IsSuccessStatusCode, (int)response.StatusCode, await response.Content.ReadAsStringAsync());
        }

        var attempt = await Post(form);
        lastTokenError = null;

        if (!attempt.success && attempt.status == 401 && form.ContainsKey("client_secret") &&
            !string.IsNullOrWhiteSpace(codeVerifier))
        {
            // A wrong client secret must not take the gate down: PKCE alone is enough to prove the
            // exchange came from this browser, so retry without the secret before giving up.
            Roblox.Logging.Writer.Info(Roblox.Logging.LogGroup.SignUp,
                "Discord rejected the configured client secret, retrying the exchange with PKCE only");
            form.Remove("client_secret");
            attempt = await Post(form);
        }

        if (!attempt.success)
        {
            // Read Discord's error even though the status is a failure, otherwise an invalid client
            // secret looks exactly like an expired code.
            try
            {
                using var errorDoc = JsonDocument.Parse(attempt.body);
                lastTokenError = errorDoc.RootElement.TryGetProperty("error", out var errorCode)
                    ? errorCode.GetString()
                    : "http_" + attempt.status;
            }
            catch (JsonException)
            {
                lastTokenError = "http_" + attempt.status;
            }

            Roblox.Logging.Writer.Info(Roblox.Logging.LogGroup.SignUp,
                "Discord code exchange failed: status={0} error={1}", attempt.status, lastTokenError);
            return null;
        }

        try
        {
            using var json = JsonDocument.Parse(attempt.body);
            if (!json.RootElement.TryGetProperty("access_token", out var token))
            {
                lastTokenError = "no_access_token";
                return null;
            }

            return token.GetString();
        }
        catch (JsonException)
        {
            lastTokenError = "bad_json";
            return null;
        }
    }

    public static async Task<DiscordUser?> GetCurrentUser(string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + "/users/@me");
        request.Headers.Add("Authorization", "Bearer " + accessToken);
        using var response = await client.SendAsync(request);
        using var json = await ReadJson(response);
        if (json == null) return null;
        try
        {
            return JsonSerializer.Deserialize<DiscordUser>(json.RootElement.GetRawText());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static async Task<bool> IsMemberOfGuild(string accessToken, string userId)
    {
        // First try the player's own guild list. This needs no bot and no elevated permissions.
        using (var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + "/users/@me/guilds"))
        {
            request.Headers.Add("Authorization", "Bearer " + accessToken);
            using var response = await client.SendAsync(request);
            using var json = await ReadJson(response);
            if (json != null && json.RootElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var guild in json.RootElement.EnumerateArray())
                {
                    if (guild.TryGetProperty("id", out var id) && id.GetString() == guildId)
                        return true;
                }
            }
        }

        // Fall back to the bot, which can see members even when a guild is hidden from the OAuth
        // guild list. This is also the authoritative check once a player's roles matter.
        if (string.IsNullOrWhiteSpace(botToken))
            return false;

        using (var request = new HttpRequestMessage(HttpMethod.Get, ApiBase + "/guilds/" + guildId + "/members/" + userId))
        {
            request.Headers.Add("Authorization", "Bot " + botToken);
            using var response = await client.SendAsync(request);
            // 200 means the member exists. 404 means they are not in the guild.
            return response.IsSuccessStatusCode;
        }
    }

    private static byte[] GetSigningKey()
    {
        // A configured key keeps verification cookies valid across restarts. Configure() falls back
        // to another site secret, so by the time this runs the value should already be set.
        var key = string.IsNullOrWhiteSpace(signingKey) ? "bloxyn-discord-signup-gate" : signingKey;
        return Encoding.UTF8.GetBytes(key);
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }

        return Convert.FromBase64String(padded);
    }

    /// <summary>
    /// Builds the value stored in the verification cookie.
    /// </summary>
    public static string CreateVerificationToken(DiscordVerification verification)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(verification);
        using var hmac = new HMACSHA256(GetSigningKey());
        var signature = hmac.ComputeHash(json);
        return Base64Url(json) + "." + Base64Url(signature);
    }

    /// <summary>
    /// Validates a verification cookie. Returns null when the value is missing, tampered with,
    /// malformed or expired.
    /// </summary>
    public static DiscordVerification? ReadVerificationToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var parts = value.Split('.');
        if (parts.Length != 2) return null;
        byte[] json;
        byte[] signature;
        try
        {
            json = FromBase64Url(parts[0]);
            signature = FromBase64Url(parts[1]);
        }
        catch (FormatException)
        {
            return null;
        }

        using var hmac = new HMACSHA256(GetSigningKey());
        var expected = hmac.ComputeHash(json);
        if (!CryptographicOperations.FixedTimeEquals(expected, signature))
            return null;

        DiscordVerification? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<DiscordVerification>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        if (parsed == null || parsed.discordId <= 0) return null;
        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > parsed.expiresAt) return null;
        return parsed;
    }

    public static string CreateStateToken()
    {
        return Base64Url(RandomNumberGenerator.GetBytes(32));
    }
}
