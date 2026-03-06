using CliWrap;
using CliWrap.Buffered;

namespace Payload.Tests.TestSupport;

internal static class DotnetCommand
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public static async Task<CommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environmentVariables = null)
    {
        await Gate.WaitAsync();
        try
        {
            var command = Cli.Wrap("dotnet")
                .WithArguments(builder =>
                {
                    foreach (var argument in arguments)
                    {
                        builder.Add(argument);
                    }
                })
                .WithWorkingDirectory(workingDirectory)
                .WithValidation(CommandResultValidation.None);

            if (environmentVariables is not null)
            {
                command = command.WithEnvironmentVariables(variables =>
                {
                    foreach (var (key, value) in environmentVariables)
                    {
                        variables.Set(key, value);
                    }
                });
            }

            var result = await command.ExecuteBufferedAsync();
            return new CommandResult(result.ExitCode, result.StandardOutput, result.StandardError);
        }
        finally
        {
            Gate.Release();
        }
    }
}

internal sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError)
{
    public string CombinedOutput => StandardOutput + StandardError;
}
