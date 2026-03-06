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
            TestTaskItem.Create("ParentPackage", ("Tag", "FluentValidationSkill"), ("Disable", "true"))
        ]);

        await Assert.That(map.IsDisabled("parentpackage", "fluentvalidationskill")).IsTrue();
    }

    [Test]
    public async Task Create_Ignores_Items_Missing_Package_Or_Tag()
    {
        var map = PolicyMap.Create(
        [
            TestTaskItem.Create(string.Empty, ("Tag", "Skill"), ("Disable", "true")),
            TestTaskItem.Create("ParentPackage", ("Disable", "true"))
        ]);

        await Assert.That(map.IsDisabled("ParentPackage", "Skill")).IsFalse();
    }

    [Test]
    public async Task Create_Treats_Non_True_Disable_As_Enabled()
    {
        var map = PolicyMap.Create(
        [
            TestTaskItem.Create("ParentPackage", ("Tag", "SkillA"), ("Disable", "false")),
            TestTaskItem.Create("ParentPackage", ("Tag", "SkillB"), ("Disable", "invalid"))
        ]);

        await Assert.That(map.IsDisabled("ParentPackage", "SkillA")).IsFalse();
        await Assert.That(map.IsDisabled("ParentPackage", "SkillB")).IsFalse();
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
