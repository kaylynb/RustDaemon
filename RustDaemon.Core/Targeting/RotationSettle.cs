namespace RustDaemon.Targeting;

public sealed class RotationSettle(double movementThresholdDegrees, int settleFrames)
{
	public const double DefaultMovementThresholdDegrees = 0.05;

	public const int DefaultSettleFrames = 2;

	public static RotationSettle Default => new(DefaultMovementThresholdDegrees, DefaultSettleFrames);

	private bool _hasPrevious;
	private double _previousYawDegrees;
	private double _previousPitchDegrees;

	private int StableFrames { get; set; }

	public bool Observe(double yawDegrees, double pitchDegrees)
	{
		var movement = 0.0;
		if (_hasPrevious)
		{
			movement =
				Math.Abs(Angles.Wrap180(yawDegrees - _previousYawDegrees))
				+ Math.Abs(pitchDegrees - _previousPitchDegrees);
		}

		_previousYawDegrees = yawDegrees;
		_previousPitchDegrees = pitchDegrees;
		_hasPrevious = true;
		StableFrames = movement < movementThresholdDegrees ? StableFrames + 1 : 0;
		return StableFrames >= settleFrames;
	}

	public void Reset() => StableFrames = 0;
}
