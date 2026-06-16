using System.IO.Compression;
using System.Reflection;
using Payload.Tests.TestSupport;

namespace Payload.Tests;

public class SampleBuildIntegrationTests
{
    private const string TestStrongNameKeyBase64 =
        "BwIAAAAkAABSU0EyAAQAAAEAAQArmG9rXqVxfTXHLThWH0E+JnAp76m18Qe5Mx2D32VzgTJbOme3WBL2OpQ2zsy0lJTej1dPjmOdTSbA/Piw"
        + "6aGhlrgLb27QU2KNENAn4DLfLtHWCDXl9H0yye9toQ0DZqMZVzNiyCG1+PpavFuyIkHeb2Zqhdgta6jDCQ0BY2vSu20StxuABWomv4h9ZbW+"
        + "whBzp70hVbuH1AcAK3HDTZmfLEOg1VMcLiTzc0bZFyq/p61FmcdlLagFoQMZV6mTsuf3dUVXtmXU76mM1xyZuadNNB4y4960rOa6r9suJKfUwe"
        + "LHZRz1Owy6zPR1Nqm7je2RvpJmzLBJE9oN9Wilt4XPPSA8yTQw81kk+IJ4fNb9QDndauNKeteUYL8FzX1aIKY9NcJsGDehBUMVMvFGI0I12aUz"
        + "248B9ngSFaivgvfc2GOYv3RNqMp1xApQtJjcL7S3klHTMdleZTYK8zCQh5WoqyX30dQ2BLYk7e/HJDa4FZU1VmcITGDF8qGcpxKp+VhAfFIPdP"
        + "0y3xu/IXrP10xOBONoR2OatKClkmHpCQzzmCFnZe0mUsXOgg/gVFNzKH5wjY0BKUTF17/cdWHIv5pY6U9GpDwrNoz1VZyQ5dj/DDUNH9QnW6Rj"
        + "p1c9RZLFLwwExVwCue+CGh6CEVFTBVVmn16U9OaOkxUFLp5wlOdAsII5rDwxxT9jpPkCGMkjQ3ue6zUOyGUIVNJmf8slILQTv2m8la1XsPhTAB"
        + "jh6Ge5AUK7uEpD8tYpXCDee3qEtQY=";

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
    public async Task Payload_Package_Ships_Only_StrongNamed_TaskLoad_Assemblies()
    {
        using var workspace = new TestWorkspace();
        var env = CreateEnvironment(workspace);
        var strongNameKeyPath = Path.Combine(workspace.RootPath, "Payload.Tests.snk");
        await File.WriteAllBytesAsync(strongNameKeyPath, Convert.FromBase64String(TestStrongNameKeyBase64));

        var payloadPack = await DotnetCommand.RunAsync(
            [
                "pack",
                "src/Payload/Payload.csproj",
                "-nologo",
                "-c",
                "Debug",
                "-p:SignAssembly=true",
                $"-p:AssemblyOriginatorKeyFile={strongNameKeyPath}"
            ],
            workspace.RootPath,
            env);
        AssertSucceeded(payloadPack);

        var nupkgPath = Path.Combine(workspace.RootPath, "src", "Payload", "bin", "Debug", "Payload.1.0.0.nupkg");
        using var package = ZipFile.OpenRead(nupkgPath);
        var taskLoadAssemblyEntries = package.Entries
            .Where(x => x.FullName.StartsWith("build/netstandard2.0/", StringComparison.OrdinalIgnoreCase)
                        && x.FullName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.FullName, StringComparer.Ordinal)
            .ToArray();

        await Assert.That(taskLoadAssemblyEntries.Length > 0).IsTrue();

        var extractionDirectory = Path.Combine(workspace.RootPath, "task-load-assemblies");
        var unsignedAssemblies = new List<string>();

        foreach (var entry in taskLoadAssemblyEntries)
        {
            var assemblyPath = Path.Combine(extractionDirectory, Path.GetFileName(entry.FullName));
            Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
            entry.ExtractToFile(assemblyPath, overwrite: true);

            var publicKeyToken = AssemblyName.GetAssemblyName(assemblyPath).GetPublicKeyToken();
            if (publicKeyToken is null || publicKeyToken.Length == 0)
            {
                unsignedAssemblies.Add(entry.FullName);
            }
        }

        await Assert.That(string.Join(", ", unsignedAssemblies)).IsEqualTo(string.Empty);
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
