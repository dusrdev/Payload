using System.Security.Cryptography;
using Microsoft.Build.Framework;
using Payload.Internal;

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
            var parentCopyOnBuild = BuildParentCopyOnBuildMap(PayloadContentItems, PayloadRemoveItems);
            var executionPlans = BuildExecutionPlans(policies, parentCopyOnBuild);

            foreach (var item in PayloadContentItems)
            {
                if (!TryResolveContentWorkItem(item, executionPlans, out var sourcePath, out var destinationPath))
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
                if (!TryResolveRemoveWorkItem(item, executionPlans, out var packageId, out var tag, out var destinationBasePath, out var destinationPath))
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

    private Dictionary<(string PackageId, string Tag), TagExecutionPlan> BuildExecutionPlans(
        PolicyMap policies,
        IReadOnlyDictionary<(string PackageId, string Tag), ParentCopyOnBuildState> parentCopyOnBuild)
    {
        var plans = new Dictionary<(string PackageId, string Tag), TagExecutionPlan>(ParentTagComparer.OrdinalIgnoreCase);
        string? repoRoot = null;
        var repoRootAttempted = false;

        foreach (var key in EnumerateTagKeys(PayloadContentItems, PayloadRemoveItems))
        {
            plans[key] = CreateExecutionPlan(key.PackageId, key.Tag, policies, parentCopyOnBuild, ref repoRoot, ref repoRootAttempted);
        }

        return plans;
    }

    private static IEnumerable<(string PackageId, string Tag)> EnumerateTagKeys(params ITaskItem[][] itemGroups)
    {
        var seen = new HashSet<(string PackageId, string Tag)>(ParentTagComparer.OrdinalIgnoreCase);

        foreach (var items in itemGroups)
        {
            foreach (var item in items)
            {
                var packageId = item.GetMetadata("PackageId");
                var tag = item.GetMetadata("Tag");

                if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(tag))
                {
                    continue;
                }

                if (seen.Add((packageId, tag)))
                {
                    yield return (packageId, tag);
                }
            }
        }
    }

    private TagExecutionPlan CreateExecutionPlan(
        string packageId,
        string tag,
        PolicyMap policies,
        IReadOnlyDictionary<(string PackageId, string Tag), ParentCopyOnBuildState> parentCopyOnBuild,
        ref string? repoRoot,
        ref bool repoRootAttempted)
    {
        parentCopyOnBuild.TryGetValue((packageId, tag), out var parentState);

        if (!TryResolveCopyOnBuild(packageId, tag, parentState, policies, out var shouldCopyOnBuild))
        {
            return TagExecutionPlan.Blocked(packageId, tag);
        }

        if (!shouldCopyOnBuild)
        {
            Log.LogMessage(MessageImportance.Low, $"RepoContentCopy: '{packageId}' tag '{tag}' has CopyOnBuild='false'. Skipping.");
            return TagExecutionPlan.Disabled(packageId, tag);
        }

        if (!TryResolveDestinationBasePath(packageId, tag, policies, ref repoRoot, ref repoRootAttempted, out var destinationBasePath))
        {
            return TagExecutionPlan.Blocked(packageId, tag);
        }

        return TagExecutionPlan.Enabled(packageId, tag, destinationBasePath);
    }

    private static Dictionary<(string PackageId, string Tag), ParentCopyOnBuildState> BuildParentCopyOnBuildMap(
        IEnumerable<ITaskItem> payloadContentItems,
        IEnumerable<ITaskItem> payloadRemoveItems)
    {
        var map = new Dictionary<(string PackageId, string Tag), ParentCopyOnBuildState>(ParentTagComparer.OrdinalIgnoreCase);

        AddParentCopyOnBuildItems(map, payloadContentItems);
        AddParentCopyOnBuildItems(map, payloadRemoveItems);

        return map;
    }

    private static void AddParentCopyOnBuildItems(
        IDictionary<(string PackageId, string Tag), ParentCopyOnBuildState> map,
        IEnumerable<ITaskItem> items)
    {
        foreach (var item in items)
        {
            var packageId = item.GetMetadata("PackageId");
            var tag = item.GetMetadata("Tag");
            var rawCopyOnBuild = item.GetMetadata("CopyOnBuild");

            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(tag) || string.IsNullOrWhiteSpace(rawCopyOnBuild))
            {
                continue;
            }

            var key = (packageId, tag);
            if (!map.TryGetValue(key, out var state))
            {
                map[key] = new ParentCopyOnBuildState(rawCopyOnBuild, false);
                continue;
            }

            if (!string.Equals(state.RawValue, rawCopyOnBuild, StringComparison.OrdinalIgnoreCase))
            {
                map[key] = new ParentCopyOnBuildState(state.RawValue, true);
            }
        }
    }

    private bool TryResolveContentWorkItem(
        ITaskItem item,
        IReadOnlyDictionary<(string PackageId, string Tag), TagExecutionPlan> executionPlans,
        out string sourcePath,
        out string destinationPath)
    {
        sourcePath = item.ItemSpec;
        destinationPath = string.Empty;

        if (!TryGetRequiredItemValues(item, "TargetPath", out var packageId, out var tag, out var targetPath))
        {
            return false;
        }

        if (Path.IsPathRooted(targetPath))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has absolute TargetPath '{targetPath}'. TargetPath must always be relative. Use PayloadPolicy OverridePath to change the destination base path. Skipping.");
            return false;
        }

        if (!executionPlans.TryGetValue((packageId, tag), out var plan) || !plan.CanExecute)
        {
            return false;
        }

        destinationPath = Path.Combine(plan.DestinationBasePath!, targetPath);
        return true;
    }

    private bool TryResolveRemoveWorkItem(
        ITaskItem item,
        IReadOnlyDictionary<(string PackageId, string Tag), TagExecutionPlan> executionPlans,
        out string packageId,
        out string tag,
        out string destinationBasePath,
        out string destinationPath)
    {
        packageId = string.Empty;
        tag = string.Empty;
        destinationBasePath = string.Empty;
        destinationPath = string.Empty;

        if (!TryGetRequiredItemValues(item, "Include", out packageId, out tag, out var removePath))
        {
            return false;
        }

        if (Path.IsPathRooted(removePath))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has absolute PayloadRemove path '{removePath}'. PayloadRemove paths must always be relative. Skipping.");
            return false;
        }

        if (!executionPlans.TryGetValue((packageId, tag), out var plan) || !plan.CanExecute)
        {
            return false;
        }

        destinationBasePath = plan.DestinationBasePath!;
        destinationPath = Path.Combine(destinationBasePath, removePath);
        return true;
    }

    private bool TryGetRequiredItemValues(ITaskItem item, string valueName, out string packageId, out string tag, out string value)
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

    private bool TryResolveDestinationBasePath(
        string packageId,
        string tag,
        PolicyMap policies,
        ref string? repoRoot,
        ref bool repoRootAttempted,
        out string destinationBasePath)
    {
        destinationBasePath = string.Empty;

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

    private bool TryResolveCopyOnBuild(
        string packageId,
        string tag,
        ParentCopyOnBuildState? parentState,
        PolicyMap policies,
        out bool shouldCopyOnBuild)
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

        if (parentState is not null && parentState.HasConflict)
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has conflicting CopyOnBuild values across parent payload items. Defaulting to 'true'.");
            return true;
        }

        if (TryParseCopyOnBuild(parentState?.RawValue, out var parentCopyOnBuild))
        {
            shouldCopyOnBuild = parentCopyOnBuild;
            return true;
        }

        if (!string.IsNullOrWhiteSpace(parentState?.RawValue))
        {
            Log.LogWarning($"RepoContentCopy: '{packageId}' tag '{tag}' has unsupported CopyOnBuild value '{parentState!.RawValue}' on parent payload items. Supported values are 'true' and 'false'. Defaulting to 'true'.");
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
            var relativePath = Helper.GetRelativePath(sourceDirectoryPath, sourceFilePath);
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

    private static void DeleteEmptyParentDirectories(string filePath, string destinationBasePath)
    {
        var destinationBaseInfo = new DirectoryInfo(Path.GetFullPath(destinationBasePath));

        for (var directory = new DirectoryInfo(Path.GetDirectoryName(filePath) ?? string.Empty); directory is not null; directory = directory.Parent)
        {
            if (string.Equals(directory.FullName, destinationBaseInfo.FullName, StringComparison.Ordinal))
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

    private sealed class ParentCopyOnBuildState(string rawValue, bool hasConflict)
    {
        public string RawValue { get; } = rawValue;
        public bool HasConflict { get; } = hasConflict;
    }

    private sealed class TagExecutionPlan(string packageId, string tag, bool canExecute, string? destinationBasePath)
    {
        public string PackageId { get; } = packageId;
        public string Tag { get; } = tag;
        public bool CanExecute { get; } = canExecute;
        public string? DestinationBasePath { get; } = destinationBasePath;

        public static TagExecutionPlan Enabled(string packageId, string tag, string destinationBasePath)
            => new(packageId, tag, true, destinationBasePath);

        public static TagExecutionPlan Disabled(string packageId, string tag)
            => new(packageId, tag, false, null);

        public static TagExecutionPlan Blocked(string packageId, string tag)
            => new(packageId, tag, false, null);
    }
}
