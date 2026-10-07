using Microsoft.Extensions.Logging;
using Polly;
using RustDaemon.Companion;
using RustDaemon.Monitoring;
using RustPlusApi.Data;
using RustPlusApi.Data.Events;

namespace RustDaemon.Lifecycle;

public sealed class DaemonRunner(
	ICompanionConnection connection,
	CameraMonitor monitor,
	ILogger<DaemonRunner> logger,
	string cameraId
) : IAsyncDisposable
{
	private readonly ResiliencePipeline _connectionPipeline = CompanionRetry.Infinite(logger);
	private readonly Lock _signalLock = new();
	private TaskCompletionSource<bool> _transportSignal = NewSignal();
	private ICompanionCamera? _camera;
	private EventHandler<CameraRaysEventArg>? _frameHandler;
	private EventHandler<ErrorMessage>? _keepAliveHandler;
	private string? _lastCameraFailure;
	private bool _disposed;

	public async Task RunAsync(CancellationToken cancellationToken)
	{
		connection.TransportLost += OnTransportLost;
		await monitor.StartAsync();

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				ResetTransportSignal();

				ICompanionCamera camera;
				try
				{
					camera = await _connectionPipeline.ExecuteAsync(
						async token => await connection.ConnectCameraAsync(cameraId, token),
						cancellationToken
					);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					break;
				}

				_camera = camera;
				var sourceSessionId = Guid.NewGuid();
				await monitor.BeginSessionAsync(sourceSessionId, cameraId, cancellationToken);
				AttachCameraHandlers(camera, sourceSessionId);
				_lastCameraFailure = null;
				logger.LogInformation(
					"Subscribed to camera {CameraId}; session {SessionId}",
					cameraId,
					sourceSessionId
				);

				try
				{
					await WaitForTransportSignalAsync(cancellationToken);
				}
				catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
				{
					// Normal shutdown.
				}

				await StopCameraSessionAsync(CancellationToken.None);
			}
		}
		finally
		{
			await StopCameraSessionAsync(CancellationToken.None);
			connection.TransportLost -= OnTransportLost;
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		await StopCameraSessionAsync(CancellationToken.None);
	}

	private void AttachCameraHandlers(ICompanionCamera camera, Guid sourceSessionId)
	{
		_frameHandler = (_, frame) =>
		{
			_lastCameraFailure = null;
			monitor.EnqueueFrame(sourceSessionId, frame);
		};
		_keepAliveHandler = (_, error) => OnKeepAliveFailed(error);
		camera.FrameReceived += _frameHandler;
		camera.KeepAliveFailed += _keepAliveHandler;
	}

	private async Task StopCameraSessionAsync(CancellationToken cancellationToken)
	{
		var camera = _camera;
		if (camera is null)
		{
			return;
		}

		_camera = null;
		if (_frameHandler is not null)
		{
			camera.FrameReceived -= _frameHandler;
		}

		if (_keepAliveHandler is not null)
		{
			camera.KeepAliveFailed -= _keepAliveHandler;
		}

		_frameHandler = null;
		_keepAliveHandler = null;
		await monitor.EndSessionAsync(cancellationToken);
		await camera.DisposeAsync();
		logger.LogInformation("Camera session for {CameraId} stopped", cameraId);
	}

	private void OnKeepAliveFailed(ErrorMessage error)
	{
		if (!connection.IsConnected && error.Code == RustPlusErrorCode.Unknown)
		{
			SignalTransport();
			return;
		}

		LogCameraFailureOnce("renewal", error);
	}

	private void OnTransportLost(object? sender, EventArgs eventArgs)
	{
		logger.LogWarning("Rust+ companion transport lost");
		SignalTransport();
	}

	private void SignalTransport()
	{
		lock (_signalLock)
		{
			_transportSignal.TrySetResult(true);
		}
	}

	private async Task WaitForTransportSignalAsync(CancellationToken cancellationToken)
	{
		Task signal;
		lock (_signalLock)
		{
			signal = _transportSignal.Task;
		}

		await signal.WaitAsync(cancellationToken);
		lock (_signalLock)
		{
			if (_transportSignal.Task.IsCompleted)
			{
				_transportSignal = NewSignal();
			}
		}
	}

	private void ResetTransportSignal()
	{
		lock (_signalLock)
		{
			if (_transportSignal.Task.IsCompleted)
			{
				_transportSignal = NewSignal();
			}
		}
	}

	private void LogCameraFailureOnce(string action, ErrorMessage? error)
	{
		if (error is null)
		{
			logger.LogWarning("Camera {Action} failed for {CameraId} without an error", action, cameraId);
			return;
		}

		var failure = $"{error.Code}:{error.Message}";
		if (string.Equals(_lastCameraFailure, failure, StringComparison.Ordinal))
		{
			logger.LogDebug(
				"Camera {Action} still failing for {CameraId}: {Code} {Message}",
				action,
				cameraId,
				error.Code,
				error.Message
			);
			return;
		}

		_lastCameraFailure = failure;
		logger.LogWarning(
			"Camera {Action} failed for {CameraId}: {Code} {Message}",
			action,
			cameraId,
			error.Code,
			error.Message
		);
	}

	private static TaskCompletionSource<bool> NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
