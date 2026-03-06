namespace Payload.Tests.TestSupport;

internal static class TestEnvironment
{
    public static string RepositoryRoot
    {
        get
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current is not null)
            {
                if (File.Exists(Path.Combine(current.FullName, "AGENTS.md"))
                    && File.Exists(Path.Combine(current.FullName, "src", "Payload", "Payload.csproj")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the repository root for tests.");
        }
    }
}
