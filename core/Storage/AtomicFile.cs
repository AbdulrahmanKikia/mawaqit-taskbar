using System.Text;

namespace Mawaqit.Core;

/// <summary>The only way the app writes its own files: write a temp file next to the
/// target, flush it to disk, then swap it in. A crash or power cut leaves either the
/// old file or the new one, never half of each.</summary>
public static class AtomicFile
{
    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void WriteAllText(string path, string contents)
    {
        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full) ?? throw new ArgumentException("No directory", nameof(path));
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(full)}.{Guid.NewGuid():N}.tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(Utf8NoBom.GetBytes(contents));
                fs.Flush(flushToDisk: true);
            }
            File.Move(tmp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }

    /// <summary>Moves an unreadable file aside (e.g. config.toml.broken-20261101-2300) so the
    /// app can start fresh and the user's data is not lost.</summary>
    public static string? QuarantineCorrupt(string path, DateTimeOffset now)
    {
        if (!File.Exists(path)) return null;
        var dest = $"{path}.broken-{now:yyyyMMdd-HHmmss}";
        File.Move(path, dest, overwrite: true);
        return dest;
    }
}
