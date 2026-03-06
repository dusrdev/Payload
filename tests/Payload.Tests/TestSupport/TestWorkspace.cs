namespace Payload.Tests.TestSupport;

internal sealed class TestWorkspace : IDisposable
{
    private readonly TemporaryDirectory _temporaryDirectory = new();

    public TestWorkspace()
    {
        RootPath = _temporaryDirectory.Path;
        NuGetPackagesPath = Path.Combine(RootPath, ".nuget", "packages");

        CopyFile("AGENTS.md");
        CopyFile("README.md");
        CopyFile("RepoContentCopy.slnx");
        CopyDirectory("src");
        CopyDirectory("samples");
    }

    public string RootPath { get; }

    public string NuGetPackagesPath { get; }

    public void Dispose() => _temporaryDirectory.Dispose();

    private void CopyFile(string relativePath)
    {
        var sourcePath = Path.Combine(TestEnvironment.RepositoryRoot, relativePath);
        var destinationPath = Path.Combine(RootPath, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private void CopyDirectory(string relativePath)
    {
        var sourceRoot = Path.Combine(TestEnvironment.RepositoryRoot, relativePath);
        var destinationRoot = Path.Combine(RootPath, relativePath);

        foreach (var sourceDirectory in Directory.EnumerateDirectories(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativeDirectory = Path.GetRelativePath(sourceRoot, sourceDirectory);
            Directory.CreateDirectory(Path.Combine(destinationRoot, relativeDirectory));
        }

        Directory.CreateDirectory(destinationRoot);

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories))
        {
            var relativeFile = Path.GetRelativePath(sourceRoot, sourceFile);

            if (ShouldSkip(relativeFile))
            {
                continue;
            }

            var destinationFile = Path.Combine(destinationRoot, relativeFile);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationFile)!);
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }
    }

    private static bool ShouldSkip(string relativeFile)
    {
        var normalized = relativeFile.Replace('\\', '/');
        return normalized.Contains("/bin/", StringComparison.Ordinal)
               || normalized.StartsWith("bin/", StringComparison.Ordinal)
               || normalized.Contains("/obj/", StringComparison.Ordinal)
               || normalized.StartsWith("obj/", StringComparison.Ordinal);
    }
}
