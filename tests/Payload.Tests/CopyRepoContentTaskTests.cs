using Payload.Tasks;
using Payload.Tests.TestSupport;

namespace Payload.Tests;

public class CopyRepoContentTaskTests
{
    [Test]
    public async Task Execute_Copies_Single_File_To_Resolved_Target_Path()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);

        var task = CreateTask(projectDirectory, repoRoot,
        [
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
        ]);

        var result = task.Execute();
        var destinationFile = Path.Combine(repoRoot, "docs", "README.md");

        await Assert.That(result).IsTrue();
        await Assert.That(File.Exists(destinationFile)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(destinationFile)).IsEqualTo("payload");
    }

    [Test]
    public async Task Execute_Copies_Directory_Recursively()
    {
        using var temp = new TemporaryDirectory();
        var sourceDirectory = Path.Combine(temp.Path, "package", "assets");
        Directory.CreateDirectory(Path.Combine(sourceDirectory, "nested"));
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "a.txt"), "A");
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "nested", "b.txt"), "B");

        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);

        var task = CreateTask(projectDirectory, repoRoot,
        [
            TestTaskItem.Create(sourceDirectory, ("PackageId", "ParentPackage"), ("Tag", "Assets"), ("TargetPath", "assets"))
        ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(repoRoot, "assets", "a.txt"))).IsEqualTo("A");
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(repoRoot, "assets", "nested", "b.txt"))).IsEqualTo("B");
    }

    [Test]
    public async Task Execute_Skips_Tag_When_CopyOnBuild_Is_False()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);

        var task = CreateTask(projectDirectory, repoRoot,
        [
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
        ],
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("CopyOnBuild", "false"))
        ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(Path.Combine(repoRoot, "docs", "README.md"))).IsFalse();
    }

    [Test]
    public async Task Execute_Does_Not_Overwrite_Existing_File_When_CopyOnBuild_Is_False()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

        var repoRoot = Path.Combine(temp.Path, "repo");
        var destinationFile = Path.Combine(repoRoot, "docs", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        await File.WriteAllTextAsync(destinationFile, "consumer-edit");

        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);

        var task = CreateTask(projectDirectory, repoRoot,
        [
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
        ],
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("CopyOnBuild", "false"))
        ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(destinationFile)).IsEqualTo("consumer-edit");
    }

    [Test]
    public async Task Execute_Does_Not_Overwrite_When_Destination_Has_Identical_Content()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

        var repoRoot = Path.Combine(temp.Path, "repo");
        var destinationFile = Path.Combine(repoRoot, "docs", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        await File.WriteAllTextAsync(destinationFile, "payload");

        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);

        var task = CreateTask(projectDirectory, repoRoot,
        [
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
        ]);

        var beforeWriteTime = File.GetLastWriteTimeUtc(destinationFile);
        await Task.Delay(1200);

        await Assert.That(task.Execute()).IsTrue();

        var afterWriteTime = File.GetLastWriteTimeUtc(destinationFile);
        await Assert.That(afterWriteTime).IsEqualTo(beforeWriteTime);
    }

    [Test]
    public async Task Execute_Overwrites_When_Destination_Has_Same_Size_But_Different_Content()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "abc");

        var repoRoot = Path.Combine(temp.Path, "repo");
        var destinationFile = Path.Combine(repoRoot, "docs", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
        await File.WriteAllTextAsync(destinationFile, "xyz");

        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);

        var task = CreateTask(projectDirectory, repoRoot,
        [
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
        ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(destinationFile)).IsEqualTo("abc");
    }

    [Test]
    public async Task Execute_Copies_To_Absolute_Target_Path_When_PathKind_Is_Absolute()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

        var absoluteRoot = Path.Combine(temp.Path, "absolute");
        Directory.CreateDirectory(absoluteRoot);
        var absoluteTarget = Path.Combine(absoluteRoot, "docs", "README.md");

        var projectDirectory = Path.Combine(temp.Path, "isolated", "Consumer");
        Directory.CreateDirectory(projectDirectory);

        var task = CreateTask(projectDirectory, string.Empty,
        [
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", absoluteTarget))
        ],
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("PathKind", "Absolute"))
        ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(absoluteTarget)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(absoluteTarget)).IsEqualTo("payload");
    }

    [Test]
    public async Task Execute_Skips_Rooted_Target_Path_When_PathKind_Is_Not_Absolute()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

        var absoluteTarget = Path.Combine(temp.Path, "absolute", "docs", "README.md");

        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);

        var engine = new RecordingBuildEngine();
        var task = new CopyRepoContentTask
        {
            BuildEngine = engine,
            ProjectDirectory = projectDirectory,
            RootDirectory = repoRoot,
            PayloadContentItems =
            [
                TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", absoluteTarget))
            ],
            PayloadPolicies = []
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(absoluteTarget)).IsFalse();
        await Assert.That(engine.Warnings.Any(x => (x.Message ?? string.Empty).Contains("PathKind", StringComparison.Ordinal))).IsTrue();
    }

    [Test]
    public async Task Execute_Skips_Absolute_PathKind_When_Target_Path_Is_Not_Rooted()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

        var projectDirectory = Path.Combine(temp.Path, "isolated", "Consumer");
        Directory.CreateDirectory(projectDirectory);

        var engine = new RecordingBuildEngine();
        var task = new CopyRepoContentTask
        {
            BuildEngine = engine,
            ProjectDirectory = projectDirectory,
            RootDirectory = string.Empty,
            PayloadContentItems =
            [
                TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
            ],
            PayloadPolicies =
            [
                TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("PathKind", "Absolute"))
            ]
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(Path.Combine(temp.Path, "docs", "README.md"))).IsFalse();
        await Assert.That(engine.Warnings.Any(x => (x.Message ?? string.Empty).Contains("not rooted", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    private static CopyRepoContentTask CreateTask(
        string projectDirectory,
        string rootDirectory,
        Microsoft.Build.Framework.ITaskItem[] payloadContentItems,
        Microsoft.Build.Framework.ITaskItem[]? payloadPolicies = null)
        => new()
        {
            BuildEngine = new RecordingBuildEngine(),
            ProjectDirectory = projectDirectory,
            RootDirectory = rootDirectory,
            PayloadContentItems = payloadContentItems,
            PayloadPolicies = payloadPolicies ?? []
        };
}
