using RustDaemon.Companion;
using RustDaemon.Targeting;

namespace RustDaemon.Cli.Targeting;

public sealed record ObserveOptions(string CameraId, string CredentialsPath, bool UseProxy);

public static class ObserveCommand
{
	private const double PrintThresholdDegrees = 0.05;

	private static readonly TimeSpan ReadTimeout = TimeSpan.FromSeconds(1);

	public static async Task<int> RunAsync(ObserveOptions options) =>
		await TurretCommandHost.RunAsync(
			"observe",
			options.CameraId,
			options.CredentialsPath,
			options.UseProxy,
			requireAutoTurret: false,
			run: (camera, cancellationToken) => ObserveAsync(camera, options.CameraId, cancellationToken)
		);

	private static async Task<int> ObserveAsync(
		ICompanionCamera camera,
		string cameraId,
		CancellationToken cancellationToken
	)
	{
		await Console.Error.WriteLineAsync(
			$"Observing '{cameraId}' (flags: {camera.ControlFlags}). stdout is yaw,pitch in degrees (0 forward/level, positive right/up); Ctrl+C to stop.",
			cancellationToken
		);

		using var frames = CameraFrames.Subscribe(camera);
		var lastYaw = double.NaN;
		var lastPitch = double.NaN;
		while (true)
		{
			var rotation = await frames.ReadAsync(ReadTimeout, cancellationToken);
			if (rotation is null)
			{
				continue;
			}

			if (
				double.IsNaN(lastYaw)
				|| Math.Abs(Angles.Wrap180(rotation.YawDegrees - lastYaw)) >= PrintThresholdDegrees
				|| Math.Abs(rotation.PitchDegrees - lastPitch) >= PrintThresholdDegrees
			)
			{
				lastYaw = rotation.YawDegrees;
				lastPitch = rotation.PitchDegrees;
				Console.WriteLine(FormattableString.Invariant($"{lastYaw:F3},{lastPitch:F3}"));
			}
		}
	}
}
