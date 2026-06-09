namespace MpsMes.ViewModels;

using System.IO;

internal static class PathHelper
{
    public static string? ResolveFile(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidates = new[]
        {
            Path.Combine(baseDir, path),
            Path.Combine(Directory.GetCurrentDirectory(), path),
            Path.Combine(baseDir, "..", path),
            Path.Combine(baseDir, "..", "..", path),
            Path.Combine(baseDir, "..", "..", "..", path),
        };

        foreach (var candidate in candidates)
        {
            var full = Path.GetFullPath(candidate);
            if (File.Exists(full)) return full;
        }

        return Path.GetFullPath(Path.Combine(baseDir, path));
    }
}
