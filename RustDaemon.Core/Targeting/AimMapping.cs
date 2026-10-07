namespace RustDaemon.Targeting;

public sealed record AimMapping(double YawPerMouseX, double YawPerMouseY, double PitchPerMouseX, double PitchPerMouseY)
{
	public static readonly AimMapping Default = new(4, 0, 0, 4);

	public static AimMapping FromProbe(
		CameraRotation start,
		CameraRotation afterX,
		CameraRotation afterY,
		double mouseUnits
	) =>
		new(
			Angles.Wrap180(afterX.YawDegrees - start.YawDegrees) / mouseUnits,
			Angles.Wrap180(afterY.YawDegrees - afterX.YawDegrees) / mouseUnits,
			(afterX.PitchDegrees - start.PitchDegrees) / mouseUnits,
			(afterY.PitchDegrees - afterX.PitchDegrees) / mouseUnits
		);

	private double Determinant => YawPerMouseX * PitchPerMouseY - YawPerMouseY * PitchPerMouseX;

	public bool IsUsable => Math.Abs(Determinant) > 0.25;

	public bool TryInvert(double yawErrorDegrees, double pitchErrorDegrees, out double mouseX, out double mouseY)
	{
		var determinant = Determinant;
		if (Math.Abs(determinant) < 1e-6)
		{
			mouseX = 0;
			mouseY = 0;
			return false;
		}

		mouseX = (PitchPerMouseY * yawErrorDegrees - YawPerMouseY * pitchErrorDegrees) / determinant;
		mouseY = (YawPerMouseX * pitchErrorDegrees - PitchPerMouseX * yawErrorDegrees) / determinant;
		return true;
	}
}
