using RustDaemon.Targeting;

namespace RustDaemon.Test;

public sealed class AimMappingTests
{
	private static readonly AimMapping DefaultMapping = new(4, 0, 0, 4);

	[Fact]
	public void TryInvert_DefaultMapping_SolvesYawError()
	{
		var solved = DefaultMapping.TryInvert(yawErrorDegrees: 8, pitchErrorDegrees: 0, out var mouseX, out var mouseY);

		Assert.True(solved);
		Assert.Equal(2, mouseX, 3);
		Assert.Equal(0, mouseY, 3);
	}

	[Fact]
	public void TryInvert_DefaultMapping_SolvesPitchError()
	{
		var solved = DefaultMapping.TryInvert(yawErrorDegrees: 0, pitchErrorDegrees: 8, out var mouseX, out var mouseY);

		Assert.True(solved);
		Assert.Equal(0, mouseX, 3);
		Assert.Equal(2, mouseY, 3);
	}

	[Fact]
	public void TryInvert_DegenerateMapping_ReturnsFalse()
	{
		var mapping = new AimMapping(1, 2, 2, 4);

		Assert.False(mapping.TryInvert(1, 1, out _, out _));
		Assert.False(mapping.IsUsable);
	}

	[Fact]
	public void Default_IsUsableAndMatchesTheMeasuredGain()
	{
		Assert.True(AimMapping.Default.IsUsable);
		Assert.Equal(DefaultMapping, AimMapping.Default);
	}

	[Fact]
	public void FromProbe_ComputesACrossCoupledMappingFromTwoProbes()
	{
		// The +x probe rotates yaw and pitch; the +y probe rotates both too (cross-coupled).
		var mapping = AimMapping.FromProbe(
			start: new CameraRotation(0, 0),
			afterX: new CameraRotation(40, 2),
			afterY: new CameraRotation(44, -36),
			mouseUnits: 10
		);

		Assert.Equal(4.0, mapping.YawPerMouseX, 3);
		Assert.Equal(0.4, mapping.YawPerMouseY, 3);
		Assert.Equal(0.2, mapping.PitchPerMouseX, 3);
		Assert.Equal(-3.8, mapping.PitchPerMouseY, 3);
	}

	[Fact]
	public void TryInvert_CrossCoupledMapping_SolvesBothAxes()
	{
		var mapping = new AimMapping(2, 1, 1, -2);

		Assert.True(mapping.TryInvert(yawErrorDegrees: 10, pitchErrorDegrees: 4, out var mouseX, out var mouseY));
		Assert.Equal(4.8, mouseX, 3);
		Assert.Equal(0.4, mouseY, 3);
	}
}
