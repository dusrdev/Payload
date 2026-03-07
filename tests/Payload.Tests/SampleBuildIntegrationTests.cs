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

        await BuildFixturePackagesAsync(workspace.RootPath, env);

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

        await BuildFixturePackagesAsync(workspace.RootPath, env);

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

        await BuildFixturePackagesAsync(workspace.RootPath, env);
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

    [Test]
    public async Task Sample_Flow_Removes_File_End_To_End()
    {
        using var workspace = new TestWorkspace();
        var parentProjectPath = Path.Combine(workspace.RootPath, "tests", "ParentPackage", "ParentPackage.csproj");
        var parentProject = await File.ReadAllTextAsync(parentProjectPath);
        parentProject = parentProject.Replace(
            "</ItemGroup>\n</Project>",
            "  <PayloadRemove Include=\".agents/skills/example-skill/obsolete.md\">\n    <Tag>ExampleSkill</Tag>\n  </PayloadRemove>\n</ItemGroup>\n</Project>",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(parentProjectPath, parentProject);

        var obsoleteFile = Path.Combine(workspace.RootPath, ".agents", "skills", "example-skill", "obsolete.md");
        Directory.CreateDirectory(Path.GetDirectoryName(obsoleteFile)!);
        await File.WriteAllTextAsync(obsoleteFile, "remove me");

        var env = CreateEnvironment(workspace);
        await BuildFixturePackagesAsync(workspace.RootPath, env);

        AssertSucceeded(await DotnetCommand.RunAsync(["build", "tests/ConsumerApp/ConsumerApp.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env));

        await Assert.That(File.Exists(obsoleteFile)).IsFalse();
        await Assert.That(File.Exists(Path.Combine(workspace.RootPath, ".agents", "skills", "example-skill", "SKILL.md"))).IsTrue();
    }

    [Test]
    public async Task Sample_Flow_Copies_To_OverridePath_End_To_End()
    {
        using var workspace = new TestWorkspace();
        var consumerProjectPath = Path.Combine(workspace.RootPath, "tests", "ConsumerApp", "ConsumerApp.csproj");
        var consumerProject = await File.ReadAllTextAsync(consumerProjectPath);
        consumerProject = consumerProject.Replace(
            "CopyOnBuild=\"true\"",
            "CopyOnBuild=\"true\" OverridePath=\"custom-root\"",
            StringComparison.Ordinal);
        await File.WriteAllTextAsync(consumerProjectPath, consumerProject);

        var env = CreateEnvironment(workspace);
        await BuildFixturePackagesAsync(workspace.RootPath, env);

        AssertSucceeded(await DotnetCommand.RunAsync(["build", "tests/ConsumerApp/ConsumerApp.csproj", "-nologo", "-p:RestoreForce=true"], workspace.RootPath, env));

        var overriddenPath = Path.Combine(workspace.RootPath, "tests", "ConsumerApp", "custom-root", ".agents", "skills", "example-skill", "SKILL.md");
        var defaultPath = Path.Combine(workspace.RootPath, ".agents", "skills", "example-skill", "SKILL.md");

        await Assert.That(File.Exists(overriddenPath)).IsTrue();
        await Assert.That(File.Exists(defaultPath)).IsFalse();
    }

    [Test]
    public async Task File_Based_App_Flow_Copies_Content_End_To_End()
    {
        using var workspace = new TestWorkspace();
        var env = CreateEnvironment(workspace);
        await BuildFixturePackagesAsync(workspace.RootPath, env);

        var appDirectory = Path.Combine(workspace.RootPath, "tests", "FileApp");
        Directory.CreateDirectory(appDirectory);

        await File.WriteAllTextAsync(
            Path.Combine(appDirectory, "NuGet.config"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="payload-local" value="../ParentPackage/bin/Debug" />
                <add key="payload-core" value="../../src/Payload/bin/Debug" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """);

        await File.WriteAllTextAsync(
            Path.Combine(appDirectory, "app.cs"),
            """
            #!/usr/bin/dotnet run
            #:sdk Microsoft.NET.Sdk
            #:package ParentPackage@0.1.0-alpha

            Console.WriteLine("Payload file app");
            """);

        AssertSucceeded(await DotnetCommand.RunAsync(["run", "tests/FileApp/app.cs"], workspace.RootPath, env));

        var copiedSkillPath = Path.Combine(workspace.RootPath, ".agents", "skills", "example-skill", "SKILL.md");
        await Assert.That(File.Exists(copiedSkillPath)).IsTrue();
    }

    private static async Task BuildFixturePackagesAsync(string rootPath, IReadOnlyDictionary<string, string> env)
    {
        var payloadPack = await DotnetCommand.RunAsync(["pack", "src/Payload/Payload.csproj", "-nologo", "-c", "Debug"], rootPath, env);
        AssertSucceeded(payloadPack);

        var parentBuild = await DotnetCommand.RunAsync(["build", "tests/ParentPackage/ParentPackage.csproj", "-nologo", "-p:RestoreForce=true"], rootPath, env);
        AssertSucceeded(parentBuild);
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
