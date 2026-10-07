using Microsoft.Extensions.Logging;
using RustDaemon.Companion;
using RustDaemon.Configuration;

namespace RustDaemon.Cli.Targeting;

internal static class TurretCommandHost
{
	public static Task<int> RunAsync(
		string commandName,
		string cameraId,
		string credentialsPath,
		bool useProxy,
		bool requireAutoTurret,
		Func<ICompanionCamera, CancellationToken, Task<int>> run,
		CancellationToken cancellationToken = default
	) =>
		RunAsync(
			commandName,
			cameraId,
			credentialsPath,
			useProxy,
			requireAutoTurret,
			run,
			null,
			null,
			cancellationToken
		);

	internal static async Task<int> RunAsync(
		string commandName,
		string cameraId,
		string credentialsPath,
		bool useProxy,
		bool requireAutoTurret,
		Func<ICompanionCamera, CancellationToken, Task<int>> run,
		ICompanionConnectionFactory? connectionFactory,
		ILoggerFactory? loggerFactory,
		CancellationToken cancellationToken = default
	)
	{
		CredentialsDocument credentials;
		try
		{
			credentials = await CredentialsLoader.LoadAsync(credentialsPath, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			return 130;
		}
		catch (Exception exception) when (exception is InvalidOperationException or IOException)
		{
			await Console.Error.WriteLineAsync(exception.Message, cancellationToken);
			return 1;
		}

		var ownedLoggerFactory = loggerFactory is null ? CreateLoggerFactory() : null;
		var loggers = loggerFactory ?? ownedLoggerFactory!;
		try
		{
			await using var connection = (connectionFactory ?? new CompanionConnectionFactory(loggers)).Create(
				credentials,
				useProxy
			);

			using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
			ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
			{
				eventArgs.Cancel = true;
				cancellation.Cancel();
			};
			Console.CancelKeyPress += cancelHandler;

			try
			{
				ICompanionCamera camera;
				try
				{
					camera = await CompanionRetry
						.Bounded(loggers.CreateLogger($"{commandName}.connect"))
						.ExecuteAsync(
							async token => await connection.ConnectCameraAsync(cameraId.Trim(), token),
							cancellation.Token
						);
				}
				catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
				{
					return 130;
				}
				catch (Exception exception)
				{
					await Console.Error.WriteLineAsync($"{commandName} failed: {exception.Message}", cancellationToken);
					return 1;
				}

				await using (camera)
				{
					if (requireAutoTurret && !camera.IsAutoTurret)
					{
						await Console.Error.WriteLineAsync(
							$"'{cameraId}' is not an auto-turret or is already being controlled by another user (control flags: {camera.ControlFlags}).",
							cancellationToken
						);
						return 1;
					}

					try
					{
						return await run(camera, cancellation.Token);
					}
					catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
					{
						return 130;
					}
					catch (Exception exception)
					{
						await Console.Error.WriteLineAsync(
							$"{commandName} failed: {exception.Message}",
							cancellationToken
						);
						return 1;
					}
				}
			}
			finally
			{
				Console.CancelKeyPress -= cancelHandler;
			}
		}
		finally
		{
			ownedLoggerFactory?.Dispose();
		}
	}

	private static ILoggerFactory CreateLoggerFactory() =>
		LoggerFactory.Create(builder =>
		{
			builder.AddSimpleConsole(configure => configure.TimestampFormat = "yyyy-MM-dd HH:mm:ss zzz ");
			builder.SetMinimumLevel(LogLevel.Warning);
		});
}
