namespace RustDaemon.Monitoring;

public sealed record PlayerObservation(ulong FakeEntityId, string? Name);

public sealed record FrameObservation(
	Guid SourceSessionId,
	DateTimeOffset ObservedAtUtc,
	IReadOnlyList<PlayerObservation> Players
);
