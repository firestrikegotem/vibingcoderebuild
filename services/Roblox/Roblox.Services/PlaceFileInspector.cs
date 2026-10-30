using System.Text;

namespace Roblox.Services;

public class PlaceFileInspectionResult
{
    /// <summary>True when the file is a Roblox place (.rbxl) and contains a Workspace and Lighting.</summary>
    public bool IsPlaceFile { get; set; }

    /// <summary>True for the binary .rbxl format, false for the XML format.</summary>
    public bool IsBinary { get; set; }

    /// <summary>Set when IsPlaceFile is false, with a message that can be shown to the uploader.</summary>
    public string? Error { get; set; }
}

/// <summary>
/// Cheap structural check for uploaded place files. Roblox Studio saves places in two formats:
///   XML:    a &lt;roblox&gt; document (optionally behind an &lt;?xml ...?&gt; declaration) whose
///           instances are listed as attributes.
///   Binary: "&lt;roblox!" followed by the rbxl magic (89 FF 0D 0A 1A 0A).
/// Both formats store service class names as plain ASCII, so a place can be confirmed by looking for
/// the Workspace and Lighting services without pulling in a full file parser. The asset validation
/// service is still the authority when it is available; this only keeps uploads safe when it is not.
/// </summary>
public static class PlaceFileInspector
{
    private static readonly byte[] RobloxTag = {(byte) '<', (byte) 'r', (byte) 'o', (byte) 'b', (byte) 'l', (byte) 'o', (byte) 'x'};
    private static readonly byte[] BinaryMagic = {0x89, 0xFF, 0x0D, 0x0A, 0x1A, 0x0A};
    private const string WorkspaceToken = "Workspace";
    private const string LightingToken = "Lighting";
    private const int HeaderReadSize = 512;

    private static int IndexOf(byte[] haystack, int haystackLength, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystackLength; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match) return i;
        }

        return -1;
    }

    public static async Task<PlaceFileInspectionResult> Inspect(Stream stream, CancellationToken cancellationToken = default)
    {
        var result = new PlaceFileInspectionResult();
        if (!stream.CanRead)
        {
            result.Error = "That file could not be read.";
            return result;
        }

        if (stream.CanSeek) stream.Position = 0;
        var header = new byte[HeaderReadSize];
        var read = await stream.ReadAsync(header.AsMemory(0, header.Length), cancellationToken);
        if (read < 8)
        {
            result.Error = "That file is too small to be a place file.";
            return result;
        }

        // A raw binary file starts with the magic directly. Otherwise look for the <roblox tag, which
        // may sit behind an XML declaration.
        var legacyBinary = header[0] == BinaryMagic[0] && header[1] == BinaryMagic[1] && header[2] == BinaryMagic[2] &&
                           header[3] == BinaryMagic[3] && header[4] == BinaryMagic[4] && header[5] == BinaryMagic[5];
        var tagIndex = IndexOf(header, read, RobloxTag);
        if (!legacyBinary && tagIndex < 0)
        {
            result.Error = "That is not a Roblox place file. Upload the .rbxl file you saved from Roblox Studio.";
            return result;
        }

        if (tagIndex >= 0)
        {
            // "<roblox" + "!" + magic is the binary format. "<roblox " (or a newline/attribute after
            // the tag) is the XML format.
            var magicStart = tagIndex + RobloxTag.Length + 1;
            if (magicStart + BinaryMagic.Length <= read && header[tagIndex + RobloxTag.Length] == (byte) '!')
            {
                var isBinary = true;
                for (var i = 0; i < BinaryMagic.Length; i++)
                {
                    if (header[magicStart + i] != BinaryMagic[i])
                    {
                        isBinary = false;
                        break;
                    }
                }

                result.IsBinary = isBinary;
            }
        }
        else
        {
            result.IsBinary = true;
        }

        if (stream.CanSeek) stream.Position = 0;
        var hasWorkspace = false;
        var hasLighting = false;
        var buffer = new byte[1024 * 1024 + 64];
        var carry = 0;
        while (true)
        {
            var got = await stream.ReadAsync(buffer.AsMemory(carry, buffer.Length - carry), cancellationToken);
            if (got <= 0) break;
            var length = carry + got;
            var text = Encoding.UTF8.GetString(buffer, 0, length);
            if (!hasWorkspace && text.Contains(WorkspaceToken, StringComparison.Ordinal)) hasWorkspace = true;
            if (!hasLighting && text.Contains(LightingToken, StringComparison.Ordinal)) hasLighting = true;
            if (hasWorkspace && hasLighting) break;

            // Keep the tail so a token split across two reads is still found
            carry = Math.Min(31, length);
            Array.Copy(buffer, length - carry, buffer, 0, carry);
        }

        if (!hasWorkspace || !hasLighting)
        {
            result.Error =
                "That file has no Workspace or Lighting, so it is not a playable place. Save the full place from Roblox Studio and upload the .rbxl.";
            return result;
        }

        result.IsPlaceFile = true;
        return result;
    }
}
