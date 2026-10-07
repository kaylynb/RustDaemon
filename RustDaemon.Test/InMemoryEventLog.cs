using RustDaemon.Persistence;

namespace RustDaemon.Test;

internal sealed class InMemoryEventLog : IEventLog
{
	private readonly List<PlayerEvent> _events = [];

	public Task AppendAsync(PlayerEvent playerEvent, CancellationToken cancellationToken = default)
	{
		_events.Add(playerEvent);
		return Task.CompletedTask;
	}

	public Task<IReadOnlyList<PlayerEvent>> ReadAllAsync(CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<PlayerEvent>>(_events.ToArray());

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
