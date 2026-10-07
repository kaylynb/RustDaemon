using System.Text.Json;

namespace RustDaemon.Configuration;

public static class CredentialsLoader
{
	private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

	public static async Task<CredentialsDocument> LoadAsync(string path, CancellationToken cancellationToken = default)
	{
		await using var stream = File.OpenRead(Path.GetFullPath(path));
		var credentials = await JsonSerializer.DeserializeAsync<CredentialsDocument>(
			stream,
			JsonOptions,
			cancellationToken
		);
		if (
			credentials is null
			|| string.IsNullOrWhiteSpace(credentials.Ip)
			|| credentials.Port is <= 0 or > 65535
			|| credentials.PlayerId == 0
			|| credentials.PlayerToken == 0
		)
		{
			throw new InvalidOperationException(
				"Invalid credentials file: expected ip, valid port, playerId, and playerToken."
			);
		}

		return credentials;
	}
}
