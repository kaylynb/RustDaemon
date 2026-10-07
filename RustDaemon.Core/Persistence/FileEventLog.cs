using System.Text;
using System.Text.Json;

namespace RustDaemon.Persistence;

public sealed class FileEventLog(string path) : IEventLog
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.General)
	{
		WriteIndented = false,
	};

	private readonly SemaphoreSlim _gate = new(1, 1);

	public async Task AppendAsync(PlayerEvent playerEvent, CancellationToken cancellationToken = default)
	{
		await _gate.WaitAsync(cancellationToken);
		try
		{
			var directory = Path.GetDirectoryName(path);
			if (!string.IsNullOrEmpty(directory))
			{
				Directory.CreateDirectory(directory);
			}

			var line = JsonSerializer.Serialize(playerEvent, JsonOptions) + Environment.NewLine;
			await File.AppendAllTextAsync(path, line, new UTF8Encoding(false), cancellationToken);
		}
		finally
		{
			_gate.Release();
		}
	}

	public async Task<IReadOnlyList<PlayerEvent>> ReadAllAsync(CancellationToken cancellationToken = default)
	{
		if (!File.Exists(path))
		{
			return [];
		}

		return
		[
			.. (await File.ReadAllLinesAsync(path, cancellationToken))
				.Where(line => !string.IsNullOrWhiteSpace(line))
				.Select(line =>
					JsonSerializer.Deserialize<PlayerEvent>(line, JsonOptions)
					?? throw new InvalidDataException($"Unable to deserialize an event in '{path}'.")
				),
		];
	}

	public ValueTask DisposeAsync()
	{
		_gate.Dispose();
		return ValueTask.CompletedTask;
	}
}
