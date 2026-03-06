using System.Globalization;
using System.Security;
using System.Text;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Polyfills;

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
            if (PayloadContentItems.Length == 0)
            {
                Log.LogMessage(MessageImportance.Low, "Payload: no authored PayloadContent items found for packing.");
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
                var item = PayloadContentItems[index];
                var tag = item.GetMetadata("Tag");
                var targetPath = item.GetMetadata("TargetPath");
                var copyOnBuild = item.GetMetadata("CopyOnBuild");
                var sourcePath = GetSourcePath(item);

                if (string.IsNullOrWhiteSpace(tag))
                {
                    Log.LogError($"Payload: authored PayloadContent item '{item.ItemSpec}' is missing Tag metadata.");
                    hasErrors = true;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(targetPath))
                {
                    Log.LogError($"Payload: authored PayloadContent item '{item.ItemSpec}' is missing TargetPath metadata.");
                    hasErrors = true;
                    continue;
                }

                if (File.Exists(sourcePath))
                {
                    var relativePath = NormalizePath(Path.Combine(index.ToString("D4", CultureInfo.InvariantCulture), Path.GetFileName(sourcePath)));
                    packFiles.Add(CreatePackFile(sourcePath, relativePath));
                    generatedEntries.Add(new GeneratedEntry(relativePath, tag, targetPath, copyOnBuild));
                    continue;
                }

                if (Directory.Exists(sourcePath))
                {
                    var relativeRoot = index.ToString("D4", CultureInfo.InvariantCulture);
                    foreach (var filePath in Directory.EnumerateFiles(sourcePath, "*", SearchOption.AllDirectories))
                    {
                        var childRelativePath = NormalizePath(Path.Combine(relativeRoot, Polyfill.GetRelativePath(sourcePath, filePath)));
                        packFiles.Add(CreatePackFile(filePath, childRelativePath));
                    }

                    generatedEntries.Add(new GeneratedEntry(NormalizePath(relativeRoot), tag, targetPath, copyOnBuild));
                    continue;
                }

                Log.LogError($"Payload: authored PayloadContent source '{item.ItemSpec}' does not exist.");
                hasErrors = true;
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
                $"Payload: {(wroteTargetsFile ? "generated" : "reused")} '{outputFile}' with {generatedEntries.Count} PayloadContent item(s) and {PackFiles.Length} packaged file(s).");

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
            builder.AppendLine($"    <PayloadContent Include=\"$({rootProperty}){Escape(entry.PackageRelativePath)}\">");
            builder.AppendLine($"      <PackageId>{Escape(packageId)}</PackageId>");
            builder.AppendLine($"      <Tag>{Escape(entry.Tag)}</Tag>");
            builder.AppendLine($"      <TargetPath>{Escape(entry.TargetPath)}</TargetPath>");
            if (!string.IsNullOrWhiteSpace(entry.CopyOnBuild))
            {
                builder.AppendLine($"      <CopyOnBuild>{Escape(entry.CopyOnBuild!)}</CopyOnBuild>");
            }
            builder.AppendLine("    </PayloadContent>");
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

    private sealed record GeneratedEntry(string PackageRelativePath, string Tag, string TargetPath, string? CopyOnBuild);
}
