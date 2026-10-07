using Microsoft.Extensions.Logging;
using RustDaemon.Configuration;
using RustPlusApi;
using RustPlusApi.Camera;
using RustPlusApi.Data;
using RustPlusApi.Interfaces;

namespace RustDaemon.Companion;

public sealed class CompanionConnectionException(ErrorMessage? error)
	: Exception(error is null ? "Could not connect to the Rust+ companion server." : $"{error.Code}: {error.Message}")
{
	public ErrorMessage? Error { get; } = error;
}

public interface ICompanionConnection : IAsyncDisposable
{
	event EventHandler? TransportLost;

	bool IsConnected { get; }

	ValueTask<ICompanionCamera> ConnectCameraAsync(string cameraId, CancellationToken cancellationToken);
}

public interface ICompanionConnectionFactory
{
	ICompanionConnection Create(CredentialsDocument credentials, bool useProxy);
}

public sealed class CompanionConnectionFactory(ILoggerFactory loggerFactory) : ICompanionConnectionFactory
{
	public ICompanionConnection Create(CredentialsDocument credentials, bool useProxy) =>
		new CompanionConnection(
			new RustPlus(
				new RustPlusConnection(
					credentials.Ip,
					credentials.Port,
					credentials.PlayerId,
					credentials.PlayerToken,
					useProxy
				),
				loggerFactory: loggerFactory
			),
			CompanionConnection.ResubscribeInterval
		);
}

public sealed class CompanionConnection : ICompanionConnection
{
	public static readonly TimeSpan ResubscribeInterval = TimeSpan.FromSeconds(10);

	private readonly IRustPlus _rustPlus;
	private readonly TimeSpan _resubscribeInterval;
	private bool _reconnectRequired;
	private bool _disposed;

	public CompanionConnection(IRustPlus rustPlus, TimeSpan resubscribeInterval)
	{
		_rustPlus = rustPlus;
		_resubscribeInterval = resubscribeInterval;
		_rustPlus.Disconnected += OnDisconnected;
		_rustPlus.ErrorOccurred += OnErrorOccurred;
	}

	public event EventHandler? TransportLost;

	public bool IsConnected => _rustPlus.IsConnected;

	public async ValueTask<ICompanionCamera> ConnectCameraAsync(string cameraId, CancellationToken cancellationToken)
	{
		if (_reconnectRequired && _rustPlus.IsConnected)
		{
			await _rustPlus.DisconnectAsync(forceClose: true);
		}

		if (!_rustPlus.IsConnected)
		{
			await _rustPlus.ConnectAsync(cancellationToken);
		}

		_reconnectRequired = false;

		var response = await CameraController.SubscribeAsync(
			_rustPlus,
			cameraId,
			_resubscribeInterval,
			cancellationToken
		);

		if (response is not { IsSuccess: true, Data: not null })
		{
			throw new CompanionConnectionException(response.Error);
		}

		return new CameraControllerCompanionCamera(response.Data);
	}

	public async ValueTask DisposeAsync()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_rustPlus.Disconnected -= OnDisconnected;
		_rustPlus.ErrorOccurred -= OnErrorOccurred;
		try
		{
			if (_rustPlus.IsConnected)
			{
				await _rustPlus.DisconnectAsync(forceClose: true);
			}
		}
		finally
		{
			if (_rustPlus is IAsyncDisposable disposable)
			{
				await disposable.DisposeAsync();
			}
		}
	}

	private void OnDisconnected(object? sender, EventArgs eventArgs) => SignalTransportLost();

	private void OnErrorOccurred(object? sender, Exception exception) => SignalTransportLost();

	private void SignalTransportLost()
	{
		_reconnectRequired = true;
		TransportLost?.Invoke(this, EventArgs.Empty);
	}
}
