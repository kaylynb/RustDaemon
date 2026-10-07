using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;

namespace RustDaemon.Monitoring;

public sealed class CameraMonitor(
	PlayerSightingTracker tracker,
	ILogger<CameraMonitor> logger,
	TimeProvider timeProvider
) : IAsyncDisposable
{
	private readonly Channel<MonitorWork> _work = Channel.CreateBounded<MonitorWork>(
		new BoundedChannelOptions(256)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true,
			SingleWriter = false,
		}
	);
	private Task? _worker;

	public Task StartAsync()
	{
		_worker ??= Task.Run(ProcessAsync);
		return Task.CompletedTask;
	}

	public Task BeginSessionAsync(
		Guid sourceSessionId,
		string cameraId,
		CancellationToken cancellationToken = default
	) =>
		WriteControlAsync(new BeginSessionWork(sourceSessionId, cameraId, timeProvider.GetUtcNow()), cancellationToken);

	public Task EndSessionAsync(CancellationToken cancellationToken = default) =>
		WriteControlAsync(new EndSessionWork(timeProvider.GetUtcNow()), cancellationToken);

	public void EnqueueFrame(Guid sourceSessionId, CameraRaysEventArg frame)
	{
		var observedAtUtc = timeProvider.GetUtcNow();
		var players = frame
			.Entities.Where(entity => entity.Type == CameraEntityType.Player)
			.Select(entity => new PlayerObservation(entity.EntityId, entity.Name))
			.ToArray();

		// Drop instead of blocking the camera callback; only the latest positions matter for sightings.
		_ = _work.Writer.TryWrite(new FrameWork(new FrameObservation(sourceSessionId, observedAtUtc, players)));
	}

	public async ValueTask DisposeAsync()
	{
		if (_worker is null)
		{
			return;
		}

		await EndSessionAsync();
		_work.Writer.TryComplete();
		await _worker.ConfigureAwait(false);
		_worker = null;
	}

	private async Task ProcessAsync()
	{
		await foreach (var work in _work.Reader.ReadAllAsync())
		{
			try
			{
				switch (work)
				{
					case BeginSessionWork begin:
						await tracker.StartSessionAsync(begin.SourceSessionId, begin.CameraId, begin.Timestamp);
						break;
					case EndSessionWork end:
						await tracker.EndSessionAsync(end.Timestamp);
						break;
					case FrameWork frame:
						await tracker.ProcessFrameAsync(frame.Observation);
						break;
				}
			}
			catch (Exception exception)
			{
				logger.LogError(exception, "Camera monitor work item failed");
			}
		}
	}

	private async Task WriteControlAsync(MonitorWork work, CancellationToken cancellationToken)
	{
		try
		{
			await _work.Writer.WriteAsync(work, cancellationToken);
		}
		catch (ChannelClosedException)
		{
			logger.LogDebug("Ignoring camera monitor command after shutdown");
		}
	}

	private closed record MonitorWork;

	private sealed record BeginSessionWork(Guid SourceSessionId, string CameraId, DateTimeOffset Timestamp)
		: MonitorWork;

	private sealed record EndSessionWork(DateTimeOffset Timestamp) : MonitorWork;

	private sealed record FrameWork(FrameObservation Observation) : MonitorWork;
}
