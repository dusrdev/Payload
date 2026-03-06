using Microsoft.Build.Framework;

namespace Payload.Tests.TestSupport;

internal sealed class RecordingBuildEngine : IBuildEngine
{
    public List<BuildErrorEventArgs> Errors { get; } = [];
    public List<BuildWarningEventArgs> Warnings { get; } = [];
    public List<BuildMessageEventArgs> Messages { get; } = [];

    public bool ContinueOnError => false;

    public int LineNumberOfTaskNode => 0;

    public int ColumnNumberOfTaskNode => 0;

    public string ProjectFileOfTaskNode => "Payload.Tests";

    public bool BuildProjectFile(string projectFileName, string[] targetNames, System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs)
        => throw new NotSupportedException();

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
    }

    public void LogErrorEvent(BuildErrorEventArgs e)
        => Errors.Add(e);

    public void LogMessageEvent(BuildMessageEventArgs e)
        => Messages.Add(e);

    public void LogWarningEvent(BuildWarningEventArgs e)
        => Warnings.Add(e);
}
