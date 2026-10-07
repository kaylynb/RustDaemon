namespace RustDaemon.Configuration;

public sealed record DaemonOptions(
	string CredentialsPath,
	string CameraId,
	string LogPath,
	bool UseProxy,
	TimeSpan AbsenceGap
);
