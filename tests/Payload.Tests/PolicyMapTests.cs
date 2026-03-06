using Payload.Internal;
using Payload.Tests.TestSupport;

namespace Payload.Tests;

public class PolicyMapTests
{
    [Test]
    public async Task Create_Matches_Package_And_Tag_Case_Insensitively()
    {
        var map = PolicyMap.Create(
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "ExampleSkill"), ("CopyOnBuild", "false"))
        ]);

        await Assert.That(map.ShouldCopyOnBuild("parentpackage", "exampleskill")).IsFalse();
    }

    [Test]
    public async Task Create_Ignores_Items_Missing_Package_Or_Tag()
    {
        var map = PolicyMap.Create(
        [
            TestTaskItem.Create(string.Empty, ("Tag", "Skill"), ("CopyOnBuild", "false")),
            TestTaskItem.Create("ParentPackage", ("CopyOnBuild", "false"))
        ]);

        await Assert.That(map.ShouldCopyOnBuild("ParentPackage", "Skill")).IsTrue();
    }

    [Test]
    public async Task Create_Defaults_CopyOnBuild_To_True_When_Missing_Or_Invalid()
    {
        var map = PolicyMap.Create(
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "SkillA"), ("CopyOnBuild", "true")),
            TestTaskItem.Create("ParentPackage", ("Tag", "SkillB"), ("CopyOnBuild", "invalid"))
        ]);

        await Assert.That(map.ShouldCopyOnBuild("ParentPackage", "SkillA")).IsTrue();
        await Assert.That(map.ShouldCopyOnBuild("ParentPackage", "SkillB")).IsTrue();
    }

    [Test]
    public async Task Create_Returns_PathKind_For_Matching_Package_And_Tag()
    {
        var map = PolicyMap.Create(
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), (nameof(PathKind), nameof(PathKind.Absolute)))
        ]);

        var found = map.TryGetPathKind("parentpackage", "docs", out var pathKind, out var rawPathKind);

        await Assert.That(found).IsTrue();
        await Assert.That(pathKind).IsEqualTo(PathKind.Absolute);
        await Assert.That(rawPathKind).IsEqualTo(nameof(PathKind.Absolute));
    }
}
