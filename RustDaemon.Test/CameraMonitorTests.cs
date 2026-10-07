using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using RustDaemon.Monitoring;
using RustDaemon.Persistence;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;

namespace RustDaemon.Test;

public sealed class CameraMonitorTests
{
	[Fact]
	public async Task EnqueueFrame_StampsFramesWithTheInjectedClock()
	{
		var token = TestContext.Current.CancellationToken;
		var start = new DateTimeOffset(2026, 8, 24, 12, 0, 0, TimeSpan.Zero);
		var time = new FakeTimeProvider(start);
		var log = new InMemoryEventLog();
		var tracker = new PlayerSightingTracker(
			log,
			TimeSpan.FromSeconds(1),
			NullLogger<PlayerSightingTracker>.Instance
		);
		var monitor = new CameraMonitor(tracker, NullLogger<CameraMonitor>.Instance, time);
		var session = Guid.NewGuid();

		await monitor.StartAsync();
		await monitor.BeginSessionAsync(session, "CAM01", token);

		time.Advance(TimeSpan.FromSeconds(3));
		monitor.EnqueueFrame(session, PlayerFrame(42, "Alice"));

		await monitor.DisposeAsync();

		var seen = Assert.Single((await log.ReadAllAsync(token)).OfType<PlayerSeenEvent>());
		Assert.Equal(start.AddSeconds(3), seen.Utc);
	}

	private static CameraRaysEventArg PlayerFrame(ulong entityId, string? name) =>
		new()
		{
			Entities =
			[
				new CameraEntity
				{
					EntityId = entityId,
					Type = CameraEntityType.Player,
					Name = name,
				},
			],
		};
}
