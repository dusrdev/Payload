using Payload.Tasks;
using Payload.Tests.TestSupport;

namespace Payload.Tests;

public class GeneratePayloadPackageAssetsTaskTests
{
    [Test]
    public async Task Execute_Generates_Targets_And_Pack_Files_For_Directory_Content_And_Removals()
    {
        using var temp = new TemporaryDirectory();
        var contentRoot = Path.Combine(temp.Path, "content", "skills", "example-skill");
        Directory.CreateDirectory(contentRoot);
        await File.WriteAllTextAsync(Path.Combine(contentRoot, "SKILL.md"), "# skill");
        await File.WriteAllTextAsync(Path.Combine(contentRoot, "notes.txt"), "notes");

        var engine = new RecordingBuildEngine();
        var task = new GeneratePayloadPackageAssetsTask
        {
            BuildEngine = engine,
            PackageId = "ParentPackage",
            OutputPath = Path.Combine(temp.Path, "obj"),
            PayloadContentItems =
            [
                TestTaskItem.Create(contentRoot, ("Tag", "ExampleSkill"), ("TargetPath", ".agents/skills/example-skill"))
            ],
            PayloadRemoveItems =
            [
                TestTaskItem.Create(".agents/skills/example-skill/obsolete.md", ("Tag", "ExampleSkill"))
            ]
        };

        var result = task.Execute();

        await Assert.That(result).IsTrue();
        await Assert.That(File.Exists(task.GeneratedTargetsFile)).IsTrue();
        await Assert.That(task.PackFiles).Count().IsEqualTo(2);

        await Verify(new
        {
            PackagePaths = task.PackFiles
                .Select(x => x.GetMetadata("PackagePath").Replace('\\', '/'))
                .OrderBy(x => x)
                .ToArray(),
            GeneratedTargets = await File.ReadAllTextAsync(task.GeneratedTargetsFile)
        });

        await Assert.That(engine.Errors).Count().IsEqualTo(0);
    }

    [Test]
    public async Task Execute_Preserves_Parent_CopyOnBuild_Metadata_In_Generated_Targets()
    {
        using var temp = new TemporaryDirectory();
        var filePath = Path.Combine(temp.Path, "content", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "hello");

        var engine = new RecordingBuildEngine();
        var task = new GeneratePayloadPackageAssetsTask
        {
            BuildEngine = engine,
            PackageId = "ParentPackage",
            OutputPath = Path.Combine(temp.Path, "obj"),
            PayloadContentItems =
            [
                TestTaskItem.Create(filePath, ("Tag", "OptionalDocs"), ("TargetPath", "docs/README.md"), ("CopyOnBuild", "false"))
            ]
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(task.GeneratedTargetsFile)).Contains("<CopyOnBuild>false</CopyOnBuild>");
    }

    [Test]
    public async Task Execute_Preserves_PayloadRemove_Items_In_Generated_Targets()
    {
        using var temp = new TemporaryDirectory();
        var filePath = Path.Combine(temp.Path, "content", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "hello");

        var task = new GeneratePayloadPackageAssetsTask
        {
            BuildEngine = new RecordingBuildEngine(),
            PackageId = "ParentPackage",
            OutputPath = Path.Combine(temp.Path, "obj"),
            PayloadContentItems =
            [
                TestTaskItem.Create(filePath, ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
            ],
            PayloadRemoveItems =
            [
                TestTaskItem.Create("docs/OLD.md", ("Tag", "Docs"))
            ]
        };

        await Assert.That(task.Execute()).IsTrue();
        var targetsContent = await File.ReadAllTextAsync(task.GeneratedTargetsFile);
        await Assert.That(targetsContent).Contains("<PayloadRemove Include=\"docs/OLD.md\">");
        await Assert.That(targetsContent).Contains("<Tag>Docs</Tag>");
    }

    [Test]
    public async Task Execute_Reuses_Existing_Targets_File_When_Content_Does_Not_Change()
    {
        using var temp = new TemporaryDirectory();
        var filePath = Path.Combine(temp.Path, "content", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "hello");

        var outputPath = Path.Combine(temp.Path, "obj");

        var firstEngine = new RecordingBuildEngine();
        var firstTask = CreateTask(filePath, outputPath, firstEngine);
        await Assert.That(firstTask.Execute()).IsTrue();
        var firstWriteTime = File.GetLastWriteTimeUtc(firstTask.GeneratedTargetsFile);

        await Task.Delay(1200);

        var secondEngine = new RecordingBuildEngine();
        var secondTask = CreateTask(filePath, outputPath, secondEngine);
        await Assert.That(secondTask.Execute()).IsTrue();
        var secondWriteTime = File.GetLastWriteTimeUtc(secondTask.GeneratedTargetsFile);

        await Assert.That(secondWriteTime).IsEqualTo(firstWriteTime);
        await Assert.That(secondEngine.Messages.Any(x => (x.Message ?? string.Empty).Contains("reused", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Execute_Fails_When_Required_Metadata_Is_Missing()
    {
        using var temp = new TemporaryDirectory();
        var filePath = Path.Combine(temp.Path, "content", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        await File.WriteAllTextAsync(filePath, "hello");

        var engine = new RecordingBuildEngine();
        var task = new GeneratePayloadPackageAssetsTask
        {
            BuildEngine = engine,
            PackageId = "ParentPackage",
            OutputPath = Path.Combine(temp.Path, "obj"),
            PayloadContentItems =
            [
                TestTaskItem.Create(filePath, ("TargetPath", ".agents/skills/example-skill"))
            ]
        };

        var result = task.Execute();

        await Assert.That(result).IsFalse();
        await Assert.That(engine.Errors).Count().IsEqualTo(1);
        await Assert.That(engine.Errors[0].Message).Contains("missing Tag metadata");
    }

    private static GeneratePayloadPackageAssetsTask CreateTask(string sourcePath, string outputPath, RecordingBuildEngine engine)
        => new()
        {
            BuildEngine = engine,
            PackageId = "ParentPackage",
            OutputPath = outputPath,
            PayloadContentItems =
            [
                TestTaskItem.Create(sourcePath, ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
            ]
        };
}
