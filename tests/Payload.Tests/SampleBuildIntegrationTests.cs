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

        var payloadBuild = await DotnetCommand.RunAsync(["build", "src/Payload/Payload.csproj", "-nologo"], workspace.RootPath, env);
        AssertSucceeded(payloadBuild);

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

        var copiedSkillPath = Path.Combine(workspace.RootPath, ".agents", "skills", "fluent-validation-expert", "SKILL.md");
        await Assert.That(File.Exists(copiedSkillPath)).IsTrue();
    }

    [Test]
    public async Task Sample_Flow_Respects_Disable_Policy()
    {
        using var workspace = new TestWorkspace();
        var consumerProjectPath = Path.Combine(workspace.RootPath, "tests", "ConsumerApp", "ConsumerApp.csproj");
        var consumerProject = await File.ReadAllTextAsync(consumerProjectPath);
        consumerProject = consumerProject.Replace("Disable=\"false\"", "Disable=\"true\"", StringComparison.Ordinal);
        await File.WriteAllTextAsync(consumerProjectPath, consumerProject);

        var env = CreateEnvironment(workspace);

        AssertSucceeded(await DotnetCommand.RunAsync(["build", "src/Payload/Payload.csproj", "-nologo"], workspace.RootPath, env));
        AssertSucceeded(await DotnetCommand.RunAsync(["build", "tests/ParentPackage/ParentPackage.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env));

        var consumerBuild = await DotnetCommand.RunAsync(["build", "tests/ConsumerApp/ConsumerApp.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env);
        AssertSucceeded(consumerBuild);

        var copiedSkillPath = Path.Combine(workspace.RootPath, ".agents", "skills", "fluent-validation-expert", "SKILL.md");
        await Assert.That(File.Exists(copiedSkillPath)).IsFalse();
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
