using System.Globalization;
using RustDaemon.Monitoring;
using RustDaemon.Persistence;

namespace RustDaemon.Reporting;

public sealed record PlayerReportRecord
{
	public Guid SubscriptionSessionId { get; init; }
	public string CameraId { get; init; } = string.Empty;
	public string FakeEntityId { get; init; } = string.Empty;
	public List<string> NamesSeen { get; } = [];
	public DateTimeOffset FirstSeenUtc { get; init; }
	public DateTimeOffset FirstSeenLocal { get; init; }
	public DateTimeOffset? LeftUtc { get; set; }
	public DateTimeOffset? LeftLocal { get; set; }
}

public static class PlayerReportProjector
{
	public static async Task<IReadOnlyList<PlayerReportRecord>> ProjectAsync(
		IEventLog log,
		TimeZoneInfo? timeZone = null,
		CancellationToken cancellationToken = default
	)
	{
		timeZone ??= TimeZoneInfo.Local;
		var events = await log.ReadAllAsync(cancellationToken);
		List<PlayerReportRecord> records = [with(capacity: events.Count)];
		Dictionary<EntitySessionKey, SightingState> states = [with(capacity: events.Count)];

		foreach (var playerEvent in events)
		{
			switch (playerEvent)
			{
				case PlayerSeenEvent seen:
				{
					if (!TryCreateKey(seen.SubscriptionSessionId, seen.FakeEntityId, out var key))
					{
						break;
					}

					var state = GetOrAdd(states, key);
					var record = new PlayerReportRecord
					{
						SubscriptionSessionId = seen.SubscriptionSessionId,
						CameraId = seen.CameraId,
						FakeEntityId = seen.FakeEntityId,
						FirstSeenUtc = seen.Utc,
						FirstSeenLocal = TimeZoneInfo.ConvertTime(seen.Utc, timeZone),
					};
					foreach (var name in state.Names)
					{
						SightingRules.AddName(record.NamesSeen, name);
					}

					state.Records.Add(record);
					state.Open = record;
					records.Add(record);
					break;
				}
				case PlayerNameDetectedEvent named when !string.IsNullOrWhiteSpace(named.Name):
				{
					if (!TryCreateKey(named.SubscriptionSessionId, named.FakeEntityId, out var key))
					{
						break;
					}

					var state = GetOrAdd(states, key);
					SightingRules.AddName(state.Names, named.Name);
					foreach (var record in state.Records)
					{
						SightingRules.AddName(record.NamesSeen, named.Name);
					}

					break;
				}
				case PlayerLeftEvent left:
				{
					if (
						!TryCreateKey(left.SubscriptionSessionId, left.FakeEntityId, out var key)
						|| !states.TryGetValue(key, out var state)
						|| state.Open is not { } open
					)
					{
						break;
					}

					open.LeftUtc = left.Utc;
					open.LeftLocal = TimeZoneInfo.ConvertTime(left.Utc, timeZone);
					state.Open = null;
					break;
				}
				case SessionStartedEvent:
				case SessionEndedEvent:
					break;
			}
		}

		return records;
	}

	private static bool TryCreateKey(Guid subscriptionSessionId, string fakeEntityId, out EntitySessionKey key)
	{
		if (ulong.TryParse(fakeEntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var entityId))
		{
			key = new EntitySessionKey(subscriptionSessionId, entityId);
			return true;
		}

		key = default;
		return false;
	}

	private static SightingState GetOrAdd(Dictionary<EntitySessionKey, SightingState> states, EntitySessionKey key)
	{
		if (!states.TryGetValue(key, out var state))
		{
			state = new SightingState();
			states.Add(key, state);
		}

		return state;
	}

	private sealed class SightingState
	{
		public List<string> Names { get; } = [];
		public List<PlayerReportRecord> Records { get; } = [];
		public PlayerReportRecord? Open { get; set; }
	}
}
