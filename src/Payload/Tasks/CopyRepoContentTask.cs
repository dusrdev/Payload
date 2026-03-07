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

                if (!TryResolveCopyOnBuild(item, packageId, tag, policies, out var shouldCopyOnBuild))
                {
                    continue;
                }

                if (!shouldCopyOnBuild)
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

        if (Path.IsPathRooted(targetPath))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has absolute TargetPath '{targetPath}'. TargetPath must always be relative. Use PayloadPolicy OverridePath to change the destination base path. Skipping.");
            return false;
        }

        if (policies.TryGetOverridePath(packageId, tag, out var overridePath) && !string.IsNullOrWhiteSpace(overridePath))
        {
            destinationRoot = Path.IsPathRooted(overridePath)
                ? Path.GetFullPath(overridePath)
                : Path.GetFullPath(Path.Combine(ProjectDirectory, overridePath));

            destinationRoot = Path.Combine(destinationRoot, targetPath);
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

        destinationRoot = Path.GetFullPath(Path.Combine(repoRoot, targetPath));
        return true;
    }

    private bool TryResolveCopyOnBuild(ITaskItem item, string packageId, string tag, PolicyMap policies, out bool shouldCopyOnBuild)
    {
        shouldCopyOnBuild = true;

        var parentCopyOnBuildRaw = item.GetMetadata("CopyOnBuild");
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
