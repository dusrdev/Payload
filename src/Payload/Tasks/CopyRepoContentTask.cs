using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Payload.Internal;
using Polyfills;

namespace Payload.Tasks;

/// <summary>
/// Copies packaged <c>PayloadContent</c> items into the consumer repository during build.
/// </summary>
public sealed class CopyRepoContentTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Gets or sets the current consumer project directory.
    /// </summary>
    [Required]
    public string ProjectDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets an optional explicit repository root override.
    /// </summary>
    public string RootDirectory { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the resolved packaged content items to copy.
    /// </summary>
    [Required]
    public ITaskItem[] PayloadContentItems { get; set; } = [];

    /// <summary>
    /// Gets or sets the resolved packaged remove items to delete.
    /// </summary>
    public ITaskItem[] PayloadRemoveItems { get; set; } = [];

    /// <summary>
    /// Gets or sets consumer-defined policies scoped by package id and tag.
    /// </summary>
    public ITaskItem[] PayloadPolicies { get; set; } = [];

    /// <summary>
    /// Executes the copy and remove operations for each enabled payload tag.
    /// </summary>
    public override bool Execute()
    {
        try
        {
            var policies = PolicyMap.Create(PayloadPolicies);
            var parentCopyOnBuild = BuildParentCopyOnBuildMap(PayloadContentItems);
            string? repoRoot = null;
            var repoRootAttempted = false;

            foreach (var item in PayloadContentItems)
            {
                if (!TryResolveContentItem(item, policies, ref repoRoot, ref repoRootAttempted, out var sourcePath, out var destinationPath))
                {
                    continue;
                }

                if (File.Exists(sourcePath))
                {
                    CopySingleFile(sourcePath, destinationPath);
                    continue;
                }

                if (Directory.Exists(sourcePath))
                {
                    foreach (var _ in CopyDirectory(sourcePath, destinationPath))
                    {
                    }

                    continue;
                }

                Log.LogWarning($"RepoContentCopy: source '{sourcePath}' does not exist. Skipping.");
            }

            foreach (var item in PayloadRemoveItems)
            {
                if (!TryResolveRemoveItem(item, policies, parentCopyOnBuild, ref repoRoot, ref repoRootAttempted, out var packageId, out var tag, out var destinationBasePath, out var destinationPath))
                {
                    continue;
                }

                if (Directory.Exists(destinationPath))
                {
                    Log.LogWarning($"RepoContentCopy: directory '{destinationPath}' is no longer compatible with package '{packageId}' tag '{tag}'. Remove it manually. PayloadRemove only supports files.");
                    continue;
                }

                if (!File.Exists(destinationPath))
                {
                    Log.LogMessage(MessageImportance.Low, $"RepoContentCopy: removal target '{destinationPath}' does not exist.");
                    continue;
                }

                File.Delete(destinationPath);
                Log.LogMessage(MessageImportance.Normal, $"RepoContentCopy: removed '{destinationPath}'.");
                DeleteEmptyParentDirectories(destinationPath, destinationBasePath);
            }

            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }
    }

    private static Dictionary<(string PackageId, string Tag), string?> BuildParentCopyOnBuildMap(IEnumerable<ITaskItem> items)
    {
        var map = new Dictionary<(string PackageId, string Tag), string?>(ParentTagComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var packageId = item.GetMetadata("PackageId");
            var tag = item.GetMetadata("Tag");

            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var copyOnBuild = item.GetMetadata("CopyOnBuild");
            if (!string.IsNullOrWhiteSpace(copyOnBuild))
            {
                map[(packageId, tag)] = copyOnBuild;
            }
        }

        return map;
    }

    private bool TryResolveContentItem(
        ITaskItem item,
        PolicyMap policies,
        ref string? repoRoot,
        ref bool repoRootAttempted,
        out string sourcePath,
        out string destinationPath)
    {
        sourcePath = item.ItemSpec;
        destinationPath = string.Empty;

        if (!TryGetRequiredMetadata(item, "TargetPath", out var packageId, out var tag, out var targetPath))
        {
            return false;
        }

        if (!TryResolveCopyOnBuild(packageId, tag, item.GetMetadata("CopyOnBuild"), policies, out var shouldCopyOnBuild))
        {
            return false;
        }

        if (!shouldCopyOnBuild)
        {
            Log.LogMessage(MessageImportance.Low, $"RepoContentCopy: '{packageId}' tag '{tag}' has CopyOnBuild='false'. Skipping.");
            return false;
        }

        if (!TryResolveDestinationPath(packageId, tag, targetPath, policies, ref repoRoot, ref repoRootAttempted, out destinationPath))
        {
            return false;
        }

        return true;
    }

    private bool TryResolveRemoveItem(
        ITaskItem item,
        PolicyMap policies,
        Dictionary<(string PackageId, string Tag), string?> parentCopyOnBuild,
        ref string? repoRoot,
        ref bool repoRootAttempted,
        out string packageId,
        out string tag,
        out string destinationBasePath,
        out string destinationPath)
    {
        destinationBasePath = string.Empty;
        destinationPath = string.Empty;
        packageId = string.Empty;
        tag = string.Empty;

        var removePath = item.ItemSpec;
        if (string.IsNullOrWhiteSpace(removePath))
        {
            Log.LogWarning("RepoContentCopy: PayloadRemove item is missing Include. Skipping.");
            return false;
        }

        if (!TryGetRequiredMetadata(item, "Include", out packageId, out tag, out _))
        {
            return false;
        }

        parentCopyOnBuild.TryGetValue((packageId, tag), out var parentCopyOnBuildRaw);
        if (!TryResolveCopyOnBuild(packageId, tag, parentCopyOnBuildRaw, policies, out var shouldCopyOnBuild))
        {
            return false;
        }

        if (!shouldCopyOnBuild)
        {
            Log.LogMessage(MessageImportance.Low, $"RepoContentCopy: '{packageId}' tag '{tag}' has CopyOnBuild='false'. Skipping removals.");
            return false;
        }

        if (Path.IsPathRooted(removePath))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has absolute PayloadRemove path '{removePath}'. PayloadRemove paths must always be relative. Skipping.");
            return false;
        }

        if (!TryResolveDestinationBasePath(packageId, tag, removePath, policies, ref repoRoot, ref repoRootAttempted, out destinationBasePath))
        {
            return false;
        }

        destinationPath = Path.Combine(destinationBasePath, removePath);
        return true;
    }

    private bool TryGetRequiredMetadata(ITaskItem item, string valueName, out string packageId, out string tag, out string value)
    {
        packageId = item.GetMetadata("PackageId");
        tag = item.GetMetadata("Tag");
        value = valueName == "Include" ? item.ItemSpec : item.GetMetadata(valueName);

        if (string.IsNullOrWhiteSpace(packageId))
        {
            Log.LogWarning($"RepoContentCopy: item '{item.ItemSpec}' is missing PackageId metadata. Skipping.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(tag))
        {
            Log.LogWarning($"RepoContentCopy: item '{item.ItemSpec}' is missing Tag metadata. Skipping.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' is missing {valueName}. Skipping.");
            return false;
        }

        return true;
    }

    private bool TryResolveDestinationPath(
        string packageId,
        string tag,
        string targetPath,
        PolicyMap policies,
        ref string? repoRoot,
        ref bool repoRootAttempted,
        out string destinationPath)
    {
        destinationPath = string.Empty;

        if (!TryResolveDestinationBasePath(packageId, tag, targetPath, policies, ref repoRoot, ref repoRootAttempted, out var destinationBasePath))
        {
            return false;
        }

        destinationPath = Path.Combine(destinationBasePath, targetPath);
        return true;
    }

    private bool TryResolveDestinationBasePath(
        string packageId,
        string tag,
        string targetPath,
        PolicyMap policies,
        ref string? repoRoot,
        ref bool repoRootAttempted,
        out string destinationBasePath)
    {
        destinationBasePath = string.Empty;

        if (Path.IsPathRooted(targetPath))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has absolute TargetPath '{targetPath}'. TargetPath must always be relative. Use PayloadPolicy OverridePath to change the destination base path. Skipping.");
            return false;
        }

        if (policies.TryGetOverridePath(packageId, tag, out var overridePath) && !string.IsNullOrWhiteSpace(overridePath))
        {
            destinationBasePath = Path.IsPathRooted(overridePath)
                ? Path.GetFullPath(overridePath)
                : Path.GetFullPath(Path.Combine(ProjectDirectory, overridePath));
            return true;
        }

        if (!repoRootAttempted)
        {
            repoRoot = RepoRootDetector.TryResolve(ProjectDirectory, RootDirectory, Log);
            repoRootAttempted = true;

            if (repoRoot is null)
            {
                Log.LogWarning($"RepoContentCopy: could not determine repository root from '{ProjectDirectory}'. Relative payloads will be skipped.");
            }
        }

        if (repoRoot is null)
        {
            return false;
        }

        destinationBasePath = repoRoot;
        return true;
    }

    private bool TryResolveCopyOnBuild(string packageId, string tag, string? parentCopyOnBuildRaw, PolicyMap policies, out bool shouldCopyOnBuild)
    {
        shouldCopyOnBuild = true;
        var hasConsumerPolicy = policies.TryGetCopyOnBuild(packageId, tag, out var consumerCopyOnBuild, out var rawConsumerCopyOnBuild);

        if (hasConsumerPolicy && consumerCopyOnBuild.HasValue)
        {
            shouldCopyOnBuild = consumerCopyOnBuild.Value;
            return true;
        }

        if (hasConsumerPolicy && !string.IsNullOrWhiteSpace(rawConsumerCopyOnBuild))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has unsupported CopyOnBuild value '{rawConsumerCopyOnBuild}' on PayloadPolicy. Supported values are 'true' and 'false'. Ignoring policy value.");
        }

        if (TryParseCopyOnBuild(parentCopyOnBuildRaw, out var parentCopyOnBuild))
        {
            shouldCopyOnBuild = parentCopyOnBuild;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(parentCopyOnBuildRaw))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has unsupported CopyOnBuild value '{parentCopyOnBuildRaw}' on PayloadContent. Supported values are 'true' and 'false'. Defaulting to 'true'.");
        }

        return true;
    }

    private static bool TryParseCopyOnBuild(string? value, out bool copyOnBuild)
    {
        copyOnBuild = false;
        return !string.IsNullOrWhiteSpace(value) && bool.TryParse(value, out copyOnBuild);
    }

    private string CopySingleFile(string sourceFilePath, string destinationPath)
    {
        var destinationFilePath = ResolveSingleFileDestinationPath(sourceFilePath, destinationPath);

        EnsureParentDirectory(destinationFilePath);

        if (!ShouldCopy(sourceFilePath, destinationFilePath))
        {
            Log.LogMessage(MessageImportance.Low, $"RepoContentCopy: '{destinationFilePath}' is up to date.");
            return destinationFilePath;
        }

        File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
        Log.LogMessage(MessageImportance.Normal, $"RepoContentCopy: copied '{sourceFilePath}' -> '{destinationFilePath}'.");
        return destinationFilePath;
    }

    private IEnumerable<string> CopyDirectory(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        foreach (var sourceFilePath in Directory.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Polyfill.GetRelativePath(sourceDirectoryPath, sourceFilePath);
            var destinationFilePath = Path.Combine(destinationDirectoryPath, relativePath);

            EnsureParentDirectory(destinationFilePath);

            if (!ShouldCopy(sourceFilePath, destinationFilePath))
            {
                Log.LogMessage(MessageImportance.Low, $"RepoContentCopy: '{destinationFilePath}' is up to date.");
                yield return destinationFilePath;
                continue;
            }

            File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
            Log.LogMessage(MessageImportance.Normal, $"RepoContentCopy: copied '{sourceFilePath}' -> '{destinationFilePath}'.");
            yield return destinationFilePath;
        }
    }

    private static void DeleteEmptyParentDirectories(string filePath, string stopDirectory)
    {
        var stopInfo = new DirectoryInfo(Path.GetFullPath(stopDirectory));

        for (var directory = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? string.Empty); directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.FullName, stopInfo.FullName, StringComparison.Ordinal))
            {
                break;
            }

            if (!directory.Exists)
            {
                continue;
            }

            if (Directory.EnumerateFileSystemEntries(directory.FullName).Any())
            {
                break;
            }

            directory.Delete();
        }
    }

    private static string ResolveSingleFileDestinationPath(string sourceFilePath, string destinationPath)
        => Directory.Exists(destinationPath)
            ? Path.Combine(destinationPath, Path.GetFileName(sourceFilePath))
            : destinationPath;

    private static void EnsureParentDirectory(string filePath)
    {
        var parentDirectory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(parentDirectory))
        {
            Directory.CreateDirectory(parentDirectory);
        }
    }

    private static bool ShouldCopy(string sourceFilePath, string destinationFilePath)
    {
        if (!File.Exists(destinationFilePath))
        {
            return true;
        }

        var sourceInfo = new FileInfo(sourceFilePath);
        var destinationInfo = new FileInfo(destinationFilePath);

        if (sourceInfo.Length != destinationInfo.Length)
        {
            return true;
        }

        return !HashesEqual(sourceFilePath, destinationFilePath);
    }

    private static bool HashesEqual(string leftPath, string rightPath)
    {
        var leftHash = ComputeSha256(leftPath);
        var rightHash = ComputeSha256(rightPath);
        return Enumerable.SequenceEqual(leftHash, rightHash);
    }

    private static byte[] ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return sha.ComputeHash(stream);
    }

    private sealed class ParentTagComparer : IEqualityComparer<(string PackageId, string Tag)>
    {
        public static readonly ParentTagComparer OrdinalIgnoreCase = new();

        public bool Equals((string PackageId, string Tag) x, (string PackageId, string Tag) y)
            => string.Equals(x.PackageId, y.PackageId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Tag, y.Tag, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string PackageId, string Tag) obj)
            => (StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageId) * 397)
               ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Tag);
    }
}
