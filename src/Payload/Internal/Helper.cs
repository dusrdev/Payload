namespace Payload.Internal;

internal static class Helper
{
	static bool IsDirectorySeparator(char c) =>
			c == Path.DirectorySeparatorChar ||
			c == Path.AltDirectorySeparatorChar;

	/// <summary>
	/// Returns a relative path from one path to another.
	/// </summary>
	public static string GetRelativePath(string relativeTo, string path)
	{
		if (relativeTo is null)
		{
			throw new ArgumentNullException(nameof(relativeTo));
		}
		if (path is null)
		{
			throw new ArgumentNullException(nameof(path));
		}
		if (relativeTo.Length == 0)
		{
			throw new ArgumentException("The path is empty.", nameof(relativeTo));
		}
		if (path.Length == 0)
		{
			throw new ArgumentException("The path is empty.", nameof(path));
		}
		relativeTo = Path.GetFullPath(relativeTo);
		path = Path.GetFullPath(path);
		var comparison = StringComparison.OrdinalIgnoreCase;
		var normalizedFrom = relativeTo.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		var normalizedTo = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
		if (string.Equals(normalizedFrom, normalizedTo, comparison))
		{
			return ".";
		}
		var rootFrom = Path.GetPathRoot(relativeTo);
		var rootTo = Path.GetPathRoot(path);
		if (!string.Equals(rootFrom, rootTo, comparison))
		{
			return path;
		}
		if (!IsDirectorySeparator(relativeTo[relativeTo.Length - 1]))
		{
			relativeTo += Path.DirectorySeparatorChar;
		}
		if (!IsDirectorySeparator(path[path.Length - 1]))
		{
			path += Path.DirectorySeparatorChar;
		}
		var fromUri = new Uri(relativeTo);
		var toUri = new Uri(path);
		var relativeUri = fromUri.MakeRelativeUri(toUri);
		var result = Uri.UnescapeDataString(relativeUri.ToString());
		if (Path.DirectorySeparatorChar != '/')
		{
			result = result.Replace('/', Path.DirectorySeparatorChar);
		}
		if (result.Length > 0 && IsDirectorySeparator(result[result.Length - 1]))
		{
			result = result.Substring(0, result.Length - 1);
		}
		if (result.Length == 0)
		{
			return ".";
		}
		return result;
	}
}