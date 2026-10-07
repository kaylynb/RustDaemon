using RustDaemon.Targeting;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;

namespace RustDaemon.Test;

public sealed class CameraFramesTests
{
	private const float YawRadians = MathF.PI / 2f; // 90 degrees
	private const float PitchRadians = MathF.PI / 4f; // 45 degrees, raw (down-positive)

	[Fact]
	public void From_NullRotation_ReturnsNull()
	{
		Assert.Null(CameraRotation.From((Vector3?)null));
	}

	[Fact]
	public async Task ReadAsync_ConvertsTheFramesRotationToDegreesWithYawFirst()
	{
		var camera = new InMemoryCompanionCamera();
		using var frames = CameraFrames.Subscribe(camera);

		// The frame reports X = pitch (raw down-positive) and Y = yaw, in radians.
		camera.RaiseFrame(
			new CameraRaysEventArg
			{
				CameraRotation = new Vector3 { X = PitchRadians, Y = YawRadians },
			}
		);

		var rotation = await frames.ReadAsync(TimeSpan.FromSeconds(1), CancellationToken.None);

		Assert.NotNull(rotation);
		Assert.Equal(90.0, rotation!.YawDegrees, 3);
		Assert.Equal(-45.0, rotation.PitchDegrees, 3);
	}

	[Fact]
	public void From_FlipsPitchToUpPositiveAndCanonicalizesBothAxes()
	{
		// Raw Unity euler: X = 315 degrees (down-positive) is 45 degrees up; Y = 200 degrees is -160.
		var rotation = CameraRotation.From(new Vector3 { X = 315f * (MathF.PI / 180f), Y = 200f * (MathF.PI / 180f) });

		Assert.NotNull(rotation);
		Assert.Equal(-160.0, rotation!.YawDegrees, 3);
		Assert.Equal(45.0, rotation.PitchDegrees, 3);
	}

	[Fact]
	public async Task ReadAsync_SkipsFramesWithoutARotation()
	{
		var camera = new InMemoryCompanionCamera();
		using var frames = CameraFrames.Subscribe(camera);

		camera.RaiseFrame(new CameraRaysEventArg());
		camera.RaiseFrame(new CameraRaysEventArg { CameraRotation = null });

		Assert.Null(await frames.ReadAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
	}

	[Fact]
	public async Task ReadAsync_CallerCancellationThrows()
	{
		var camera = new InMemoryCompanionCamera();
		using var frames = CameraFrames.Subscribe(camera);
		using var cancellation = new CancellationTokenSource();

		var read = frames.ReadAsync(TimeSpan.FromSeconds(5), cancellation.Token);
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
	}

	[Fact]
	public async Task Dispose_DetachesTheHandler()
	{
		var camera = new InMemoryCompanionCamera();
		var frames = CameraFrames.Subscribe(camera);
		frames.Dispose();

		// No handler is attached, so nothing is buffered and a read times out.
		camera.RaiseFrame(new CameraRaysEventArg { CameraRotation = new Vector3 { Y = 1f } });
		Assert.Null(await frames.ReadAsync(TimeSpan.FromMilliseconds(50), CancellationToken.None));
	}
}
