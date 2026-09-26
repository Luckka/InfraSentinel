namespace InfraSentinel.Core.Adapters;

internal sealed class ProjectAdapterFileReader(ProjectAdapterRequest request)
{
    private readonly string root = Path.GetFullPath(request.WorkspaceRoot);
    private readonly HashSet<string> excluded = new(request.Options.EffectiveExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyList<string> FindFiles(IEnumerable<string> patterns, List<string> ignored)
    {
        var results = new List<string>();
        foreach (var pattern in patterns.Order(StringComparer.Ordinal))
        {
            foreach (var path in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).Order(StringComparer.Ordinal))
            {
                if (!IsSafe(path))
                {
                    ignored.Add(ToLogicalPath(path));
                    continue;
                }
                if (results.Contains(path, StringComparer.Ordinal)) continue;
                if (results.Count >= request.Options.MaxFiles)
                    throw new InvalidOperationException($"File limit exceeded ({request.Options.MaxFiles}).");
                var length = new FileInfo(path).Length;
                if (length > request.Options.MaxFileBytes)
                {
                    ignored.Add(ToLogicalPath(path) + " (size-limit)");
                    continue;
                }
                results.Add(path);
            }
        }
        return results;
    }

    public string ReadText(string path)
    {
        var full = ResolveSafe(path);
        var info = new FileInfo(full);
        if (info.Length > request.Options.MaxFileBytes)
            throw new InvalidOperationException($"File exceeds configured size limit: {ToLogicalPath(full)}");
        return File.ReadAllText(full);
    }

    public string ToLogicalPath(string path)
        => "/" + Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');

    private string ResolveSafe(string path)
    {
        if (!Path.IsPathRooted(path) && !ProjectAdapterPathPolicy.IsSafeRelativePath(path))
            throw new InvalidOperationException("Relative path is not allowed.");
        var full = Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(root, path));
        if (!IsWithinRoot(full)) throw new InvalidOperationException("Path escapes the project workspace.");
        if (!File.Exists(full)) throw new FileNotFoundException("Project file was not found.", full);
        return full;
    }

    private bool IsSafe(string path)
    {
        var info = new FileInfo(path);
        if ((info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
        if (!IsWithinRoot(path)) return false;
        var relative = Path.GetRelativePath(root, path);
        return !relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => excluded.Contains(segment));
    }

    private bool IsWithinRoot(string path)
    {
        var full = Path.GetFullPath(path);
        var boundary = root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.Equals(root, StringComparison.Ordinal) || full.StartsWith(boundary, StringComparison.Ordinal);
    }
}
