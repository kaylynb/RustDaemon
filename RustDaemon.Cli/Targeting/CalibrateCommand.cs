using RustDaemon.Companion;
using RustDaemon.Targeting;

namespace RustDaemon.Cli.Targeting;

public sealed record CalibrateOptions(string CameraId, string CredentialsPath, bool UseProxy);

public static class CalibrateCommand
{
	private const double ProbeMouseUnits = 10;

	private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

	public static async Task<int> RunAsync(CalibrateOptions options, TimeProvider? timeProvider = null)
	{
		var clock = timeProvider ?? TimeProvider.System;
		return await TurretCommandHost.RunAsync(
			"calibrate",
			options.CameraId,
			options.CredentialsPath,
			options.UseProxy,
			requireAutoTurret: true,
			run: (turret, cancellationToken) => RunProbeAsync(turret, options.CameraId, clock, cancellationToken)
		);
	}

	private static async Task<int> RunProbeAsync(
		ICompanionCamera turret,
		string cameraId,
		TimeProvider timeProvider,
		CancellationToken cancellationToken
	)
	{
		using var frames = CameraFrames.Subscribe(turret);
		Console.WriteLine(
			$"Probing '{cameraId}' with {ProbeMouseUnits:F0} mouse units per axis. The turret must be powered, off peacekeeper, and controlled by this client."
		);

		async Task<int> FailAsync(string reason)
		{
			await Console.Error.WriteLineAsync($"Calibration failed: {reason}.", cancellationToken);
			return 1;
		}

		var start = await WaitForSettledAsync(frames, ProbeTimeout, timeProvider, cancellationToken);
		if (start is null)
		{
			return await FailAsync("no settled rotation was reported");
		}

		if (!await ProbeAsync(turret, (float)ProbeMouseUnits, 0, cancellationToken))
		{
			return await FailAsync("the '+x' probe input was rejected");
		}

		var afterX = await WaitForSettledAsync(frames, ProbeTimeout, timeProvider, cancellationToken);
		if (afterX is null)
		{
			return await FailAsync("the turret did not settle after the '+x' probe");
		}

		if (!await ProbeAsync(turret, 0, (float)ProbeMouseUnits, cancellationToken))
		{
			return await FailAsync("the '+y' probe input was rejected");
		}

		var afterY = await WaitForSettledAsync(frames, ProbeTimeout, timeProvider, cancellationToken);
		if (afterY is null)
		{
			return await FailAsync("the turret did not settle after the '+y' probe");
		}

		var mapping = AimMapping.FromProbe(start, afterX, afterY, ProbeMouseUnits);

		PrintMeasurements(start, afterX, afterY, mapping);

		if (!mapping.IsUsable)
		{
			await Console.Error.WriteLineAsync(
				"The measured mapping is near-singular and unusable; the turret may be powered down, unresponsive, or driven by another viewer.",
				cancellationToken
			);
			return 1;
		}

		return 0;
	}

	private static async Task<bool> ProbeAsync(
		ICompanionCamera turret,
		float mouseX,
		float mouseY,
		CancellationToken cancellationToken
	)
	{
		var response = await turret.LookAsync(mouseX, mouseY, cancellationToken);
		return response.IsSuccess;
	}

	private static void PrintMeasurements(
		CameraRotation start,
		CameraRotation afterX,
		CameraRotation afterY,
		AimMapping mapping
	)
	{
		static string Degrees(double degrees) => FormattableString.Invariant($"{degrees, 9:F3}");

		Console.WriteLine("Rotation (yaw, pitch in degrees; 0 forward/level, positive right/up):");
		Console.WriteLine($"  settled start   {Degrees(start.YawDegrees)}, {Degrees(start.PitchDegrees)}");
		Console.WriteLine($"  after +x probe  {Degrees(afterX.YawDegrees)}, {Degrees(afterX.PitchDegrees)}");
		Console.WriteLine($"  after +y probe  {Degrees(afterY.YawDegrees)}, {Degrees(afterY.PitchDegrees)}");

		var yawX = Angles.Wrap180(afterX.YawDegrees - start.YawDegrees);
		var yawY = Angles.Wrap180(afterY.YawDegrees - afterX.YawDegrees);
		var pitchX = afterX.PitchDegrees - start.PitchDegrees;
		var pitchY = afterY.PitchDegrees - afterX.PitchDegrees;

		Console.WriteLine($"Deltas for {ProbeMouseUnits:F0} mouse units (degrees):");
		Console.WriteLine(FormattableString.Invariant($"  yaw   +x {yawX, 8:F3}   +y {yawY, 8:F3}"));
		Console.WriteLine(FormattableString.Invariant($"  pitch +x {pitchX, 8:F3}   +y {pitchY, 8:F3}"));

		Console.WriteLine("Measured mapping (degrees per mouse unit):");
		PrintMapping(mapping);
		Console.WriteLine("Default mapping (degrees per mouse unit):");
		PrintMapping(AimMapping.Default);

		var deviation = new[]
		{
			Math.Abs(mapping.YawPerMouseX - AimMapping.Default.YawPerMouseX),
			Math.Abs(mapping.YawPerMouseY - AimMapping.Default.YawPerMouseY),
			Math.Abs(mapping.PitchPerMouseX - AimMapping.Default.PitchPerMouseX),
			Math.Abs(mapping.PitchPerMouseY - AimMapping.Default.PitchPerMouseY),
		}.Max();

		Console.WriteLine(
			FormattableString.Invariant($"Largest deviation from default: {deviation:F4} degrees per mouse unit.")
		);
	}

	private static void PrintMapping(AimMapping mapping) =>
		Console.WriteLine(
			FormattableString.Invariant(
				$"  yaw   = {mapping.YawPerMouseX, 8:F4}/x + {mapping.YawPerMouseY, 8:F4}/y{Environment.NewLine}  pitch = {mapping.PitchPerMouseX, 8:F4}/x + {mapping.PitchPerMouseY, 8:F4}/y"
			)
		);

	private static async Task<CameraRotation?> WaitForSettledAsync(
		CameraFrames frames,
		TimeSpan timeout,
		TimeProvider timeProvider,
		CancellationToken cancellationToken
	)
	{
		var deadline = timeProvider.GetUtcNow() + timeout;
		var settle = RotationSettle.Default;
		CameraRotation? last = null;

		while (true)
		{
			var remaining = deadline - timeProvider.GetUtcNow();
			if (remaining <= TimeSpan.Zero)
			{
				return last;
			}

			var rotation = await frames.ReadAsync(remaining, cancellationToken);
			if (rotation is null)
			{
				return last;
			}

			last = rotation;
			if (settle.Observe(rotation.YawDegrees, rotation.PitchDegrees))
			{
				return rotation;
			}
		}
	}
}
