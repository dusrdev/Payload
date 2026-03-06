using Microsoft.Build.Utilities;

namespace Payload.Tests.TestSupport;

internal static class TestTaskItem
{
    public static TaskItem Create(string include, params (string Name, string Value)[] metadata)
    {
        var item = new TaskItem(include);

        foreach (var (name, value) in metadata)
        {
            item.SetMetadata(name, value);
        }

        return item;
    }
}
