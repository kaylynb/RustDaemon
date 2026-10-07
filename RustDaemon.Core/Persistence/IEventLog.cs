namespace RustDaemon.Persistence;

public interface IEventLog : IAsyncDisposable
{
	Task AppendAsync(PlayerEvent playerEvent, CancellationToken cancellationToken = default);

	Task<IReadOnlyList<PlayerEvent>> ReadAllAsync(CancellationToken cancellationToken = default);
}
