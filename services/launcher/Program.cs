using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Bloxyn.Launcher;

/// <summary>
/// Bloxyn.exe - the bootstrapper that sits between the website and the player client.
///
/// It does the two jobs a Roblox bootstrapper does, but for this site:
///   1. registers the roblox-player URL scheme so a browser click reaches it,
///   2. finds the player client and starts it with the launch data from the URI.
///
/// Launch URI parsing lives in LaunchUri so it can be tested without Windows.
/// </summary>
internal static class Program
{
    private const string ClientExeName = "RobloxPlayerBeta.exe";

    private static readonly string LocalAppData =
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

    private static readonly string LogPath = Path.Combine(LocalAppData, "Bloxyn", "launcher.log");

    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            Log("start: " + string.Join(' ', args));

            var uri = LaunchUri.Find(args);
            var flags = args
                .Where(arg => arg.StartsWith("-", StringComparison.Ordinal) && !arg.StartsWith("--", StringComparison.Ordinal))
                .Select(arg => arg.ToLowerInvariant())
                .ToList();

            if (flags.Contains("-unregister"))
            {
                Unregister();
                ShowInfo("Bloxyn is no longer the handler for " + LaunchUri.Protocol + ": links.", "Bloxyn");
                return 0;
            }

            // Self-healing: every run repairs the protocol registration, so a moved or reinstalled
            // exe keeps working.
            EnsureRegistered();

            if (uri != null) return LaunchFromUri(uri);

            var client = FindClient();
            if (client == null)
            {
                OfferInstall(LaunchUri.DefaultSiteUrl);
                return 0;
            }

            ShowInfo("Bloxyn is installed.\n\nThe player was found at:\n" + client +
                     "\n\nOpen a game on the site and press Play.", "Bloxyn");
            return 0;
        }
        catch (Exception ex)
        {
            Log("error: " + ex);
            ShowError(ex.Message);
            return 1;
        }
    }

    private static int LaunchFromUri(string uri)
    {
        var data = LaunchUri.Parse(uri);
        data.TryGetValue("placelauncherurl", out var placeLauncherUrl);
        data.TryGetValue("gameinfo", out var ticket);
        var site = LaunchUri.BaseUrlFrom(placeLauncherUrl);

        var client = FindClient();
        if (client == null)
        {
            Log("no client installed, offering download");
            OfferInstall(site);
            return 2;
        }

        var arguments = LaunchUri.BuildClientArgs(uri, placeLauncherUrl, ticket, site);
        Log("launching: " + client + " " + arguments);

        Process.Start(new ProcessStartInfo(client)
        {
            Arguments = arguments,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(client) ?? AppContext.BaseDirectory,
        });
        return 0;
    }

    /// <summary>
    /// Search order: BLOXYN_CLIENT env var, client.txt next to the exe, a client folder next to the
    /// exe, then the usual per-user Versions folders.
    /// </summary>
    private static string? FindClient()
    {
        var fromEnv = Environment.GetEnvironmentVariable("BLOXYN_CLIENT");
        if (!string.IsNullOrWhiteSpace(fromEnv) && File.Exists(fromEnv)) return fromEnv;

        var here = AppContext.BaseDirectory;

        var pointer = Path.Combine(here, "client.txt");
        if (File.Exists(pointer))
        {
            var path = File.ReadAllText(pointer).Trim();
            if (File.Exists(path)) return path;
            Log("client.txt points at a missing file: " + path);
        }

        var localClientFolder = Path.Combine(here, "client");
        if (Directory.Exists(localClientFolder))
        {
            var found = Directory.GetFiles(localClientFolder, "*.exe", SearchOption.AllDirectories)
                .Where(file => !string.Equals(Path.GetFileName(file), "Bloxyn.exe", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (found != null) return found;
        }

        foreach (var root in new[]
                 {
                     Path.Combine(LocalAppData, "Bloxyn", "Versions"),
                     Path.Combine(LocalAppData, "Roblox", "Versions"),
                 })
        {
            if (!Directory.Exists(root)) continue;
            var exe = Directory.GetDirectories(root)
                .Select(dir => Path.Combine(dir, ClientExeName))
                .Where(File.Exists)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
            if (exe != null) return exe;
        }

        return null;
    }

    private static void EnsureRegistered()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exe)) exe = Process.GetCurrentProcess().MainModule?.FileName;
        if (string.IsNullOrWhiteSpace(exe)) return;

        foreach (var scheme in new[] { LaunchUri.Protocol, LaunchUri.AltProtocol })
        {
            using var root = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{scheme}");
            if (root == null) continue;
            root.SetValue(null, "URL:Bloxyn Player Protocol");
            root.SetValue("URL Protocol", string.Empty);
            using (var icon = root.CreateSubKey("DefaultIcon")) icon?.SetValue(null, $"\"{exe}\",0");
            using (var command = root.CreateSubKey(@"shell\open\command"))
                command?.SetValue(null, $"\"{exe}\" -player \"%1\"");
        }

        Log("registered protocol handler: " + exe);
    }

    private static void Unregister()
    {
        foreach (var scheme in new[] { LaunchUri.Protocol, LaunchUri.AltProtocol })
        {
            try
            {
                Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{scheme}", false);
            }
            catch (Exception ex)
            {
                Log("could not remove " + scheme + ": " + ex.Message);
            }
        }
    }

    private static void OfferInstall(string site)
    {
        var url = site.TrimEnd('/') + "/download";
        ShowInfo("The Bloxyn player is not installed yet.\n\nDownload it from:\n" + url +
                 "\n\nRun Bloxyn.exe once, then press Play on a game again.", "Bloxyn");
        TryOpen(url);
    }

    private static void TryOpen(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log("could not open browser: " + ex.Message);
        }
    }

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, DateTime.Now.ToString("O") + " " + message + Environment.NewLine);
        }
        catch
        {
            // logging must never break the launch
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "MessageBoxW")]
    private static extern int MessageBox(IntPtr hWnd, string text, string caption, uint type);

    private const uint MbIconInformation = 0x40;
    private const uint MbIconError = 0x10;

    private static void ShowInfo(string message, string caption)
    {
        try
        {
            MessageBox(IntPtr.Zero, message, caption, MbIconInformation);
        }
        catch
        {
            Log(message);
        }
    }

    private static void ShowError(string message)
    {
        try
        {
            MessageBox(IntPtr.Zero, message, "Bloxyn", MbIconError);
        }
        catch
        {
            Log("ERROR: " + message);
        }
    }
}
