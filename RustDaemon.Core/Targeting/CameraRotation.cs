using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;

namespace RustDaemon.Targeting;

public sealed record CameraRotation(double YawDegrees, double PitchDegrees)
{
	public static CameraRotation? From(Vector3? rotation) =>
		rotation is null
			? null
			: new CameraRotation(
				Angles.Wrap180(Angles.RadiansToDegrees(rotation.Y)),
				Angles.Wrap180(-Angles.RadiansToDegrees(rotation.X))
			);

	public static CameraRotation? From(CameraRaysEventArg frame) => From(frame.CameraRotation);
}
