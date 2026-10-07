using Microsoft.Extensions.Logging;
using RustDaemon.Companion;
using RustDaemon.Configuration;
using RustDaemon.Lifecycle;
using RustDaemon.Monitoring;
using RustDaemon.Persistence;

namespace RustDaemon.Cli.Lifecycle;

public static class DaemonCommand
{
	public static async Task<int> RunAsync(DaemonOptions options)
	{
		try
		{
			var credentials = await CredentialsLoader.LoadAsync(options.CredentialsPath);

			using var loggerFactory = LoggerFactory.Create(builder =>
			{
				builder.AddSimpleConsole(configure => configure.TimestampFormat = "yyyy-MM-dd HH:mm:ss zzz ");
				builder.SetMinimumLevel(LogLevel.Information);
			});

			await using var log = new FileEventLog(options.LogPath);
			var tracker = new PlayerSightingTracker(
				log,
				options.AbsenceGap,
				loggerFactory.CreateLogger<PlayerSightingTracker>()
			);
			await using var monitor = new CameraMonitor(
				tracker,
				loggerFactory.CreateLogger<CameraMonitor>(),
				TimeProvider.System
			);

			await using var connection = new CompanionConnectionFactory(loggerFactory).Create(
				credentials,
				options.UseProxy
			);
			await using var runner = new DaemonRunner(
				connection,
				monitor,
				loggerFactory.CreateLogger<DaemonRunner>(),
				options.CameraId
			);

			var cancellation = new CancellationTokenSource();
			ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
			{
				eventArgs.Cancel = true;
				cancellation.Cancel();
			};
			Console.CancelKeyPress += cancelHandler;

			try
			{
				await runner.RunAsync(cancellation.Token);
			}
			finally
			{
				Console.CancelKeyPress -= cancelHandler;
				cancellation.Dispose();
			}

			return 0;
		}
		catch (Exception exception)
		{
			await Console.Error.WriteLineAsync($"Fatal error: {exception.Message}");
			return 1;
		}
	}
}
