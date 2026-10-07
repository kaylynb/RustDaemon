using System.Text.Json.Serialization;

namespace RustDaemon.Persistence;

public static class PlayerEventTypes
{
	public const string DiscriminatorPropertyName = "Type";
	public const string SessionStarted = "session_started";
	public const string SessionEnded = "session_ended";
	public const string PlayerSeen = "player_seen";
	public const string PlayerNameDetected = "player_name_detected";
	public const string PlayerLeft = "player_left";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = PlayerEventTypes.DiscriminatorPropertyName)]
[JsonDerivedType(typeof(SessionStartedEvent), PlayerEventTypes.SessionStarted)]
[JsonDerivedType(typeof(SessionEndedEvent), PlayerEventTypes.SessionEnded)]
[JsonDerivedType(typeof(PlayerSeenEvent), PlayerEventTypes.PlayerSeen)]
[JsonDerivedType(typeof(PlayerNameDetectedEvent), PlayerEventTypes.PlayerNameDetected)]
[JsonDerivedType(typeof(PlayerLeftEvent), PlayerEventTypes.PlayerLeft)]
public closed record PlayerEvent
{
	public Guid SubscriptionSessionId { get; init; }
	public string CameraId { get; init; } = string.Empty;
	public DateTimeOffset Utc { get; init; }
}

public sealed record SessionStartedEvent : PlayerEvent;

public sealed record SessionEndedEvent : PlayerEvent;

public sealed record PlayerSeenEvent : PlayerEvent
{
	public string FakeEntityId { get; init; } = string.Empty;
}

public sealed record PlayerNameDetectedEvent : PlayerEvent
{
	public string FakeEntityId { get; init; } = string.Empty;
	public string Name { get; init; } = string.Empty;
}

public sealed record PlayerLeftEvent : PlayerEvent
{
	public string FakeEntityId { get; init; } = string.Empty;
}
