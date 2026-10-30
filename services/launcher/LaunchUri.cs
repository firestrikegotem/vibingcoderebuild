namespace Bloxyn.Launcher;

/// <summary>
/// Turning a roblox-player: launch URI into something the player client can be started with.
/// Kept apart from the Windows-only parts (registry, dialogs) so it can be exercised anywhere.
/// </summary>
public static class LaunchUri
{
    public const string Protocol = "roblox-player";
    public const string AltProtocol = "bloxyn-player";
    public const string DefaultSiteUrl = "http://localhost:5000";

    /// <summary>Finds the launch URI among the arguments, which may be "-player roblox-player:..."</summary>
    public static string? Find(IEnumerable<string> args)
    {
        return args.FirstOrDefault(arg =>
            arg.StartsWith(Protocol + ":", StringComparison.OrdinalIgnoreCase) ||
            arg.StartsWith(AltProtocol + ":", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parses "roblox-player:1+launchmode:play+gameinfo:abc+placelauncherurl:http%3A..."</summary>
    public static Dictionary<string, string> Parse(string uri)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var colon = uri.IndexOf(':');
        if (colon < 0) return result;

        foreach (var part in uri[(colon + 1)..].Split('+'))
        {
            var split = part.IndexOf(':');
            if (split <= 0) continue;
            var key = part[..split];
            var value = Uri.UnescapeDataString(part[(split + 1)..]);
            result[key] = value;
        }

        return result;
    }

    /// <summary>The website the launch came from, so the launcher knows where to send people for a download.</summary>
    public static string BaseUrlFrom(string? placeLauncherUrl)
    {
        if (!string.IsNullOrWhiteSpace(placeLauncherUrl) &&
            Uri.TryCreate(placeLauncherUrl, UriKind.Absolute, out var parsed))
            return parsed.GetLeftPart(UriPartial.Authority);
        return DefaultSiteUrl;
    }

    /// <summary>
    /// Clients built for this site take the join data as flags (that is what the site returns in
    /// retroArgs), while a stock client wants the raw URI plus -gameinfo/-placelauncherurl. Send
    /// both shapes and let the client use what it understands.
    /// </summary>
    public static string BuildClientArgs(string uri, string? placeLauncherUrl, string? ticket, string site)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append("-player \"").Append(uri).Append('"');
        builder.Append(" -app");

        if (!string.IsNullOrWhiteSpace(ticket))
        {
            builder.Append(" -gameinfo \"").Append(ticket).Append('"');
            builder.Append(" --authenticationTicket \"").Append(ticket).Append('"');
            builder.Append(" --authenticationUrl \"").Append(site.TrimEnd('/')).Append("/Login/Negotiate.ashx\"");
        }

        if (!string.IsNullOrWhiteSpace(placeLauncherUrl))
        {
            builder.Append(" -placelauncherurl \"").Append(placeLauncherUrl).Append('"');
            builder.Append(" --joinScriptUrl \"").Append(placeLauncherUrl).Append('"');
        }

        return builder.ToString();
    }
}
