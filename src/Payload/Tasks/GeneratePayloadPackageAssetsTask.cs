using System.Globalization;
using System.Security;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Payload.Internal;

namespace Payload.Tasks;

/// <summary>
/// Generates the parent package's transitive targets file and packaged payload file list during pack.
/// </summary>
public sealed class GeneratePayloadPackageAssetsTask : Microsoft.Build.Utilities.Task
{
    /// <summary>
    /// Gets or sets the authored <c>PayloadContent</c> items declared by the parent package.
    /// </summary>
    [Required]
    public ITaskItem[] PayloadContentItems { get; set; } = [];

    /// <summary>
    /// Gets or sets the authored <c>PayloadRemove</c> items declared by the parent package.
    /// </summary>
    public ITaskItem[] PayloadRemoveItems { get; set; } = [];

    /// <summary>
    /// Gets or sets the parent package id.
    /// </summary>
    [Required]
    public string PackageId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the intermediate output directory for generated assets.
    /// </summary>
    [Required]
    public string OutputPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the generated targets file path.
    /// </summary>
    [Output]
    public string GeneratedTargetsFile { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the files that should be packed into the parent package.
    /// </summary>
    [Output]
    public ITaskItem[] PackFiles { get; set; } = [];

    /// <summary>
    /// Generates packaged payload assets from authored content items.
    /// </summary>
    public override bool Execute()
    {
        try
        {
            if (PayloadContentItems.Length == 0 && PayloadRemoveItems.Length == 0)
            {
                Log.LogMessage(MessageImportance.Low, "Payload: no authored PayloadContent or PayloadRemove items found for packing.");
                return true;
            }

            if (string.IsNullOrWhiteSpace(PackageId))
            {
                Log.LogError("Payload: PackageId is required to generate package assets.");
                return false;
            }

            Directory.CreateDirectory(OutputPath);

            var generatedEntries = new List<GeneratedEntry>();
            var packFiles = new List<ITaskItem>();
            var hasErrors = false;

            for (var index = 0; index < PayloadContentItems.Length; index++)
            {
                hasErrors |= !TryAddGeneratedContentEntry(index, PayloadContentItems[index], generatedEntries, packFiles);
            }

            foreach (var item in PayloadRemoveItems)
            {
                hasErrors |= !TryAddGeneratedRemoveEntry(item, generatedEntries);
            }

            if (hasErrors)
            {
                return false;
            }

            var outputFile = Path.Combine(OutputPath, $"{PackageId}.targets");
            var outputContent = GenerateTargetsContent(PackageId, generatedEntries);
            var wroteTargetsFile = WriteFileIfChanged(outputFile, outputContent);

            GeneratedTargetsFile = outputFile;
            PackFiles = packFiles.ToArray();

            Log.LogMessage(
                wroteTargetsFile ? MessageImportance.High : MessageImportance.Low,
                $"Payload: {(wroteTargetsFile ? "generated" : "reused")} '{outputFile}' with {generatedEntries.Count} generated item(s) and {PackFiles.Length} packaged file(s).");

            return true;
        }
        catch (Exception ex)
        {
            Log.LogErrorFromException(ex, showStackTrace: true);
            return false;
        }
    }

    private static string GetSourcePath(ITaskItem item)
    {
        var fullPath = item.GetMetadata("FullPath");
        return string.IsNullOrWhiteSpace(fullPath) ? Path.GetFullPath(item.ItemSpec) : Path.GetFullPath(fullPath);
    }

    private bool TryAddGeneratedContentEntry(
        int index,
        ITaskItem item,
        ICollection<GeneratedEntry> generatedEntries,
        List<ITaskItem> packFiles)
    {
        if (!TryGetRequiredMetadata(item, "PayloadContent", "TargetPath", out var tag, out var targetPath))
        {
            return false;
        }

        var copyOnBuild = item.GetMetadata("CopyOnBuild");
        var sourcePath = GetSourcePath(item);

        if (File.Exists(sourcePath))
        {
            var relativePath = NormalizePath(Path.Combine(index.ToString("D4", CultureInfo.InvariantCulture), Path.GetFileName(sourcePath)));
            packFiles.Add(CreatePackFile(sourcePath, relativePath));
            generatedEntries.Add(GeneratedEntry.CreateContent(relativePath, tag, targetPath, copyOnBuild));
            return true;
        }

        if (Directory.Exists(sourcePath))
        {
            var relativeRoot = index.ToString("D4", CultureInfo.InvariantCulture);
            foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
            {
                var childRelativePath = NormalizePath(Path.Combine(relativeRoot, Helper.GetRelativePath(sourcePath, filePath)));
                packFiles.Add(CreatePackFile(filePath, childRelativePath));
            }

            generatedEntries.Add(GeneratedEntry.CreateContent(NormalizePath(relativeRoot), tag, targetPath, copyOnBuild));
            return true;
        }

        Log.LogError($"Payload: authored PayloadContent source '{item.ItemSpec}' does not exist.");
        return false;
    }

    private bool TryAddGeneratedRemoveEntry(ITaskItem item, ICollection<GeneratedEntry> generatedEntries)
    {
        if (!TryGetRequiredMetadata(item, "PayloadRemove", "Include", out var tag, out var removePath))
        {
            return false;
        }

        generatedEntries.Add(GeneratedEntry.CreateRemove(removePath, tag, item.GetMetadata("CopyOnBuild")));
        return true;
    }

    private bool TryGetRequiredMetadata(ITaskItem item, string itemName, string valueName, out string tag, out string value)
    {
        tag = item.GetMetadata("Tag");
        value = valueName == "Include" ? item.ItemSpec : item.GetMetadata(valueName);

        if (string.IsNullOrWhiteSpace(tag))
        {
            Log.LogError($"Payload: authored {itemName} item '{item.ItemSpec}' is missing Tag metadata.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(value))
        {
            Log.LogError($"Payload: authored {itemName} item '{item.ItemSpec}' is missing {valueName}.");
            return false;
        }

        return true;
    }

    private static TaskItem CreatePackFile(string sourcePath, string relativePath)
    {
        var packFile = new TaskItem(sourcePath);
        packFile.SetMetadata("PackagePath", $"payload/{NormalizePath(relativePath, '/')}");
        return packFile;
    }

    private static string GenerateTargetsContent(string packageId, IReadOnlyList<GeneratedEntry> entries)
    {
        var safePackageId = MakeSafePropertyName(packageId);
        var rootProperty = $"_Payload_{safePackageId}_Root";
        var builder = new StringBuilder(2048);

        builder.AppendLine("<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
        builder.AppendLine("  <PropertyGroup>");
        builder.AppendLine($"    <{rootProperty}>$(MSBuildThisFileDirectory)..\\payload\\</{rootProperty}>");
        builder.AppendLine("  </PropertyGroup>");
        builder.AppendLine();
        builder.AppendLine("  <ItemGroup>");

        foreach (var entry in entries)
        {
            builder.AppendLine($"    <{entry.ItemName} Include=\"{Escape(entry.ResolveInclude(rootProperty))}\">");
            builder.AppendLine($"      <PackageId>{Escape(packageId)}</PackageId>");
            builder.AppendLine($"      <Tag>{Escape(entry.Tag)}</Tag>");

            foreach (var metadata in entry.Metadata)
            {
                builder.AppendLine($"      <{metadata.Name}>{Escape(metadata.Value)}</{metadata.Name}>");
            }

            builder.AppendLine($"    </{entry.ItemName}>");
        }

        builder.AppendLine("  </ItemGroup>");
        builder.AppendLine("</Project>");
        return builder.ToString();
    }

    private static string MakeSafePropertyName(string packageId)
    {
        var dest = packageId.ToCharArray();

        for (int i = 0; i < dest.Length; i++)
        {
            if (char.IsLetterOrDigit(dest[i])) continue;
            dest[i] = '_';
        }

        return new string(dest);
    }

    private static string NormalizePath(string value, char separator = '\\')
        => value.Replace(Path.AltDirectorySeparatorChar, separator).Replace(Path.DirectorySeparatorChar, separator);

    private static bool WriteFileIfChanged(string path, string content)
    {
        if (File.Exists(path))
        {
            var existingContent = File.ReadAllText(path);
            if (string.Equals(existingContent, content, StringComparison.Ordinal))
            {
                return false;
            }
        }

        File.WriteAllText(path, content, new UTF8Encoding(false));
        return true;
    }

    private static string Escape(string value)
        => SecurityElement.Escape(value) ?? string.Empty;

    private sealed class GeneratedEntry(string itemName, string tag, string? packageRelativePath, IReadOnlyList<EntryMetadata> metadata)
    {
        public string ItemName { get; } = itemName;
        public string Tag { get; } = tag;
        public string? PackageRelativePath { get; } = packageRelativePath;
        public IReadOnlyList<EntryMetadata> Metadata { get; } = metadata;

        public static GeneratedEntry CreateContent(string packageRelativePath, string tag, string targetPath, string? copyOnBuild)
        {
            var metadata = new List<EntryMetadata>
            {
                new("TargetPath", targetPath)
            };

            if (!string.IsNullOrWhiteSpace(copyOnBuild))
            {
                metadata.Add(new EntryMetadata("CopyOnBuild", copyOnBuild!));
            }

            return new GeneratedEntry("PayloadContent", tag, packageRelativePath, metadata);
        }

        public static GeneratedEntry CreateRemove(string removePath, string tag, string? copyOnBuild)
        {
            var metadata = new List<EntryMetadata>();

            if (!string.IsNullOrWhiteSpace(copyOnBuild))
            {
                metadata.Add(new EntryMetadata("CopyOnBuild", copyOnBuild!));
            }

            return new GeneratedEntry("PayloadRemove", tag, removePath, metadata);
        }

        public string ResolveInclude(string rootProperty)
            => ItemName == "PayloadContent"
                ? $"$({rootProperty}){PackageRelativePath}"
                : PackageRelativePath ?? string.Empty;
    }

    private sealed class EntryMetadata(string name, string value)
    {
        public string Name { get; } = name;
        public string Value { get; } = value;
    }
}
