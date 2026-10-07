using System.Globalization;
using RustDaemon.Persistence;

namespace RustDaemon.Test;

public sealed class FileEventLogTests
{
	[Fact]
	public async Task Append_IsImmediatelyReadable_AndPreservesLargeEntityId()
	{
		var token = TestContext.Current.CancellationToken;
		var directory = System.IO.Directory.CreateTempSubdirectory("rustd-log-tests-");
		try
		{
			var path = System.IO.Path.Combine(directory.FullName, "events.jsonl");
			await using var log = new FileEventLog(path);
			var session = Guid.NewGuid();
			await log.AppendAsync(
				new PlayerSeenEvent
				{
					SubscriptionSessionId = session,
					CameraId = "CAM01",
					FakeEntityId = ulong.MaxValue.ToString(CultureInfo.InvariantCulture),
					Utc = DateTimeOffset.UtcNow,
				},
				token
			);

			var events = await log.ReadAllAsync(token);
			var playerEvent = Assert.IsType<PlayerSeenEvent>(Assert.Single(events));
			Assert.Equal(ulong.MaxValue.ToString(CultureInfo.InvariantCulture), playerEvent.FakeEntityId);
			var json = await File.ReadAllTextAsync(path, token);
			Assert.Contains("\"Type\":\"player_seen\"", json, StringComparison.Ordinal);
		}
		finally
		{
			System.IO.Directory.Delete(directory.FullName, true);
		}
	}
}
