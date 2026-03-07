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
    public async Task Execute_Skips_Tag_When_Parent_CopyOnBuild_Is_False_And_Consumer_Does_Not_Override()
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
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"), ("CopyOnBuild", "false"))
        ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(Path.Combine(repoRoot, "docs", "README.md"))).IsFalse();
    }

    [Test]
    public async Task Execute_Copies_Tag_When_Parent_CopyOnBuild_Is_False_And_Consumer_Overrides_To_True()
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
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"), ("CopyOnBuild", "false"))
        ],
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("CopyOnBuild", "true"))
        ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(repoRoot, "docs", "README.md"))).IsEqualTo("payload");
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
    public async Task Execute_Warns_And_Defaults_To_True_When_Parent_CopyOnBuild_Is_Invalid()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

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
                TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"), ("CopyOnBuild", "maybe"))
            ],
            PayloadPolicies = []
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(repoRoot, "docs", "README.md"))).IsEqualTo("payload");
        await Assert.That(engine.Warnings.Any(x => (x.Message ?? string.Empty).Contains("unsupported CopyOnBuild value", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Execute_Warns_And_Uses_Parent_Default_When_Consumer_CopyOnBuild_Is_Invalid()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

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
                TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"), ("CopyOnBuild", "false"))
            ],
            PayloadPolicies =
            [
                TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("CopyOnBuild", "maybe"))
            ]
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(Path.Combine(repoRoot, "docs", "README.md"))).IsFalse();
        await Assert.That(engine.Warnings.Any(x => (x.Message ?? string.Empty).Contains("unsupported CopyOnBuild value", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Execute_Removes_File_When_PayloadRemove_Is_Enabled()
    {
        using var temp = new TemporaryDirectory();
        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);
        var obsoleteFile = Path.Combine(repoRoot, ".agents", "skills", "example-skill", "obsolete.md");
        Directory.CreateDirectory(Path.GetDirectoryName(obsoleteFile)!);
        await File.WriteAllTextAsync(obsoleteFile, "remove me");

        var task = CreateTask(
            projectDirectory,
            repoRoot,
            [],
            payloadRemoveItems:
            [
                TestTaskItem.Create(".agents/skills/example-skill/obsolete.md", ("PackageId", "ParentPackage"), ("Tag", "ExampleSkill"))
            ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(obsoleteFile)).IsFalse();
    }

    [Test]
    public async Task Execute_Does_Not_Remove_File_When_Consumer_CopyOnBuild_Is_False()
    {
        using var temp = new TemporaryDirectory();
        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);
        var obsoleteFile = Path.Combine(repoRoot, ".agents", "skills", "example-skill", "obsolete.md");
        Directory.CreateDirectory(Path.GetDirectoryName(obsoleteFile)!);
        await File.WriteAllTextAsync(obsoleteFile, "keep me");

        var task = CreateTask(
            projectDirectory,
            repoRoot,
            [],
            [
                TestTaskItem.Create("ParentPackage", ("Tag", "ExampleSkill"), ("CopyOnBuild", "false"))
            ],
            [
                TestTaskItem.Create(".agents/skills/example-skill/obsolete.md", ("PackageId", "ParentPackage"), ("Tag", "ExampleSkill"))
            ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(obsoleteFile)).IsTrue();
    }

    [Test]
    public async Task Execute_Does_Not_Remove_File_When_PayloadRemove_CopyOnBuild_Is_False_And_Consumer_Does_Not_Override()
    {
        using var temp = new TemporaryDirectory();
        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);
        var obsoleteFile = Path.Combine(repoRoot, ".agents", "skills", "example-skill", "obsolete.md");
        Directory.CreateDirectory(Path.GetDirectoryName(obsoleteFile)!);
        await File.WriteAllTextAsync(obsoleteFile, "keep me");

        var task = CreateTask(
            projectDirectory,
            repoRoot,
            [],
            payloadRemoveItems:
            [
                TestTaskItem.Create(".agents/skills/example-skill/obsolete.md", ("PackageId", "ParentPackage"), ("Tag", "ExampleSkill"), ("CopyOnBuild", "false"))
            ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(obsoleteFile)).IsTrue();
    }

    [Test]
    public async Task Execute_Removes_File_When_PayloadRemove_CopyOnBuild_Is_False_And_Consumer_Overrides_To_True()
    {
        using var temp = new TemporaryDirectory();
        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);
        var obsoleteFile = Path.Combine(repoRoot, ".agents", "skills", "example-skill", "obsolete.md");
        Directory.CreateDirectory(Path.GetDirectoryName(obsoleteFile)!);
        await File.WriteAllTextAsync(obsoleteFile, "remove me");

        var task = CreateTask(
            projectDirectory,
            repoRoot,
            [],
            [
                TestTaskItem.Create("ParentPackage", ("Tag", "ExampleSkill"), ("CopyOnBuild", "true"))
            ],
            [
                TestTaskItem.Create(".agents/skills/example-skill/obsolete.md", ("PackageId", "ParentPackage"), ("Tag", "ExampleSkill"), ("CopyOnBuild", "false"))
            ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(obsoleteFile)).IsFalse();
    }

    [Test]
    public async Task Execute_Warns_And_Defaults_To_True_When_Parent_CopyOnBuild_Conflicts_Across_Tag()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);
        var obsoleteFile = Path.Combine(repoRoot, ".agents", "skills", "example-skill", "obsolete.md");
        Directory.CreateDirectory(Path.GetDirectoryName(obsoleteFile)!);
        await File.WriteAllTextAsync(obsoleteFile, "remove me");

        var engine = new RecordingBuildEngine();
        var task = new CopyRepoContentTask
        {
            BuildEngine = engine,
            ProjectDirectory = projectDirectory,
            RootDirectory = repoRoot,
            PayloadContentItems =
            [
                TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "ExampleSkill"), ("TargetPath", "docs/README.md"), ("CopyOnBuild", "false"))
            ],
            PayloadRemoveItems =
            [
                TestTaskItem.Create(".agents/skills/example-skill/obsolete.md", ("PackageId", "ParentPackage"), ("Tag", "ExampleSkill"), ("CopyOnBuild", "true"))
            ],
            PayloadPolicies = []
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(obsoleteFile)).IsFalse();
        await Assert.That(engine.Warnings.Any(x => (x.Message ?? string.Empty).Contains("conflicting CopyOnBuild values", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    [Test]
    public async Task Execute_Warns_And_Skips_Directory_Remove_Target()
    {
        using var temp = new TemporaryDirectory();
        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);
        var obsoleteDirectory = Path.Combine(repoRoot, ".agents", "skills", "example-skill");
        Directory.CreateDirectory(obsoleteDirectory);
        await File.WriteAllTextAsync(Path.Combine(obsoleteDirectory, "SKILL.md"), "keep");

        var engine = new RecordingBuildEngine();
        var task = new CopyRepoContentTask
        {
            BuildEngine = engine,
            ProjectDirectory = projectDirectory,
            RootDirectory = repoRoot,
            PayloadContentItems = [],
            PayloadPolicies = [],
            PayloadRemoveItems =
            [
                TestTaskItem.Create(".agents/skills/example-skill", ("PackageId", "ParentPackage"), ("Tag", "ExampleSkill"))
            ]
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(Directory.Exists(obsoleteDirectory)).IsTrue();
        await Assert.That(engine.Warnings.Any(x => (x.Message ?? string.Empty).Contains("Remove it manually", StringComparison.OrdinalIgnoreCase))).IsTrue();
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
    public async Task Execute_Copies_To_Absolute_OverridePath()
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
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
        ],
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("OverridePath", absoluteRoot))
        ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(absoluteTarget)).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(absoluteTarget)).IsEqualTo("payload");
    }

    [Test]
    public async Task Execute_Copies_To_Relative_OverridePath_From_Project_Directory()
    {
        using var temp = new TemporaryDirectory();
        var sourceFile = Path.Combine(temp.Path, "package", "README.md");
        Directory.CreateDirectory(Path.GetDirectoryName(sourceFile)!);
        await File.WriteAllTextAsync(sourceFile, "payload");

        var repoRoot = Path.Combine(temp.Path, "repo");
        Directory.CreateDirectory(repoRoot);
        var projectDirectory = Path.Combine(repoRoot, "src", "Consumer");
        Directory.CreateDirectory(projectDirectory);
        var overrideRoot = Path.Combine(projectDirectory, "custom-root");

        var task = CreateTask(projectDirectory, repoRoot,
        [
            TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", "docs/README.md"))
        ],
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("OverridePath", "custom-root"))
        ]);

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(Path.Combine(overrideRoot, "docs", "README.md"))).IsTrue();
        await Assert.That(await File.ReadAllTextAsync(Path.Combine(overrideRoot, "docs", "README.md"))).IsEqualTo("payload");
    }

    [Test]
    public async Task Execute_Skips_Rooted_TargetPath_Even_When_OverridePath_Is_Provided()
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
                TestTaskItem.Create(sourceFile, ("PackageId", "ParentPackage"), ("Tag", "Docs"), ("TargetPath", Path.Combine(temp.Path, "absolute", "docs", "README.md")))
            ],
            PayloadPolicies =
            [
                TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("OverridePath", Path.Combine(temp.Path, "custom-root")))
            ]
        };

        await Assert.That(task.Execute()).IsTrue();
        await Assert.That(File.Exists(Path.Combine(temp.Path, "absolute", "docs", "README.md"))).IsFalse();
        await Assert.That(engine.Warnings.Any(x => (x.Message ?? string.Empty).Contains("TargetPath must always be relative", StringComparison.OrdinalIgnoreCase))).IsTrue();
    }

    private static CopyRepoContentTask CreateTask(
        string projectDirectory,
        string rootDirectory,
        Microsoft.Build.Framework.ITaskItem[] payloadContentItems,
        Microsoft.Build.Framework.ITaskItem[]? payloadPolicies = null,
        Microsoft.Build.Framework.ITaskItem[]? payloadRemoveItems = null)
        => new()
        {
            BuildEngine = new RecordingBuildEngine(),
            ProjectDirectory = projectDirectory,
            RootDirectory = rootDirectory,
            PayloadContentItems = payloadContentItems,
            PayloadPolicies = payloadPolicies ?? [],
            PayloadRemoveItems = payloadRemoveItems ?? []
        };
}
