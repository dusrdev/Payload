using Microsoft.Build.Framework;

namespace Payload.Internal;

internal sealed class PolicyMap
{
    private readonly Dictionary<(string PackageId, string Tag), PolicyState> _policyMap;

    private PolicyMap(Dictionary<(string PackageId, string Tag), PolicyState> policyMap)
    {
        _policyMap = policyMap;
    }

    public static PolicyMap Create(IEnumerable<ITaskItem> items)
    {
        var map = new Dictionary<(string PackageId, string Tag), PolicyState>(TupleComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            var packageId = item.ItemSpec;
            var tag = item.GetMetadata("Tag");
            var disableRaw = item.GetMetadata("Disable");
            var pathKind = item.GetMetadata("PathKind");

            if (string.IsNullOrWhiteSpace(packageId) || string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            var disabled = bool.TryParse(disableRaw, out var parsed) && parsed;
            map[(packageId, tag)] = new PolicyState(disabled, TryParsePathKind(pathKind, out var parsedPathKind) ? parsedPathKind : null, pathKind);
        }

        return new PolicyMap(map);
    }

    public bool IsDisabled(string packageId, string tag)
        => _policyMap.TryGetValue((packageId, tag), out var policy) && policy.Disabled;

    public bool TryGetPathKind(string packageId, string tag, out PathKind? pathKind, out string? rawPathKind)
    {
        if (_policyMap.TryGetValue((packageId, tag), out var policy))
        {
            pathKind = policy.PathKind;
            rawPathKind = policy.RawPathKind;
            return true;
        }

        pathKind = null;
        rawPathKind = null;
        return false;
    }

    private static bool TryParsePathKind(string? value, out PathKind pathKind)
        => Enum.TryParse(value, ignoreCase: true, out pathKind);

    private sealed class TupleComparer : IEqualityComparer<(string PackageId, string Tag)>
    {
        public static readonly TupleComparer OrdinalIgnoreCase = new();

        public bool Equals((string PackageId, string Tag) x, (string PackageId, string Tag) y)
            => string.Equals(x.PackageId, y.PackageId, StringComparison.OrdinalIgnoreCase)
               && string.Equals(x.Tag, y.Tag, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string PackageId, string Tag) obj)
            => (StringComparer.OrdinalIgnoreCase.GetHashCode(obj.PackageId) * 397)
               ^ StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Tag);
    }

    private sealed record PolicyState(bool Disabled, PathKind? PathKind, string? RawPathKind);
}
