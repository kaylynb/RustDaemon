namespace RustDaemon.Targeting;

public sealed record AimLook(float MouseDeltaX, float MouseDeltaY);

public sealed record AimReload;

public sealed record AimFire;

public sealed record AimWait;

public sealed record AimTimedOut;

public sealed record AimComplete;

public union AimAction(AimLook, AimReload, AimFire, AimWait, AimTimedOut, AimComplete)
{
	public static readonly AimAction Wait = new AimWait();
	public static readonly AimAction Reload = new AimReload();
	public static readonly AimAction Fire = new AimFire();
	public static readonly AimAction TimedOut = new AimTimedOut();
	public static readonly AimAction Complete = new AimComplete();

	public static AimAction Look(float mouseDeltaX, float mouseDeltaY) => new AimLook(mouseDeltaX, mouseDeltaY);
}

public sealed record TurretAimOptions(
	double ToleranceDegrees,
	int SettleFrames,
	double MovementThresholdDegrees,
	TimeSpan MinSettleDelay,
	TimeSpan ShotSpacing,
	double MaxMouseUnitsPerStep,
	TimeSpan AimTimeout,
	TimeSpan ReloadRequestDelay,
	TimeSpan ReloadCooldown,
	int MagazineSize
)
{
	public static readonly TurretAimOptions Default = new(
		ToleranceDegrees: 1.0,
		SettleFrames: RotationSettle.DefaultSettleFrames,
		MovementThresholdDegrees: RotationSettle.DefaultMovementThresholdDegrees,
		MinSettleDelay: TimeSpan.FromMilliseconds(300),
		ShotSpacing: TimeSpan.FromSeconds(1.0),
		MaxMouseUnitsPerStep: 25,
		AimTimeout: TimeSpan.FromSeconds(20),
		ReloadRequestDelay: TimeSpan.FromSeconds(0.8),
		ReloadCooldown: TimeSpan.FromSeconds(2.1),
		MagazineSize: 1
	);
}

public sealed class AimSession(IReadOnlyList<FireTarget> targets, AimMapping mapping, TurretAimOptions options)
{
	private readonly RotationSettle _settle = new(options.MovementThresholdDegrees, options.SettleFrames);

	private int _index;
	private DateTimeOffset? _lastLookAt;
	private bool _reloadPending = true;
	private DateTimeOffset _reloadNotBefore = DateTimeOffset.MinValue;
	private DateTimeOffset _reloadReadyAt = DateTimeOffset.MinValue;
	private DateTimeOffset? _lastShotAt;
	private DateTimeOffset _targetStartedAt = DateTimeOffset.MinValue;
	private bool _started;
	private bool _awaitingShot;
	private int _shotsInMagazine = options.MagazineSize;

	public bool IsComplete { get; private set; }

	public bool IsFaulted { get; private set; }

	public int ShotsFired { get; private set; }

	public int TargetIndex => IsComplete ? targets.Count : _index;

	public int TargetCount => targets.Count;

	public AimAction Step(CameraRotation rotation, DateTimeOffset now)
	{
		if (IsComplete || IsFaulted)
		{
			return AimAction.Complete;
		}

		if (!_started)
		{
			_started = true;
			_targetStartedAt = now;
		}

		if (_awaitingShot)
		{
			return AimAction.Wait;
		}

		var yaw = rotation.YawDegrees;
		var pitch = rotation.PitchDegrees;
		var settled = _settle.Observe(yaw, pitch);

		if (_reloadPending && now >= _reloadNotBefore)
		{
			_reloadPending = false;
			_reloadReadyAt = now + options.ReloadCooldown;
			_settle.Reset();
			return AimAction.Reload;
		}

		if (!settled || _lastLookAt is { } lastLookAt && now - lastLookAt < options.MinSettleDelay)
		{
			return TimedOut(now) ? AimAction.TimedOut : AimAction.Wait;
		}

		var target = targets[_index];
		var yawError = Angles.Wrap180(target.YawDegrees - yaw);
		var pitchError = target.PitchDegrees - pitch;

		if (Math.Abs(yawError) <= options.ToleranceDegrees && Math.Abs(pitchError) <= options.ToleranceDegrees)
		{
			if (now < _reloadReadyAt || _lastShotAt is { } lastShot && now - lastShot < options.ShotSpacing)
			{
				return TimedOut(now) ? AimAction.TimedOut : AimAction.Wait;
			}

			_awaitingShot = true;
			return AimAction.Fire;
		}

		if (TimedOut(now))
		{
			return AimAction.TimedOut;
		}

		if (!mapping.TryInvert(yawError, pitchError, out var mouseX, out var mouseY))
		{
			return AimAction.Wait;
		}

		_settle.Reset();
		_lastLookAt = now;
		return AimAction.Look(
			(float)Math.Clamp(mouseX, -options.MaxMouseUnitsPerStep, options.MaxMouseUnitsPerStep),
			(float)Math.Clamp(mouseY, -options.MaxMouseUnitsPerStep, options.MaxMouseUnitsPerStep)
		);
	}

	public void ConfirmShot(bool fired, DateTimeOffset now)
	{
		if (!_awaitingShot)
		{
			return;
		}

		_awaitingShot = false;
		if (!fired)
		{
			IsFaulted = true;
			return;
		}

		++_index;
		++ShotsFired;
		--_shotsInMagazine;
		_lastShotAt = now;
		_settle.Reset();
		_lastLookAt = null;
		_targetStartedAt = now;

		if (_index >= targets.Count)
		{
			IsComplete = true;
			return;
		}

		if (_shotsInMagazine <= 0)
		{
			_shotsInMagazine = options.MagazineSize;
			_reloadPending = true;
			_reloadNotBefore = now + options.ReloadRequestDelay;
		}
	}

	private bool TimedOut(DateTimeOffset now) => now - _targetStartedAt > options.AimTimeout;
}
