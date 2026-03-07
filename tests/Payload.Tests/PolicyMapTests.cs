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

        var found = map.TryGetCopyOnBuild("parentpackage", "exampleskill", out var copyOnBuild, out var rawCopyOnBuild);

        await Assert.That(found).IsTrue();
        await Assert.That(copyOnBuild).IsFalse();
        await Assert.That(rawCopyOnBuild).IsEqualTo("false");
    }

    [Test]
    public async Task Create_Ignores_Items_Missing_Package_Or_Tag()
    {
        var map = PolicyMap.Create(
        [
            TestTaskItem.Create(string.Empty, ("Tag", "Skill"), ("CopyOnBuild", "false")),
            TestTaskItem.Create("ParentPackage", ("CopyOnBuild", "false"))
        ]);

        var found = map.TryGetCopyOnBuild("ParentPackage", "Skill", out var copyOnBuild, out var rawCopyOnBuild);

        await Assert.That(found).IsFalse();
        await Assert.That(copyOnBuild).IsNull();
        await Assert.That(rawCopyOnBuild).IsNull();
    }

    [Test]
    public async Task Create_Preserves_CopyOnBuild_Override_State()
    {
        var map = PolicyMap.Create(
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "SkillA"), ("CopyOnBuild", "true")),
            TestTaskItem.Create("ParentPackage", ("Tag", "SkillB"), ("CopyOnBuild", "invalid"))
        ]);

        await Assert.That(map.TryGetCopyOnBuild("ParentPackage", "SkillA", out var skillACopyOnBuild, out var skillARawCopyOnBuild)).IsTrue();
        await Assert.That(skillACopyOnBuild).IsTrue();
        await Assert.That(skillARawCopyOnBuild).IsEqualTo("true");

        await Assert.That(map.TryGetCopyOnBuild("ParentPackage", "SkillB", out var skillBCopyOnBuild, out var skillBRawCopyOnBuild)).IsTrue();
        await Assert.That(skillBCopyOnBuild).IsNull();
        await Assert.That(skillBRawCopyOnBuild).IsEqualTo("invalid");
    }

    [Test]
    public async Task Create_Returns_OverridePath_For_Matching_Package_And_Tag()
    {
        var map = PolicyMap.Create(
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "Docs"), ("OverridePath", "custom/root"))
        ]);

        var found = map.TryGetOverridePath("parentpackage", "docs", out var overridePath);

        await Assert.That(found).IsTrue();
        await Assert.That(overridePath).IsEqualTo("custom/root");
    }
}
