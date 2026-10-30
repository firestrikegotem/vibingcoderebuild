using Microsoft.AspNetCore.Mvc;

namespace Roblox.Website.Controllers;

/// <summary>
/// Serves the player build listed on the Download page. Player installers are dropped into
/// PublicDirectory/downloads/ and appear on the site straight away - no restart, config or
/// rebuild needed. Nothing is hardcoded, so adding a new build is just copying a file.
/// </summary>
[ApiController]
[Route("/apisite/download/v1")]
public class DownloadController
{
    private static string DownloadDirectory => Path.Combine(Configuration.PublicDirectory, "downloads");

    private static readonly string[] AllowedExtensions =
        {".exe", ".zip", ".msi", ".dmg", ".apk", ".7z", ".rar"};

    [HttpGet("clients")]
    public dynamic GetClients()
    {
        var files = new List<object>();
        var directory = DownloadDirectory;
        if (Directory.Exists(directory))
        {
            foreach (var path in Directory.GetFiles(directory).OrderBy(v => v))
            {
                var file = new FileInfo(path);
                var extension = file.Extension.ToLowerInvariant();
                if (!AllowedExtensions.Contains(extension)) continue;
                files.Add(new
                {
                    name = Path.GetFileNameWithoutExtension(file.Name),
                    fileName = file.Name,
                    extension = extension.TrimStart('.'),
                    sizeInBytes = file.Length,
                    updatedAt = file.LastWriteTimeUtc,
                    url = "/apisite/download/v1/client/" + Uri.EscapeDataString(file.Name),
                });
            }
        }

        return new
        {
            data = files,
        };
    }

    [HttpGet("client/{fileName}")]
    public IActionResult GetClient(string fileName)
    {
        // Only serve a plain file name from the downloads folder, so a crafted path can never
        // reach anything else on disk.
        var safeName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeName) || safeName != fileName) return new BadRequestResult();
        if (!AllowedExtensions.Contains(Path.GetExtension(safeName).ToLowerInvariant())) return new BadRequestResult();

        var directory = Path.GetFullPath(DownloadDirectory);
        var path = Path.GetFullPath(Path.Combine(directory, safeName));
        if (!path.StartsWith(directory, StringComparison.Ordinal) || !System.IO.File.Exists(path))
            return new NotFoundResult();

        return new PhysicalFileResult(path, "application/octet-stream")
        {
            FileDownloadName = safeName,
        };
    }
}
