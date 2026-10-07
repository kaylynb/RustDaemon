namespace RustDaemon.Targeting;

public static class Angles
{
	public static double Wrap180(double degrees) => ((degrees + 180.0) % 360.0 + 360.0) % 360.0 - 180.0;

	public static double RadiansToDegrees(double radians) => radians * (180.0 / Math.PI);
}
