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
    public ITaskItem[] PayloadContentItems { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Gets or sets consumer-defined policies scoped by package id and tag.
    /// </summary>
    public ITaskItem[] PayloadPolicies { get; set; } = Array.Empty<ITaskItem>();

    /// <summary>
    /// Executes the copy operation for each enabled payload item.
    /// </summary>
    public override bool Execute()
    {
        try
        {
            var policies = PolicyMap.Create(PayloadPolicies);
            string? repoRoot = null;
            var repoRootAttempted = false;

            foreach (var item in PayloadContentItems)
            {
                var packageId = item.GetMetadata("PackageId");
                var tag = item.GetMetadata("Tag");
                var targetPath = item.GetMetadata("TargetPath");

                if (string.IsNullOrWhiteSpace(packageId))
                {
                    Log.LogWarning($"RepoContentCopy: PayloadContent item '{item.ItemSpec}' is missing PackageId metadata. Skipping.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(tag))
                {
                    Log.LogWarning($"RepoContentCopy: PayloadContent item '{item.ItemSpec}' is missing Tag metadata. Skipping.");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    Log.LogWarning($"RepoContentCopy: PayloadContent item '{item.ItemSpec}' is missing TargetPath metadata. Skipping.");
                    continue;
                }

                if (!policies.ShouldCopyOnBuild(packageId, tag))
                {
                    Log.LogMessage(MessageImportance.Low, $"RepoContentCopy: '{packageId}' tag '{tag}' has CopyOnBuild='false'. Skipping.");
                    continue;
                }

                if (!TryResolveDestinationRoot(packageId, tag, targetPath, policies, ref repoRoot, ref repoRootAttempted, out var destinationRoot))
                {
                    continue;
                }

                var sourcePath = item.ItemSpec;

                if (File.Exists(sourcePath))
                {
                    CopySingleFile(sourcePath, destinationRoot);
                    continue;
                }

                if (Directory.Exists(sourcePath))
                {
                    CopyDirectory(sourcePath, destinationRoot);
                    continue;
                }

                Log.LogWarning($"RepoContentCopy: source '{sourcePath}' does not exist. Skipping.");
            }

            return !Log.HasLoggedErrors;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }
    }

    private bool TryResolveDestinationRoot(
        string packageId,
        string tag,
        string targetPath,
        PolicyMap policies,
        ref string? repoRoot,
        ref bool repoRootAttempted,
        out string destinationRoot)
    {
        destinationRoot = string.Empty;

        policies.TryGetPathKind(packageId, tag, out var configuredPathKind, out var rawPathKind);

        if (!TryResolvePathKind(packageId, tag, configuredPathKind, rawPathKind, out var pathKind))
        {
            return false;
        }

        if (pathKind == PathKind.Absolute)
        {
            if (!Path.IsPathRooted(targetPath))
            {
                Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' uses PathKind='{PathKind.Absolute}' but TargetPath '{targetPath}' is not rooted. Skipping.");
                return false;
            }

            destinationRoot = Path.GetFullPath(targetPath);
            return true;
        }

        if (Path.IsPathRooted(targetPath))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has rooted TargetPath '{targetPath}' but PathKind is '{PathKind.Relative}'. Set PayloadPolicy PathKind=\"{PathKind.Absolute}\" to allow absolute destinations. Skipping.");
            return false;
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

        destinationRoot = Path.GetFullPath(Path.Combine(repoRoot, targetPath));
        return true;
    }

    private bool TryResolvePathKind(string packageId, string tag, PathKind? configuredPathKind, string? rawPathKind, out PathKind resolvedPathKind)
    {
        resolvedPathKind = PathKind.Relative;

        if (configuredPathKind.HasValue)
        {
            resolvedPathKind = configuredPathKind.Value;
            return true;
        }

        if (string.IsNullOrWhiteSpace(rawPathKind))
        {
            return true;
        }

        Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has unsupported PathKind '{rawPathKind}'. Supported values are '{PathKind.Relative}' and '{PathKind.Absolute}'. Skipping.");
        return false;
    }

    private void CopySingleFile(string sourceFilePath, string destinationPath)
    {
        var destinationFilePath = Directory.Exists(destinationPath)
            ? Path.Combine(destinationPath, Path.GetFileName(sourceFilePath))
            : destinationPath;

        EnsureParentDirectory(destinationFilePath);

        if (!ShouldCopy(sourceFilePath, destinationFilePath))
        {
            Log.LogMessage(MessageImportance.Low, $"RepoContentCopy: '{destinationFilePath}' is up to date.");
            return;
        }

        File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
        Log.LogMessage(MessageImportance.Normal, $"RepoContentCopy: copied '{sourceFilePath}' -> '{destinationFilePath}'.");
    }

    private void CopyDirectory(string sourceDirectoryPath, string destinationDirectoryPath)
    {
        foreach (var sourceFilePath in Directory.EnumerateFiles(sourceDirectoryPath, "*", SearchOption.AllDirectories))
        {
            var relativePath = Polyfill.GetRelativePath(sourceDirectoryPath, sourceFilePath);
            var destinationFilePath = Path.Combine(destinationDirectoryPath, relativePath);

            EnsureParentDirectory(destinationFilePath);

            if (!ShouldCopy(sourceFilePath, destinationFilePath))
            {
                Log.LogMessage(MessageImportance.Low, $"RepoContentCopy: '{destinationFilePath}' is up to date.");
                continue;
            }

            File.Copy(sourceFilePath, destinationFilePath, overwrite: true);
            Log.LogMessage(MessageImportance.Normal, $"RepoContentCopy: copied '{sourceFilePath}' -> '{destinationFilePath}'.");
        }
    }

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
}
