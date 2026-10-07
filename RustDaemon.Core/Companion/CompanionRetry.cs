using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace RustDaemon.Companion;

public static class CompanionRetry
{
	// For the long-lived daemon: retry forever with exponential backoff and jitter.
	public static ResiliencePipeline Infinite(ILogger logger) =>
		Build(
			logger,
			maxRetryAttempts: int.MaxValue,
			delay: TimeSpan.FromSeconds(5),
			maxDelay: TimeSpan.FromMinutes(5)
		);

	// For one-shot turret commands: give up after a few tries.
	public static ResiliencePipeline Bounded(ILogger logger) =>
		Build(logger, maxRetryAttempts: 4, delay: TimeSpan.FromSeconds(1), maxDelay: TimeSpan.FromSeconds(30));

	private static ResiliencePipeline Build(ILogger logger, int maxRetryAttempts, TimeSpan delay, TimeSpan maxDelay) =>
		new ResiliencePipelineBuilder()
			.AddRetry(
				new RetryStrategyOptions
				{
					BackoffType = DelayBackoffType.Exponential,
					Delay = delay,
					MaxDelay = maxDelay,
					MaxRetryAttempts = maxRetryAttempts,
					UseJitter = true,
					ShouldHandle = new PredicateBuilder().Handle<Exception>(exception =>
						exception is not OperationCanceledException
					),
					OnRetry = arguments =>
					{
						logger.LogWarning(
							arguments.Outcome.Exception,
							"Companion connection attempt failed; retrying in {Delay}",
							arguments.RetryDelay
						);
						return default;
					},
				}
			)
			.Build();
}
