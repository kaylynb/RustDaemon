namespace RustDaemon.Persistence;

public readonly record struct EntitySessionKey(Guid SubscriptionSessionId, ulong FakeEntityId);
