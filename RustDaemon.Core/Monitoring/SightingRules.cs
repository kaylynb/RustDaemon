using RustDaemon.Persistence;

namespace RustDaemon.Monitoring;

public static class SightingRules
{
	public static bool ShouldClose(DateTimeOffset lastSeenUtc, DateTimeOffset observedAtUtc, TimeSpan absenceGap) =>
		observedAtUtc - lastSeenUtc >= absenceGap;

	public static bool AddName(
		HashSet<(EntitySessionKey Key, string Name)> names,
		EntitySessionKey key,
		string? name
	) => !string.IsNullOrWhiteSpace(name) && names.Add((key, name));

	public static void AddName(List<string> names, string name)
	{
		if (!names.Contains(name, StringComparer.Ordinal))
		{
			names.Add(name);
		}
	}
}
