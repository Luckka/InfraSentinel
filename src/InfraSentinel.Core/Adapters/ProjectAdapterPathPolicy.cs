namespace InfraSentinel.Core.Adapters;

public static class ProjectAdapterPathPolicy
{
    public static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) return false;
        var segments = relativePath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return !segments.Any(segment => segment is "." or ".." || segment.Contains('\0'));
    }
}
