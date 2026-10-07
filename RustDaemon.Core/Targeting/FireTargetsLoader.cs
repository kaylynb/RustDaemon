using System.Globalization;

namespace RustDaemon.Targeting;

public static class FireTargetsLoader
{
	public static async Task<IReadOnlyList<FireTarget>> LoadAsync(
		string path,
		CancellationToken cancellationToken = default
	)
	{
		string[] lines;
		try
		{
			lines = await File.ReadAllLinesAsync(path, cancellationToken);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			throw new InvalidOperationException(
				$"Could not read targets file '{path}': {exception.Message}",
				exception
			);
		}

		List<FireTarget> targets = [with(capacity: lines.Length)];
		for (var lineNumber = 0; lineNumber < lines.Length; ++lineNumber)
		{
			var line = lines[lineNumber].Trim();
			if (line.Length == 0 || line.StartsWith('#'))
			{
				continue;
			}

			var fields = line.Split(',', StringSplitOptions.TrimEntries);
			if (
				fields.Length < 2
				|| !double.TryParse(fields[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var yaw)
				|| !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pitch)
			)
			{
				throw new InvalidDataException(
					$"Targets file '{path}' line {lineNumber + 1} is not 'yaw,pitch': '{line}'."
				);
			}

			Validate(path, lineNumber + 1, line, yaw, pitch);
			targets.Add(new FireTarget(yaw, pitch));
		}

		return targets;
	}

	private static void Validate(string path, int lineNumber, string line, double yaw, double pitch)
	{
		if (!double.IsFinite(yaw) || !double.IsFinite(pitch))
		{
			throw new InvalidDataException(
				$"Targets file '{path}' line {lineNumber} has a non-finite coordinate: '{line}'."
			);
		}

		if (yaw is < -180.0 or >= 180.0)
		{
			throw new InvalidDataException(
				FormattableString.Invariant(
					$"Targets file '{path}' line {lineNumber} yaw {yaw} is outside [-180, 180): '{line}'."
				)
			);
		}

		if (!TurretLimits.IsReachablePitch(pitch))
		{
			throw new InvalidDataException(
				FormattableString.Invariant(
					$"Targets file '{path}' line {lineNumber} pitch {pitch} is outside the turret's reachable range [{TurretLimits.MinPitchDegrees}, {TurretLimits.MaxPitchDegrees}] degrees: '{line}'."
				)
			);
		}
	}
}
