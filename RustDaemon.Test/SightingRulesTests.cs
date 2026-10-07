using RustDaemon.Monitoring;
using RustDaemon.Persistence;

namespace RustDaemon.Test;

public sealed class SightingRulesTests
{
	private static readonly EntitySessionKey Key = new(Guid.Empty, 42);

	[Theory]
	[InlineData(0.5, false)]
	[InlineData(1.0, true)]
	[InlineData(2.0, true)]
	public void ShouldClose_OnlyWhenAbsentForTheGap(double elapsedSeconds, bool expected)
	{
		var lastSeen = DateTimeOffset.UnixEpoch;
		var observed = lastSeen.AddSeconds(elapsedSeconds);

		Assert.Equal(expected, SightingRules.ShouldClose(lastSeen, observed, TimeSpan.FromSeconds(1)));
	}

	[Fact]
	public void AddName_IsNewOncePerScope_AndIgnoresBlank()
	{
		HashSet<(EntitySessionKey Key, string Name)> names = [];

		Assert.True(SightingRules.AddName(names, Key, "Alice"));
		Assert.False(SightingRules.AddName(names, Key, "Alice"));
		Assert.True(SightingRules.AddName(names, Key, "Bob"));
		Assert.False(SightingRules.AddName(names, Key, null));
		Assert.False(SightingRules.AddName(names, Key, "  "));
		Assert.True(SightingRules.AddName(names, new EntitySessionKey(Guid.Empty, 43), "Alice"));
	}

	[Fact]
	public void AddName_ListKeepsFirstSeenOrderWithoutDuplicates()
	{
		List<string> names = [];

		SightingRules.AddName(names, "Bob");
		SightingRules.AddName(names, "Alice");
		SightingRules.AddName(names, "Bob");

		Assert.Equal(["Bob", "Alice"], names);
	}
}
