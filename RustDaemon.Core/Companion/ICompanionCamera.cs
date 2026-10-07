using RustPlusApi.Camera;
using RustPlusApi.Data;
using RustPlusApi.Data.Cameras;
using RustPlusApi.Data.Events;

namespace RustDaemon.Companion;

public interface ICompanionCamera : IAsyncDisposable
{
	bool IsAutoTurret { get; }

	CameraControlFlags ControlFlags { get; }

	event EventHandler<CameraRaysEventArg>? FrameReceived;

	event EventHandler<ErrorMessage>? KeepAliveFailed;

	Task<Response> LookAsync(float mouseX, float mouseY, CancellationToken cancellationToken);

	Task<Response> ReloadAsync(CancellationToken cancellationToken);

	Task<Response> SendInputAsync(CameraButtons buttons, CancellationToken cancellationToken);
}

internal sealed class CameraControllerCompanionCamera : ICompanionCamera
{
	private readonly CameraController _controller;

	public CameraControllerCompanionCamera(CameraController controller)
	{
		_controller = controller;
		_controller.OnFrameReceived += OnFrameReceived;
		_controller.OnKeepAliveFailed += OnKeepAliveFailed;
	}

	public string CameraId => _controller.CameraId;

	public bool IsAutoTurret => _controller.IsAutoTurret;

	public CameraControlFlags ControlFlags => _controller.Info.ControlFlags;

	public event EventHandler<CameraRaysEventArg>? FrameReceived;

	public event EventHandler<ErrorMessage>? KeepAliveFailed;

	public Task<Response> LookAsync(float mouseX, float mouseY, CancellationToken cancellationToken) =>
		_controller.LookAsync(mouseX, mouseY, cancellationToken);

	public Task<Response> ReloadAsync(CancellationToken cancellationToken) =>
		_controller.ReloadAsync(cancellationToken);

	public Task<Response> SendInputAsync(CameraButtons buttons, CancellationToken cancellationToken) =>
		_controller.SendInputAsync(buttons, cancellationToken: cancellationToken);

	public async ValueTask DisposeAsync()
	{
		_controller.OnFrameReceived -= OnFrameReceived;
		_controller.OnKeepAliveFailed -= OnKeepAliveFailed;
		await _controller.DisposeAsync();
	}

	private void OnFrameReceived(object? sender, CameraRaysEventArg frame) => FrameReceived?.Invoke(this, frame);

	private void OnKeepAliveFailed(object? sender, ErrorMessage error) => KeepAliveFailed?.Invoke(this, error);
}
