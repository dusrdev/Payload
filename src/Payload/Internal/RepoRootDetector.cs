using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Payload.Internal;

internal static class RepoRootDetector
{
    private static readonly MarkerProbe[] Markers =
    [
        new(".git", 0, MarkerMatchMode.Path),
        new(".svn", 0, MarkerMatchMode.Path),
        new(".hg", 0, MarkerMatchMode.Path),
        new(".vs", 1, MarkerMatchMode.Path),
        new(".idea", 1, MarkerMatchMode.Path),
        new("*.sln", 2, MarkerMatchMode.SearchPattern),
        new("*.slnx", 2, MarkerMatchMode.SearchPattern)
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
        var detectedRoot = FindDirectoryWithMarker(fullPath);

        if (detectedRoot is not null)
        {
            log.LogMessage(MessageImportance.Low, $"RepoContentCopy: using repository root '{detectedRoot}'.");
        }

        return detectedRoot;
    }

    private static string? FindDirectoryWithMarker(string startDirectory)
    {
        string? bestMatch = null;
        var bestPriority = int.MaxValue;

        for (var current = new DirectoryInfo(startDirectory); current is not null; current = current.Parent)
        {
            try
            {
                foreach (var marker in Markers)
                {
                    if (!marker.IsMatch(current.FullName))
                    {
                        continue;
                    }

                    if (marker.Priority >= bestPriority)
                    {
                        continue;
                    }

                    bestMatch = current.FullName;
                    bestPriority = marker.Priority;

                    if (bestPriority == 0)
                    {
                        return bestMatch;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Ignore inaccessible directories and keep walking up.
            }
        }

        return bestMatch;
    }

    private enum MarkerMatchMode
    {
        Path,
        SearchPattern
    }

    private sealed class MarkerProbe(string value, int priority, MarkerMatchMode mode)
    {
        public string Value { get; } = value;
        public int Priority { get; } = priority;
        public MarkerMatchMode Mode { get; } = mode;

        public bool IsMatch(string directoryPath)
            => Mode == MarkerMatchMode.Path
                ? Directory.Exists(Path.Combine(directoryPath, Value)) || File.Exists(Path.Combine(directoryPath, Value))
                : Directory.EnumerateFiles(directoryPath, Value, SearchOption.TopDirectoryOnly).Any();
    }
}
