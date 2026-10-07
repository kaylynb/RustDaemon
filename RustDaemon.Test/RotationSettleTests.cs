using RustDaemon.Targeting;

namespace RustDaemon.Test;

public sealed class RotationSettleTests
{
	[Fact]
	public void Observe_IsSettledAfterTheRequiredStillFrames()
	{
		var settle = new RotationSettle(movementThresholdDegrees: 0.05, settleFrames: 2);

		Assert.False(settle.Observe(0, 0));
		Assert.True(settle.Observe(0, 0));
	}

	[Fact]
	public void Observe_MovementRestartsTheStreak()
	{
		var settle = new RotationSettle(movementThresholdDegrees: 0.05, settleFrames: 2);

		settle.Observe(0, 0);
		Assert.False(settle.Observe(1, 0));
		Assert.False(settle.Observe(1, 0));
		Assert.True(settle.Observe(1, 0));
	}

	[Fact]
	public void Observe_CombinesYawAndPitchMovement()
	{
		var settle = new RotationSettle(movementThresholdDegrees: 1.0, settleFrames: 2);

		settle.Observe(0, 0);
		// 0.6 + 0.6 = 1.2 combined, above the 1.0 threshold.
		Assert.False(settle.Observe(0.6, 0.6));
		Assert.False(settle.Observe(0.6, 0.6));
		Assert.True(settle.Observe(0.6, 0.6));
	}

	[Fact]
	public void Reset_RestartsTheStreak()
	{
		var settle = new RotationSettle(movementThresholdDegrees: 0.05, settleFrames: 2);

		settle.Observe(0, 0);
		Assert.True(settle.Observe(0, 0));
		settle.Reset();
		Assert.False(settle.Observe(0, 0));
	}
}
