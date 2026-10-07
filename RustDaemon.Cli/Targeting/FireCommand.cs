using RustDaemon.Companion;
using RustDaemon.Targeting;
using RustPlusApi.Data.Cameras;

namespace RustDaemon.Cli.Targeting;

public sealed record FireOptions(
	string CameraId,
	string TargetsPath,
	string CredentialsPath,
	bool UseProxy,
	int Magazine
);

public static class FireCommand
{
	private static readonly TimeSpan FrameTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan ShutdownGrace = TimeSpan.FromMilliseconds(100);

	public static async Task<int> RunAsync(FireOptions options, TimeProvider? timeProvider = null)
	{
		IReadOnlyList<FireTarget> targets;
		try
		{
			targets = await FireTargetsLoader.LoadAsync(options.TargetsPath);
		}
		catch (Exception exception) when (exception is InvalidOperationException or InvalidDataException or IOException)
		{
			await Console.Error.WriteLineAsync(exception.Message);
			return 1;
		}

		if (targets.Count == 0)
		{
			await Console.Error.WriteLineAsync($"Targets file '{options.TargetsPath}' contains no targets.");
			return 1;
		}

		return await TurretCommandHost.RunAsync(
			"fire",
			options.CameraId,
			options.CredentialsPath,
			options.UseProxy,
			requireAutoTurret: true,
			run: (turret, cancellationToken) =>
				RunCycleAsync(turret, targets, options.Magazine, timeProvider ?? TimeProvider.System, cancellationToken)
		);
	}

	private static async Task<int> RunCycleAsync(
		ICompanionCamera turret,
		IReadOnlyList<FireTarget> targets,
		int magazine,
		TimeProvider timeProvider,
		CancellationToken cancellationToken
	)
	{
		using var frames = CameraFrames.Subscribe(turret);

		var aimOptions = TurretAimOptions.Default with { MagazineSize = magazine };
		var session = new AimSession(targets, AimMapping.Default, aimOptions);

		while (session is { IsComplete: false, IsFaulted: false })
		{
			var rotation = await frames.ReadAsync(FrameTimeout, cancellationToken);
			if (rotation is null)
			{
				await Console.Error.WriteLineAsync(
					"Camera frames stopped before all targets were fired.",
					cancellationToken
				);
				return 1;
			}

			var now = timeProvider.GetUtcNow();
			var action = session.Step(rotation, now);
			switch (action)
			{
				case AimLook look:
					var lookResponse = await turret.LookAsync(look.MouseDeltaX, look.MouseDeltaY, cancellationToken);
					if (!lookResponse.IsSuccess)
					{
						await Console.Error.WriteLineAsync(
							$"Look failed: {lookResponse.Error?.Code} {lookResponse.Error?.Message}.",
							cancellationToken
						);
						return 1;
					}

					break;
				case AimReload:
					var reload = await turret.ReloadAsync(cancellationToken);
					if (!reload.IsSuccess)
					{
						await Console.Error.WriteLineAsync(
							$"Reload failed: {reload.Error?.Code} {reload.Error?.Message}.",
							cancellationToken
						);
						return 1;
					}

					break;
				case AimFire:
					var fired = await ShootAsync(turret, cancellationToken);
					session.ConfirmShot(fired, now);
					if (!fired)
					{
						await Console.Error.WriteLineAsync(
							$"Stopped after {session.ShotsFired} of {targets.Count} shots.",
							cancellationToken
						);
						return 1;
					}

					Console.WriteLine($"Fired {session.ShotsFired}/{targets.Count}.");
					break;
				case AimTimedOut:
					await Console.Error.WriteLineAsync(
						$"Timed out aiming at target {session.TargetIndex + 1} of {targets.Count} after {session.ShotsFired} shot(s).",
						cancellationToken
					);
					return 1;
				case AimWait:
				case AimComplete:
					break;
			}
		}

		await Task.Delay(ShutdownGrace, cancellationToken);
		return 0;
	}

	private static async Task<bool> ShootAsync(ICompanionCamera turret, CancellationToken cancellationToken)
	{
		var press = await turret.SendInputAsync(CameraButtons.FirePrimary, cancellationToken);
		if (!press.IsSuccess)
		{
			await Console.Error.WriteLineAsync(
				$"Fire failed: {press.Error?.Code} {press.Error?.Message}.",
				cancellationToken
			);
			return false;
		}

		var release = await turret.SendInputAsync(CameraButtons.None, cancellationToken);
		if (!release.IsSuccess)
		{
			await Console.Error.WriteLineAsync(
				$"Fire release failed: {release.Error?.Code} {release.Error?.Message}.",
				cancellationToken
			);
			return false;
		}

		return true;
	}
}
