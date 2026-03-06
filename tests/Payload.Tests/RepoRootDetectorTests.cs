using Microsoft.Build.Utilities;
using Payload.Internal;
using Payload.Tests.TestSupport;

namespace Payload.Tests;

public class RepoRootDetectorTests
{
    [Test]
    public async System.Threading.Tasks.Task TryResolve_Uses_Explicit_Root_When_Present()
    {
        using var projectDirectory = new TemporaryDirectory();
        using var explicitRoot = new TemporaryDirectory();
        var logger = CreateLogger();

        var result = RepoRootDetector.TryResolve(projectDirectory.Path, explicitRoot.Path, logger);

        await Assert.That(result).IsEqualTo(Path.GetFullPath(explicitRoot.Path));
    }

    [Test]
    public async System.Threading.Tasks.Task TryResolve_Finds_Git_Root_From_Child_Project_Directory()
    {
        using var workspace = new TemporaryDirectory();
        var repoRoot = workspace.Path;
        Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));

        var projectDirectory = Path.Combine(repoRoot, "src", "ConsumerApp");
        Directory.CreateDirectory(projectDirectory);

        var result = RepoRootDetector.TryResolve(projectDirectory, string.Empty, CreateLogger());

        await Assert.That(result).IsEqualTo(Path.GetFullPath(repoRoot));
    }

    [Test]
    public async System.Threading.Tasks.Task TryResolve_Falls_Back_To_Solution_File_When_No_Vcs_Marker_Exists()
    {
        using var workspace = new TemporaryDirectory();
        var repoRoot = workspace.Path;
        await File.WriteAllTextAsync(Path.Combine(repoRoot, "Payload.slnx"), "<Solution />");

        var projectDirectory = Path.Combine(repoRoot, "samples", "ConsumerApp");
        Directory.CreateDirectory(projectDirectory);

        var result = RepoRootDetector.TryResolve(projectDirectory, string.Empty, CreateLogger());

        await Assert.That(result).IsEqualTo(Path.GetFullPath(repoRoot));
    }

    private static TaskLoggingHelper CreateLogger()
        => new(new TestLoggingTask { BuildEngine = new RecordingBuildEngine() });

    private sealed class TestLoggingTask : Microsoft.Build.Utilities.Task
    {
        public override bool Execute() => true;
    }
}
