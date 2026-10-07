using Microsoft.Extensions.Logging.Abstractions;
using RustDaemon.Lifecycle;
using RustDaemon.Monitoring;
using RustDaemon.Persistence;

namespace RustDaemon.Test;

public sealed class DaemonRunnerTests
{
	[Fact]
	public async Task RunAsync_OnTransportLoss_EndsTheSessionAndReconnects()
	{
		var path = Path.Combine(Path.GetTempPath(), $"rustd-daemon-{Guid.NewGuid():N}.jsonl");
		try
		{
			await using var log = new FileEventLog(path);
			var tracker = new PlayerSightingTracker(
				log,
				TimeSpan.FromSeconds(1.5),
				NullLogger<PlayerSightingTracker>.Instance
			);
			var monitor = new CameraMonitor(tracker, NullLogger<CameraMonitor>.Instance, TimeProvider.System);
			await using var connection = new InMemoryCompanionConnection(() => new InMemoryCompanionCamera("CAM01"));
			await using var runner = new DaemonRunner(connection, monitor, NullLogger<DaemonRunner>.Instance, "CAM01");

			using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
			var run = runner.RunAsync(cancellation.Token);

			Assert.True(await connection.WaitForConnectAsync(1, TimeSpan.FromSeconds(5)));
			connection.LoseTransport();
			Assert.True(await connection.WaitForConnectAsync(2, TimeSpan.FromSeconds(5)));

			await run;
			await monitor.DisposeAsync();

			var events = await log.ReadAllAsync(TestContext.Current.CancellationToken);
			Assert.Equal(2, events.OfType<SessionStartedEvent>().Count());
			Assert.Equal(2, events.OfType<SessionEndedEvent>().Count());
		}
		finally
		{
			File.Delete(path);
		}
	}
}
