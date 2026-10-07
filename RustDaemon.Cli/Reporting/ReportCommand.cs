using System.Text.Json;
using RustDaemon.Persistence;
using RustDaemon.Reporting;

namespace RustDaemon.Cli.Reporting;

public sealed record ReportOptions(string LogPath, string TimeZoneId, bool Json);

public static class ReportCommand
{
	private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

	public static async Task<int> RunAsync(ReportOptions options)
	{
		TimeZoneInfo timeZone;
		try
		{
			timeZone = string.Equals(options.TimeZoneId, "local", StringComparison.OrdinalIgnoreCase)
				? TimeZoneInfo.Local
				: TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
		}
		catch (TimeZoneNotFoundException)
		{
			await Console.Error.WriteLineAsync($"Unknown time zone '{options.TimeZoneId}'.");
			return 1;
		}
		catch (InvalidTimeZoneException)
		{
			await Console.Error.WriteLineAsync($"Invalid time zone '{options.TimeZoneId}'.");
			return 1;
		}

		await using var log = new FileEventLog(options.LogPath);
		var records = await PlayerReportProjector.ProjectAsync(log, timeZone);
		if (options.Json)
		{
			Console.WriteLine(JsonSerializer.Serialize(records, JsonOptions));
		}
		else
		{
			foreach (var record in records)
			{
				var name =
					record.NamesSeen.Count == 0
						? $"entity {record.FakeEntityId}"
						: string.Join(" / ", record.NamesSeen);
				var end = record.LeftLocal?.ToString("O") ?? "active";
				Console.WriteLine($"{record.FirstSeenLocal:O} - {end}  [{record.CameraId}] {name}");
			}
		}

		return 0;
	}
}
