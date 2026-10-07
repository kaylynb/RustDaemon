using Microsoft.Extensions.Logging.Abstractions;
using RustDaemon.Monitoring;
using RustDaemon.Persistence;
using RustDaemon.Reporting;

namespace RustDaemon.Test;

public sealed class PlayerSightingTrackerTests
{
	[Fact]
	public async Task NameLearnedLater_ProjectsAcrossEarlierUnknownRecord()
	{
		var log = new InMemoryEventLog();
		var tracker = CreateTracker(log);
		var session = Guid.NewGuid();
		var start = DateTimeOffset.UtcNow;
		var token = TestContext.Current.CancellationToken;

		await tracker.StartSessionAsync(session, "CAM01", start, token);
		await tracker.ProcessFrameAsync(Frame(session, start, new PlayerObservation(42, null)), token);
		var immediateEvents = await log.ReadAllAsync(token);
		Assert.Contains(immediateEvents, playerEvent => playerEvent is PlayerSeenEvent);

		await tracker.ProcessFrameAsync(Frame(session, start.AddSeconds(2)), token);
		await tracker.ProcessFrameAsync(Frame(session, start.AddSeconds(3), new PlayerObservation(42, "Alice")), token);
		await tracker.EndSessionAsync(start.AddSeconds(4), token);

		var records = await PlayerReportProjector.ProjectAsync(log, cancellationToken: token);
		Assert.Equal(2, records.Count);
		Assert.All(records, record => Assert.Equal(["Alice"], record.NamesSeen));
	}

	[Fact]
	public async Task ShortDropout_StaysInOneAppearanceInterval()
	{
		var log = new InMemoryEventLog();
		var tracker = CreateTracker(log);
		var session = Guid.NewGuid();
		var start = DateTimeOffset.UtcNow;
		var token = TestContext.Current.CancellationToken;

		await tracker.StartSessionAsync(session, "CAM01", start, token);
		await tracker.ProcessFrameAsync(Frame(session, start, new PlayerObservation(42, "Alice")), token);
		await tracker.ProcessFrameAsync(Frame(session, start.AddMilliseconds(500)), token);
		await tracker.ProcessFrameAsync(
			Frame(session, start.AddMilliseconds(800), new PlayerObservation(42, null)),
			token
		);
		await tracker.EndSessionAsync(start.AddSeconds(2), token);

		var record = Assert.Single(await PlayerReportProjector.ProjectAsync(log, cancellationToken: token));
		Assert.Equal(start, record.FirstSeenUtc);
		Assert.Equal(start.AddSeconds(2), record.LeftUtc);
	}

	[Fact]
	public async Task ReappearanceAfterFrameGap_KeepsOneAppearance()
	{
		var log = new InMemoryEventLog();
		var tracker = CreateTracker(log);
		var session = Guid.NewGuid();
		var start = DateTimeOffset.UtcNow;
		var token = TestContext.Current.CancellationToken;

		await tracker.StartSessionAsync(session, "CAM01", start, token);
		await tracker.ProcessFrameAsync(Frame(session, start, new PlayerObservation(42, "Alice")), token);
		// No frame in between: the entity is absent from no frame, so the id is still the same
		// player and the appearance continues.
		await tracker.ProcessFrameAsync(Frame(session, start.AddSeconds(5), new PlayerObservation(42, "Alice")), token);
		await tracker.EndSessionAsync(start.AddSeconds(6), token);

		var record = Assert.Single(await PlayerReportProjector.ProjectAsync(log, cancellationToken: token));
		Assert.Equal(start, record.FirstSeenUtc);
		Assert.Equal(start.AddSeconds(6), record.LeftUtc);
		Assert.Equal(["Alice"], record.NamesSeen);
	}

	[Fact]
	public async Task SameFakeIdInDifferentSessions_DoesNotCrossCorrelate()
	{
		var log = new InMemoryEventLog();
		var tracker = CreateTracker(log);
		var firstSession = Guid.NewGuid();
		var secondSession = Guid.NewGuid();
		var start = DateTimeOffset.UtcNow;
		var token = TestContext.Current.CancellationToken;

		await tracker.StartSessionAsync(firstSession, "CAM01", start, token);
		await tracker.ProcessFrameAsync(Frame(firstSession, start, new PlayerObservation(42, null)), token);
		await tracker.EndSessionAsync(start.AddSeconds(1), token);
		await tracker.StartSessionAsync(secondSession, "CAM01", start.AddSeconds(2), token);
		await tracker.ProcessFrameAsync(
			Frame(secondSession, start.AddSeconds(2), new PlayerObservation(42, "Alice")),
			token
		);
		await tracker.EndSessionAsync(start.AddSeconds(3), token);

		var records = await PlayerReportProjector.ProjectAsync(log, cancellationToken: token);
		Assert.Empty(records.Single(record => record.SubscriptionSessionId == firstSession).NamesSeen);
		Assert.Equal(["Alice"], records.Single(record => record.SubscriptionSessionId == secondSession).NamesSeen);
	}

	[Fact]
	public async Task NameChange_PreservesBothNames()
	{
		var log = new InMemoryEventLog();
		var tracker = CreateTracker(log);
		var session = Guid.NewGuid();
		var start = DateTimeOffset.UtcNow;
		var token = TestContext.Current.CancellationToken;

		await tracker.StartSessionAsync(session, "CAM01", start, token);
		await tracker.ProcessFrameAsync(Frame(session, start, new PlayerObservation(42, "Alice")), token);
		await tracker.ProcessFrameAsync(
			Frame(session, start.AddMilliseconds(100), new PlayerObservation(42, "Bob")),
			token
		);
		await tracker.EndSessionAsync(start.AddSeconds(1), token);

		var record = Assert.Single(await PlayerReportProjector.ProjectAsync(log, cancellationToken: token));
		Assert.Equal(["Alice", "Bob"], record.NamesSeen);
	}

	[Fact]
	public async Task ReportTimes_AreConvertedFromUtcToRequestedTimeZone()
	{
		var log = new InMemoryEventLog();
		var token = TestContext.Current.CancellationToken;
		await log.AppendAsync(
			new PlayerSeenEvent
			{
				SubscriptionSessionId = Guid.NewGuid(),
				CameraId = "CAM01",
				FakeEntityId = "42",
				Utc = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero),
			},
			token
		);

		var timeZone = TimeZoneInfo.CreateCustomTimeZone("Test-0500", TimeSpan.FromHours(-5), "Test", "Test");
		var record = Assert.Single(await PlayerReportProjector.ProjectAsync(log, timeZone, token));

		Assert.Equal(new DateTimeOffset(2026, 8, 24, 7, 0, 0, TimeSpan.FromHours(-5)), record.FirstSeenLocal);
	}

	private static PlayerSightingTracker CreateTracker(IEventLog log) =>
		new(log, TimeSpan.FromSeconds(1), NullLogger<PlayerSightingTracker>.Instance);

	private static FrameObservation Frame(Guid session, DateTimeOffset timestamp, params PlayerObservation[] players) =>
		new(session, timestamp, players);
}
