namespace RustDaemon.Targeting;

internal static class TurretLimits
{
	public const double MinPitchDegrees = -45.0;

	public const double MaxPitchDegrees = 75.0;

	public static bool IsReachablePitch(double pitchDegrees) =>
		pitchDegrees >= MinPitchDegrees && pitchDegrees <= MaxPitchDegrees;
}
