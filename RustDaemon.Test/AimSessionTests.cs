using RustDaemon.Targeting;

namespace RustDaemon.Test;

public sealed class AimSessionTests
{
	private static readonly DateTimeOffset Epoch = DateTimeOffset.UnixEpoch;
	private static readonly AimMapping DefaultMapping = new(4, 0, 0, 4);

	[Fact]
	public void Step_BeforeFirstShot_RequestsReload()
	{
		var session = new AimSession([new FireTarget(0, 0)], DefaultMapping, Options());

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));
	}

	[Fact]
	public void Step_DoesNotFireUntilReloadCooldownElapses()
	{
		var session = new AimSession(
			[new FireTarget(0, 0)],
			DefaultMapping,
			Options(settleFrames: 2, reloadCooldown: TimeSpan.FromSeconds(2))
		);

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));
		Expect<AimWait>(session.Step(Rot(0, 0), Epoch + Ms(100)));
		Expect<AimWait>(session.Step(Rot(0, 0), Epoch + Ms(200)));

		Expect<AimFire>(session.Step(Rot(0, 0), Epoch + Ms(2_100)));

		// Confirm-on-success: the shot only counts once the driver reports it fired.
		Assert.Equal(0, session.ShotsFired);
		session.ConfirmShot(fired: true, now: Epoch + Ms(2_100));
		Assert.Equal(1, session.ShotsFired);
		Assert.True(session.IsComplete);
	}

	[Fact]
	public void ConfirmShot_FailedShot_KeepsProgressAtLastSuccess()
	{
		var session = new AimSession(
			[new FireTarget(0, 0)],
			DefaultMapping,
			Options(settleFrames: 1, reloadCooldown: TimeSpan.Zero)
		);

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));
		Expect<AimFire>(session.Step(Rot(0, 0), Epoch + Ms(50)));
		session.ConfirmShot(fired: false, now: Epoch + Ms(50));

		Assert.True(session.IsFaulted);
		Assert.Equal(0, session.ShotsFired);
		Assert.Equal(0, session.TargetIndex);
	}

	[Fact]
	public void Step_ReloadsDuringTheMoveBetweenTargets()
	{
		var session = new AimSession(
			[new FireTarget(0, 0), new FireTarget(0, 0)],
			DefaultMapping,
			Options(
				settleFrames: 1,
				shotSpacing: TimeSpan.FromSeconds(2),
				reloadRequestDelay: TimeSpan.FromSeconds(1),
				reloadCooldown: TimeSpan.FromSeconds(2)
			)
		);

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));
		Expect<AimWait>(session.Step(Rot(0, 0), Epoch + Ms(50)));
		Expect<AimFire>(session.Step(Rot(0, 0), Epoch + Ms(2_100)));
		session.ConfirmShot(fired: true, now: Epoch + Ms(2_100));

		Expect<AimWait>(session.Step(Rot(0, 0), Epoch + Ms(2_200)));
		Expect<AimReload>(session.Step(Rot(0, 0), Epoch + Ms(3_100)));
		Expect<AimFire>(session.Step(Rot(0, 0), Epoch + Ms(5_200)));
		session.ConfirmShot(fired: true, now: Epoch + Ms(5_200));

		Assert.Equal(2, session.ShotsFired);
		Assert.True(session.IsComplete);
	}

	[Fact]
	public void Step_WhileGunIsMoving_DoesNotIssueAnotherLook()
	{
		var session = new AimSession([new FireTarget(90, 0)], DefaultMapping, Options(settleFrames: 2));

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));
		Expect<AimWait>(session.Step(Rot(0, 0), Epoch + Ms(100)));
		Expect<AimLook>(session.Step(Rot(0, 0), Epoch + Ms(200)));

		// The gun is now traveling toward the command; no further correction may be stacked.
		Expect<AimWait>(session.Step(Rot(5, 0), Epoch + Ms(250)));
		Expect<AimWait>(session.Step(Rot(20, 0), Epoch + Ms(300)));
	}

	[Fact]
	public void Step_WrapsYawAcrossTheBoundary()
	{
		var session = new AimSession([new FireTarget(-10, 0)], DefaultMapping, Options(settleFrames: 1));

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));
		Expect<AimWait>(session.Step(Rot(10, 0), Epoch + Ms(50)));

		var look = Expect<AimLook>(session.Step(Rot(10, 0), Epoch + Ms(100)));

		Assert.Equal(-5f, look.MouseDeltaX, 3);
		Assert.Equal(0f, look.MouseDeltaY, 3);
	}

	[Fact]
	public void Step_MapsPitchErrorOntoThePitchAxis()
	{
		var session = new AimSession([new FireTarget(0, 8)], DefaultMapping, Options(settleFrames: 1));

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));

		var look = Expect<AimLook>(session.Step(Rot(0, 0), Epoch + Ms(50)));

		Assert.Equal(0f, look.MouseDeltaX, 3);
		Assert.Equal(2f, look.MouseDeltaY, 3);
	}

	[Fact]
	public void Step_WrapsYawAcrossTheSeam()
	{
		var session = new AimSession([new FireTarget(-170, 0)], DefaultMapping, Options(settleFrames: 1));

		Expect<AimReload>(session.Step(Rot(170, 0), Epoch));

		var look = Expect<AimLook>(session.Step(Rot(170, 0), Epoch + Ms(50)));

		// The short way from 170 to -170 is +20, so the correction is +5 mouse units.
		Assert.Equal(5f, look.MouseDeltaX, 3);
		Assert.Equal(0f, look.MouseDeltaY, 3);
	}

	[Fact]
	public void Step_HoldsAfterALookUntilMinSettleDelayElapses()
	{
		var session = new AimSession(
			[new FireTarget(8, 0)],
			DefaultMapping,
			Options(settleFrames: 1, minSettleDelay: TimeSpan.FromSeconds(1), reloadCooldown: TimeSpan.Zero)
		);

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));
		Expect<AimLook>(session.Step(Rot(0, 0), Epoch + Ms(50)));

		// Frames with movement, then stillness before the minimum delay has passed, must not re-correct.
		Expect<AimWait>(session.Step(Rot(2, 0), Epoch + Ms(150)));
		Expect<AimWait>(session.Step(Rot(8, 0), Epoch + Ms(250)));
		Expect<AimWait>(session.Step(Rot(8, 0), Epoch + Ms(350)));

		Expect<AimFire>(session.Step(Rot(8, 0), Epoch + Ms(1_100)));
	}

	[Fact]
	public void Step_UnreachableTarget_TimesOutAfterAimTimeout()
	{
		var session = new AimSession(
			[new FireTarget(180, 0)],
			DefaultMapping,
			Options(settleFrames: 1, reloadCooldown: TimeSpan.Zero, aimTimeout: TimeSpan.FromSeconds(1))
		);

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));
		Expect<AimLook>(session.Step(Rot(0, 0), Epoch + Ms(50)));

		Expect<AimTimedOut>(session.Step(Rot(0, 0), Epoch + Ms(1_500)));
	}

	[Fact]
	public void Step_ReloadsOnlyAfterAMagazineIsEmpty()
	{
		var session = new AimSession(
			[new FireTarget(0, 0), new FireTarget(0, 0), new FireTarget(0, 0), new FireTarget(0, 0)],
			DefaultMapping,
			Options(
				settleFrames: 1,
				shotSpacing: TimeSpan.Zero,
				reloadRequestDelay: TimeSpan.Zero,
				reloadCooldown: TimeSpan.Zero,
				magazine: 2
			)
		);

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));

		Expect<AimFire>(session.Step(Rot(0, 0), Epoch + Ms(10)));
		session.ConfirmShot(fired: true, now: Epoch + Ms(10));

		// Within the magazine there is no reload between shots.
		Expect<AimFire>(session.Step(Rot(0, 0), Epoch + Ms(20)));
		session.ConfirmShot(fired: true, now: Epoch + Ms(20));

		// The magazine is empty, so the next step requests a reload.
		Expect<AimReload>(session.Step(Rot(0, 0), Epoch + Ms(30)));
		Assert.Equal(2, session.ShotsFired);
	}

	[Fact]
	public void Step_NoReloadAfterTheFinalShot()
	{
		var session = new AimSession(
			[new FireTarget(0, 0)],
			DefaultMapping,
			Options(settleFrames: 1, reloadCooldown: TimeSpan.Zero, magazine: 3)
		);

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));
		Expect<AimFire>(session.Step(Rot(0, 0), Epoch + Ms(10)));
		session.ConfirmShot(fired: true, now: Epoch + Ms(10));

		Assert.True(session.IsComplete);
		Expect<AimComplete>(session.Step(Rot(0, 0), Epoch + Ms(20)));
	}

	[Fact]
	public void Step_UsesTheInjectedCrossCoupledMapping()
	{
		var mapping = new AimMapping(2, 1, 1, -2);
		var session = new AimSession(
			[new FireTarget(10, 4)],
			mapping,
			Options(settleFrames: 1, reloadCooldown: TimeSpan.Zero)
		);

		Expect<AimReload>(session.Step(Rot(0, 0), Epoch));

		var look = Expect<AimLook>(session.Step(Rot(0, 0), Epoch + Ms(50)));

		Assert.Equal(4.8f, look.MouseDeltaX, 3);
		Assert.Equal(0.4f, look.MouseDeltaY, 3);
	}

	[Theory]
	[InlineData(1, 4)]
	[InlineData(2, 2)]
	[InlineData(4, 1)]
	public void Step_MagazineSize_ControlsReloadCount(int magazine, int expectedReloads)
	{
		var session = new AimSession(
			Enumerable.Repeat(new FireTarget(0, 0), 4).ToArray(),
			DefaultMapping,
			Options(
				settleFrames: 1,
				shotSpacing: TimeSpan.Zero,
				reloadRequestDelay: TimeSpan.Zero,
				reloadCooldown: TimeSpan.Zero,
				magazine: magazine
			)
		);

		var reloads = 0;
		var now = Epoch;
		while (!session.IsComplete)
		{
			var action = session.Step(Rot(0, 0), now);
			switch (action)
			{
				case AimReload:
					++reloads;
					break;
				case AimFire:
					session.ConfirmShot(fired: true, now: now);
					break;
				case AimWait:
				case AimComplete:
					break;
				case AimLook:
				case AimTimedOut:
					Assert.Fail($"Unexpected action {action}");
					break;
			}

			now += Ms(1);
		}

		Assert.Equal(expectedReloads, reloads);
		Assert.Equal(4, session.ShotsFired);
	}

	private static T Expect<T>(AimAction action) => Assert.IsType<T>(action.Value);

	private static TurretAimOptions Options(
		double tolerance = 1.0,
		int settleFrames = 2,
		TimeSpan? minSettleDelay = null,
		TimeSpan? shotSpacing = null,
		TimeSpan? reloadRequestDelay = null,
		TimeSpan? reloadCooldown = null,
		TimeSpan? aimTimeout = null,
		int magazine = 1
	) =>
		new(
			ToleranceDegrees: tolerance,
			SettleFrames: settleFrames,
			MovementThresholdDegrees: 0.05,
			MinSettleDelay: minSettleDelay ?? TimeSpan.Zero,
			ShotSpacing: shotSpacing ?? TimeSpan.FromSeconds(2.1),
			MaxMouseUnitsPerStep: 25,
			AimTimeout: aimTimeout ?? TimeSpan.FromSeconds(20),
			ReloadRequestDelay: reloadRequestDelay ?? TimeSpan.Zero,
			ReloadCooldown: reloadCooldown ?? TimeSpan.FromSeconds(2.1),
			MagazineSize: magazine
		);

	private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

	private static CameraRotation Rot(double yawDegrees, double pitchDegrees) => new(yawDegrees, pitchDegrees);
}
