using System.IO.Compression;
using Payload.Tests.TestSupport;

namespace Payload.Tests;

public class SampleBuildIntegrationTests
{
    [Test]
    public async Task Sample_Flow_Packs_And_Copies_Content_End_To_End()
    {
        using var workspace = new TestWorkspace();
        var env = CreateEnvironment(workspace);

        var payloadPack = await DotnetCommand.RunAsync(["pack", "src/Payload/Payload.csproj", "-nologo", "-c", "Debug"], workspace.RootPath, env);
        AssertSucceeded(payloadPack);

        var parentBuild = await DotnetCommand.RunAsync(["build", "tests/ParentPackage/ParentPackage.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env);
        AssertSucceeded(parentBuild);

        var nupkgPath = Path.Combine(workspace.RootPath, "tests", "ParentPackage", "bin", "Debug", "ParentPackage.0.1.0-alpha.nupkg");
        await Assert.That(File.Exists(nupkgPath)).IsTrue();

        using (var package = ZipFile.OpenRead(nupkgPath))
        {
            await Verify(package.Entries
                .Select(x => NormalizePackageEntry(x.FullName))
                .OrderBy(x => x)
                .ToArray());
        }

        var consumerBuild = await DotnetCommand.RunAsync(["build", "tests/ConsumerApp/ConsumerApp.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env);
        AssertSucceeded(consumerBuild);

        var copiedSkillPath = Path.Combine(workspace.RootPath, ".agents", "skills", "example-skill", "SKILL.md");
        await Assert.That(File.Exists(copiedSkillPath)).IsTrue();
    }

    [Test]
    public async Task Sample_Flow_Respects_Disable_Policy()
    {
        using var workspace = new TestWorkspace();
        var consumerProjectPath = Path.Combine(workspace.RootPath, "tests", "ConsumerApp", "ConsumerApp.csproj");
        var consumerProject = await File.ReadAllTextAsync(consumerProjectPath);
        consumerProject = consumerProject.Replace("CopyOnBuild=\"true\"", "CopyOnBuild=\"false\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(consumerProjectPath, consumerProject);

        var env = CreateEnvironment(workspace);

        AssertSucceeded(await DotnetCommand.RunAsync(["pack", "src/Payload/Payload.csproj", "-nologo", "-c", "Debug"], workspace.RootPath, env));
        AssertSucceeded(await DotnetCommand.RunAsync(["build", "tests/ParentPackage/ParentPackage.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env));

        var consumerBuild = await DotnetCommand.RunAsync(["build", "tests/ConsumerApp/ConsumerApp.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env);
        AssertSucceeded(consumerBuild);

        var copiedSkillPath = Path.Combine(workspace.RootPath, ".agents", "skills", "example-skill", "SKILL.md");
        await Assert.That(File.Exists(copiedSkillPath)).IsFalse();
    }

    [Test]
    public async Task Sample_Flow_Does_Not_Restore_Deleted_Content_When_CopyOnBuild_Is_False()
    {
        using var workspace = new TestWorkspace();
        var env = CreateEnvironment(workspace);

        AssertSucceeded(await DotnetCommand.RunAsync(["pack", "src/Payload/Payload.csproj", "-nologo", "-c", "Debug"], workspace.RootPath, env));
        AssertSucceeded(await DotnetCommand.RunAsync(["build", "tests/ParentPackage/ParentPackage.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env));
        AssertSucceeded(await DotnetCommand.RunAsync(["build", "tests/ConsumerApp/ConsumerApp.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env));

        var copiedSkillDirectory = Path.Combine(workspace.RootPath, ".agents", "skills", "example-skill");
        await Assert.That(Directory.Exists(copiedSkillDirectory)).IsTrue();

        var consumerProjectPath = Path.Combine(workspace.RootPath, "tests", "ConsumerApp", "ConsumerApp.csproj");
        var consumerProject = await File.ReadAllTextAsync(consumerProjectPath);
        consumerProject = consumerProject.Replace("CopyOnBuild=\"true\"", "CopyOnBuild=\"false\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(consumerProjectPath, consumerProject);

        Directory.Delete(copiedSkillDirectory, recursive: true);
        await Assert.That(Directory.Exists(copiedSkillDirectory)).IsFalse();

        AssertSucceeded(await DotnetCommand.RunAsync(["build", "tests/ConsumerApp/ConsumerApp.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env));

        await Assert.That(Directory.Exists(copiedSkillDirectory)).IsFalse();
    }

    private static IReadOnlyDictionary<string, string> CreateEnvironment(TestWorkspace workspace)
        => new Dictionary<string, string>
        {
            ["NUGET_PACKAGES"] = workspace.NuGetPackagesPath,
            ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        };

    private static void AssertSucceeded(CommandResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.CombinedOutput);
        }
    }

    private static string NormalizePackageEntry(string entry)
        => entry.StartsWith("package/services/metadata/core-properties/", StringComparison.Ordinal)
            ? "package/services/metadata/core-properties/<generated>.psmdcp"
            : entry;
}
