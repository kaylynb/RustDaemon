using System.Threading.Channels;
using RustDaemon.Companion;
using RustPlusApi.Data.Events;

namespace RustDaemon.Targeting;

public sealed class CameraFrames : IDisposable
{
	private const int FrameCapacity = 64;

	private readonly ICompanionCamera _camera;
	private readonly EventHandler<CameraRaysEventArg> _handler;
	private readonly Channel<CameraRotation> _frames;

	private CameraFrames(ICompanionCamera camera)
	{
		_camera = camera;
		_frames = Channel.CreateBounded<CameraRotation>(
			new BoundedChannelOptions(FrameCapacity)
			{
				FullMode = BoundedChannelFullMode.DropOldest,
				SingleReader = true,
			}
		);
		_handler = (_, frame) =>
		{
			if (CameraRotation.From(frame) is { } rotation)
			{
				_frames.Writer.TryWrite(rotation);
			}
		};
		camera.FrameReceived += _handler;
	}

	public static CameraFrames Subscribe(ICompanionCamera camera) => new(camera);

	public async Task<CameraRotation?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		linked.CancelAfter(timeout);
		try
		{
			return await _frames.Reader.ReadAsync(linked.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			return null;
		}
	}

	public void Dispose() => _camera.FrameReceived -= _handler;
}
