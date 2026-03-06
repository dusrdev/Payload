using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Payload.Internal;

internal static class RepoRootDetector
{
    private static readonly string[] VcsMarkers =
    [
        ".git",
        ".svn",
        ".hg"
    ];

    private static readonly string[] IdeMarkers =
    [
        ".vs",
        ".idea"
    ];

    public static string? TryResolve(string projectDirectory, string rootDirectory, TaskLoggingHelper log)
    {
        if (!string.IsNullOrWhiteSpace(rootDirectory))
        {
            var explicitRoot = Path.GetFullPath(rootDirectory);
            if (Directory.Exists(explicitRoot))
            {
                log.LogMessage(MessageImportance.Low, $"RepoContentCopy: using explicit root '{explicitRoot}'.");
                return explicitRoot;
            }

            log.LogWarning($"RepoContentCopy: explicit PayloadRootDirectory '{rootDirectory}' does not exist. Skipping.");
            return null;
        }

        if (string.IsNullOrWhiteSpace(projectDirectory) || !Directory.Exists(projectDirectory))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(projectDirectory);
        var detectedRoot = FindDirectoryWithMarker(fullPath, VcsMarkers)
            ?? FindDirectoryWithMarker(fullPath, IdeMarkers)
            ?? FindDirectoryWithSolution(fullPath);

        if (detectedRoot is not null)
        {
            log.LogMessage(MessageImportance.Low, $"RepoContentCopy: using repository root '{detectedRoot}'.");
        }

        return detectedRoot;
    }

    private static string? FindDirectoryWithMarker(string startDirectory, IEnumerable<string> markers)
    {
        for (var current = new DirectoryInfo(startDirectory); current is not null; current = current.Parent)
        {
            try
            {
                if (markers.Any(marker => Directory.Exists(Path.Combine(current.FullName, marker))
                                          || File.Exists(Path.Combine(current.FullName, marker))))
                {
                    return current.FullName;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible directories and keep walking up.
            }
        }

        return null;
    }

    private static string? FindDirectoryWithSolution(string startDirectory)
    {
        for (var current = new DirectoryInfo(startDirectory); current is not null; current = current.Parent)
        {
            try
            {
                if (Directory.EnumerateFiles(current.FullName, "*.sln", SearchOption.TopDirectoryOnly).Any()
                    || Directory.EnumerateFiles(current.FullName, "*.slnx", SearchOption.TopDirectoryOnly).Any())
                {
                    return current.FullName;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible directories and keep walking up.
            }
        }

        return null;
    }
}
