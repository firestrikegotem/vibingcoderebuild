using Bloxyn.Launcher;

// Plain asserts: this runs anywhere, and fails loudly with a non-zero exit code.
var failures = 0;

void Check(string name, bool condition, string? detail = null)
{
    if (condition)
    {
        Console.WriteLine("  ok   " + name);
        return;
    }

    failures++;
    Console.WriteLine("  FAIL " + name + (detail == null ? "" : " -> " + detail));
}

void CheckEqual(string name, string? expected, string? actual)
{
    Check(name, expected == actual, $"expected <{expected}>, got <{actual}>");
}

// A URI shaped exactly like the one the website builds (services/2016-roblox-main/services/games.js)
const string uri = "roblox-player:1+launchmode:play+gameinfo:TICKET123+launchtime:1793385053" +
                   "+placelauncherurl:http%3A%2F%2Flocalhost%3A5000%2Fplacelauncher.ashx%3Fticket%3DTICKET123" +
                   "+browsertrackerid:0+robloxlocale:en_us+gamellocale:en_us";

Console.WriteLine("LaunchUri");

var launchArgs = new[] { "-player", uri };
CheckEqual("Find picks the launch URI out of -player <uri>", uri, LaunchUri.Find(launchArgs));
Check("Find ignores a run without a URI", LaunchUri.Find(Array.Empty<string>()) == null);
Check("Find accepts the alternate scheme",
    LaunchUri.Find(new[] { "bloxyn-player:1+launchmode:play" }) == "bloxyn-player:1+launchmode:play");

var parsed = LaunchUri.Parse(uri);
CheckEqual("launchmode", "play", parsed.GetValueOrDefault("launchmode"));
CheckEqual("gameinfo is the join ticket", "TICKET123", parsed.GetValueOrDefault("gameinfo"));
CheckEqual("placelauncherurl is percent-decoded",
    "http://localhost:5000/placelauncher.ashx?ticket=TICKET123", parsed.GetValueOrDefault("placelauncherurl"));
CheckEqual("launchtime survives as text", "1793385053", parsed.GetValueOrDefault("launchtime"));
Check("keys are case-insensitive", parsed.ContainsKey("GameInfo"));

Check("empty input parses to nothing", LaunchUri.Parse("roblox-player:").Count == 0);
Check("a bare scheme parses to nothing", LaunchUri.Parse("roblox-player").Count == 0);

CheckEqual("site comes from the launch URL", "http://localhost:5000",
    LaunchUri.BaseUrlFrom("http://localhost:5000/placelauncher.ashx?ticket=x"));
CheckEqual("site keeps a non-default port", "https://bloxyn.example:8443",
    LaunchUri.BaseUrlFrom("https://bloxyn.example:8443/placelauncher.ashx"));
CheckEqual("missing URL falls back to the default site", LaunchUri.DefaultSiteUrl, LaunchUri.BaseUrlFrom(null));
CheckEqual("garbage falls back to the default site", LaunchUri.DefaultSiteUrl, LaunchUri.BaseUrlFrom("not a url"));

var clientArgs = LaunchUri.BuildClientArgs(uri, parsed["placelauncherurl"], parsed["gameinfo"], "http://localhost:5000");
Check("passes the raw URI to the client", clientArgs.StartsWith("-player \"roblox-player:1+launchmode:play"));
Check("marks the launch as an app launch", clientArgs.Contains(" -app "));
Check("passes -gameinfo", clientArgs.Contains("-gameinfo \"TICKET123\""));
Check("passes -placelauncherurl", clientArgs.Contains("-placelauncherurl \"http://localhost:5000/placelauncher.ashx?ticket=TICKET123\""));
Check("passes --authenticationTicket", clientArgs.Contains("--authenticationTicket \"TICKET123\""));
Check("passes --authenticationUrl", clientArgs.Contains("--authenticationUrl \"http://localhost:5000/Login/Negotiate.ashx\""));
Check("passes --joinScriptUrl", clientArgs.Contains("--joinScriptUrl \"http://localhost:5000/placelauncher.ashx?ticket=TICKET123\""));
CheckEqual("a ticket-free launch still produces player args", "-player \"roblox-player:1\" -app",
    LaunchUri.BuildClientArgs("roblox-player:1", null, null, "http://localhost:5000"));

Console.WriteLine();
Console.WriteLine(failures == 0 ? "all checks passed" : failures + " check(s) failed");
return failures == 0 ? 0 : 1;
