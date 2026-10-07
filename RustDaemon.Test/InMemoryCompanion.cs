using RustDaemon.Companion;
using RustDaemon.Configuration;
using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;

namespace RustDaemon.Test;

internal sealed class InMemoryCompanionCamera(string cameraId = "CAM01", bool isAutoTurret = true) : ICompanionCamera
{
	public string CameraId { get; } = cameraId;

	public bool IsAutoTurret { get; } = isAutoTurret;

	public CameraControlFlags ControlFlags { get; } = CameraControlFlags.Mouse;

	public event EventHandler<CameraRaysEventArg>? FrameReceived;

	public event EventHandler<ErrorMessage>? KeepAliveFailed;

	public bool Disposed { get; private set; }

	public void RaiseFrame(CameraRaysEventArg frame) => FrameReceived?.Invoke(this, frame);

	public void RaiseKeepAliveFailed(ErrorMessage error) => KeepAliveFailed?.Invoke(this, error);

	public Task<Response> LookAsync(float mouseX, float mouseY, CancellationToken cancellationToken) =>
		Task.FromResult(Success());

	public Task<Response> ReloadAsync(CancellationToken cancellationToken) => Task.FromResult(Success());

	public Task<Response> SendInputAsync(CameraButtons buttons, CancellationToken cancellationToken) =>
		Task.FromResult(Success());

	public ValueTask DisposeAsync()
	{
		Disposed = true;
		return ValueTask.CompletedTask;
	}

	private static Response Success() => new() { IsSuccess = true };
}

internal sealed class InMemoryCompanionConnection(Func<ICompanionCamera> cameraFactory) : ICompanionConnection
{
	public event EventHandler? TransportLost;

	public bool IsConnected { get; private set; }

	public int ConnectCount { get; private set; }

	public ICompanionCamera? Current { get; private set; }

	public ValueTask<ICompanionCamera> ConnectCameraAsync(string cameraId, CancellationToken cancellationToken)
	{
		Current = cameraFactory();
		++ConnectCount;
		IsConnected = true;
		return ValueTask.FromResult(Current);
	}

	public async Task<bool> WaitForConnectAsync(int count, TimeSpan timeout)
	{
		var deadline = DateTimeOffset.UtcNow + timeout;
		while (ConnectCount < count)
		{
			if (DateTimeOffset.UtcNow >= deadline)
			{
				return false;
			}

			await Task.Delay(5);
		}

		return true;
	}

	public void LoseTransport()
	{
		IsConnected = false;
		TransportLost?.Invoke(this, EventArgs.Empty);
	}

	public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal sealed class InMemoryCompanionConnectionFactory(ICompanionConnection connection) : ICompanionConnectionFactory
{
	public ICompanionConnection Create(CredentialsDocument credentials, bool useProxy) => connection;
}
