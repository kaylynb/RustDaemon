using System.Globalization;
using Microsoft.Extensions.Logging;
using RustDaemon.Persistence;

namespace RustDaemon.Monitoring;

public sealed class PlayerSightingTracker(IEventLog log, TimeSpan absenceGap, ILogger<PlayerSightingTracker> logger)
{
	private readonly Dictionary<EntitySessionKey, DateTimeOffset> _active = new();
	private readonly HashSet<(EntitySessionKey Key, string Name)> _knownNames = [];
	private string _cameraId = string.Empty;
	private Guid? _sessionId;

	public async Task StartSessionAsync(
		Guid sessionId,
		string cameraId,
		DateTimeOffset timestamp,
		CancellationToken cancellationToken = default
	)
	{
		await EndSessionAsync(timestamp, cancellationToken);
		_sessionId = sessionId;
		_cameraId = cameraId;
		await log.AppendAsync(
			new SessionStartedEvent
			{
				SubscriptionSessionId = sessionId,
				CameraId = cameraId,
				Utc = timestamp.ToUniversalTime(),
			},
			cancellationToken
		);
		logger.LogInformation(
			"Started player correlation session {SessionId} for camera {CameraId}",
			sessionId,
			cameraId
		);
	}

	public async Task ProcessFrameAsync(FrameObservation frame, CancellationToken cancellationToken = default)
	{
		if (_sessionId != frame.SourceSessionId)
		{
			return;
		}

		var grouped = frame.Players.GroupBy(player => player.FakeEntityId).ToArray();
		var present = grouped.Select(group => group.Key).ToHashSet();

		foreach (var group in grouped)
		{
			var key = new EntitySessionKey(frame.SourceSessionId, group.Key);
			var newNames = group
				.Select(observation => observation.Name)
				.Where(name => SightingRules.AddName(_knownNames, key, name))
				.ToList();

			if (_active.TryAdd(key, frame.ObservedAtUtc))
			{
				await log.AppendAsync(
					new PlayerSeenEvent
					{
						SubscriptionSessionId = key.SubscriptionSessionId,
						CameraId = _cameraId,
						FakeEntityId = key.FakeEntityId.ToString(CultureInfo.InvariantCulture),
						Utc = frame.ObservedAtUtc.ToUniversalTime(),
					},
					cancellationToken
				);
				logger.LogInformation(
					"Player entity {EntityId} spotted in session {SessionId}",
					group.Key,
					frame.SourceSessionId
				);
			}
			else
			{
				_active[key] = frame.ObservedAtUtc;
			}

			foreach (var name in newNames)
			{
				await log.AppendAsync(
					new PlayerNameDetectedEvent
					{
						SubscriptionSessionId = key.SubscriptionSessionId,
						CameraId = _cameraId,
						FakeEntityId = key.FakeEntityId.ToString(CultureInfo.InvariantCulture),
						Name = name!,
						Utc = frame.ObservedAtUtc.ToUniversalTime(),
					},
					cancellationToken
				);
			}
		}

		foreach (var item in _active.ToArray())
		{
			if (
				!present.Contains(item.Key.FakeEntityId)
				&& SightingRules.ShouldClose(item.Value, frame.ObservedAtUtc, absenceGap)
			)
			{
				await CloseAsync(item.Key, frame.ObservedAtUtc, cancellationToken);
			}
		}
	}

	public async Task EndSessionAsync(DateTimeOffset timestamp, CancellationToken cancellationToken = default)
	{
		foreach (var item in _active.ToArray())
		{
			await CloseAsync(item.Key, timestamp, cancellationToken);
		}

		if (_sessionId is { } sessionId)
		{
			await log.AppendAsync(
				new SessionEndedEvent
				{
					SubscriptionSessionId = sessionId,
					CameraId = _cameraId,
					Utc = timestamp.ToUniversalTime(),
				},
				cancellationToken
			);
		}

		_sessionId = null;
		_knownNames.Clear();
	}

	private async Task CloseAsync(EntitySessionKey key, DateTimeOffset timestamp, CancellationToken cancellationToken)
	{
		await log.AppendAsync(
			new PlayerLeftEvent
			{
				SubscriptionSessionId = key.SubscriptionSessionId,
				CameraId = _cameraId,
				FakeEntityId = key.FakeEntityId.ToString(CultureInfo.InvariantCulture),
				Utc = timestamp.ToUniversalTime(),
			},
			cancellationToken
		);
		_active.Remove(key);
		logger.LogInformation(
			"Player entity {EntityId} sighting closed for session {SessionId}",
			key.FakeEntityId,
			key.SubscriptionSessionId
		);
	}
}
