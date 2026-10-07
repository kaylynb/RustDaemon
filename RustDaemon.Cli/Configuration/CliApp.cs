using System.CommandLine;
using System.CommandLine.Parsing;
using System.Globalization;
using RustDaemon.Cli.Lifecycle;
using RustDaemon.Cli.Reporting;
using RustDaemon.Cli.Targeting;
using RustDaemon.Configuration;

namespace RustDaemon.Cli.Configuration;

public static class CliApp
{
	public static RootCommand BuildRootCommand() =>
		new("Rust+ daemon") { BuildWatchCommand(), BuildReportCommand(), BuildTurretCommand() };

	private static Command BuildWatchCommand()
	{
		var cameraOption = new Option<string>("--camera")
		{
			Description = "Camera identifier to watch.",
			Required = true,
		};
		var credentialsOption = CredentialsOption();
		var logOption = new Option<string>("--log")
		{
			Description = "Path to the append-only event log.",
			DefaultValueFactory = _ => DefaultPath("seen-players.jsonl"),
		};
		var proxyOption = ProxyOption();

		var command = new Command("watch", "Watch a camera and append player sightings to the event log.")
		{
			cameraOption,
			credentialsOption,
			logOption,
			proxyOption,
		};
		command.SetAction(
			async (parseResult, _) =>
			{
				var options = new DaemonOptions(
					parseResult.GetRequiredValue(credentialsOption),
					parseResult.GetRequiredValue(cameraOption).Trim(),
					Path.GetFullPath(parseResult.GetRequiredValue(logOption)),
					parseResult.GetValue(proxyOption),
					TimeSpan.FromSeconds(2)
				);
				return await DaemonCommand.RunAsync(options);
			}
		);
		return command;
	}

	private static Command BuildReportCommand()
	{
		var logOption = new Option<string>("--log")
		{
			Description = "Path to the event log to project.",
			Required = true,
		};
		var timeZoneOption = new Option<string>("--timezone")
		{
			Description = "Display time zone: 'local' or a host time zone ID.",
			DefaultValueFactory = _ => "local",
		};
		var jsonOption = new Option<bool>("--json") { Description = "Emit a JSON report instead of text." };

		var command = new Command("report", "Project the event log into appearance intervals.")
		{
			logOption,
			timeZoneOption,
			jsonOption,
		};
		command.SetAction(
			async (parseResult, _) =>
				await ReportCommand.RunAsync(
					new ReportOptions(
						Path.GetFullPath(parseResult.GetRequiredValue(logOption)),
						parseResult.GetValue(timeZoneOption) ?? "local",
						parseResult.GetValue(jsonOption)
					)
				)
		);
		return command;
	}

	private static Command BuildTurretCommand()
	{
		var command = new Command("turret", "Aim and fire a controllable Rust+ auto-turret.");
		command.Subcommands.Add(BuildObserveCommand());
		command.Subcommands.Add(BuildCalibrateCommand());
		command.Subcommands.Add(BuildFireCommand());
		return command;
	}

	private static Command BuildCalibrateCommand()
	{
		var cameraOption = new Option<string>("--camera")
		{
			Description = "Turret identifier to probe.",
			Required = true,
		};
		var credentialsOption = CredentialsOption();
		var proxyOption = ProxyOption();

		var command = new Command("calibrate", "Probe the turret's aim response and print the measured mapping.")
		{
			cameraOption,
			credentialsOption,
			proxyOption,
		};
		command.SetAction(
			async (parseResult, _) =>
				await CalibrateCommand.RunAsync(
					new CalibrateOptions(
						parseResult.GetRequiredValue(cameraOption),
						parseResult.GetRequiredValue(credentialsOption),
						parseResult.GetValue(proxyOption)
					)
				)
		);
		return command;
	}

	private static Command BuildFireCommand()
	{
		var cameraOption = new Option<string>("--camera")
		{
			Description = "Turret identifier to aim and fire.",
			Required = true,
		};
		var targetsOption = new Option<string>("--targets")
		{
			Description = "Targets CSV: one 'yaw,pitch' pair per line, in degrees (yaw -180..180, pitch -45..75).",
			Required = true,
		};
		var magazineOption = new Option<int>("--magazine")
		{
			Description = "Shots per magazine before reloading (at least 1).",
			DefaultValueFactory = _ => 1,
			CustomParser = result =>
			{
				var text = result.Tokens.Count == 1 ? result.Tokens[0].Value : null;
				if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var shots) || shots < 1)
				{
					result.AddError("--magazine must be a whole number of at least 1.");
					return 1;
				}

				return shots;
			},
		};
		var credentialsOption = CredentialsOption();
		var proxyOption = ProxyOption();

		var command = new Command("fire", "Aim at each target and fire once.")
		{
			cameraOption,
			targetsOption,
			magazineOption,
			credentialsOption,
			proxyOption,
		};
		command.SetAction(
			async (parseResult, _) =>
				await FireCommand.RunAsync(
					new FireOptions(
						parseResult.GetRequiredValue(cameraOption),
						Path.GetFullPath(parseResult.GetRequiredValue(targetsOption)),
						parseResult.GetRequiredValue(credentialsOption),
						parseResult.GetValue(proxyOption),
						parseResult.GetValue(magazineOption)
					)
				)
		);
		return command;
	}

	private static Command BuildObserveCommand()
	{
		var cameraOption = new Option<string>("--camera")
		{
			Description = "Camera/turret identifier to observe.",
			Required = true,
		};
		var credentialsOption = CredentialsOption();
		var proxyOption = ProxyOption();

		var command = new Command("observe", "Print the turret's live aim as yaw,pitch CSV.")
		{
			cameraOption,
			credentialsOption,
			proxyOption,
		};
		command.SetAction(
			async (parseResult, _) =>
				await ObserveCommand.RunAsync(
					new ObserveOptions(
						parseResult.GetRequiredValue(cameraOption),
						parseResult.GetRequiredValue(credentialsOption),
						parseResult.GetValue(proxyOption)
					)
				)
		);
		return command;
	}

	private static Option<string> CredentialsOption() =>
		new("--credentials")
		{
			Description = "Path to the Rust+ credentials file.",
			DefaultValueFactory = _ => DefaultPath("credentials.json"),
		};

	private static Option<bool> ProxyOption() =>
		new("--proxy") { Description = "Route the Rust+ WebSocket through the Facepunch proxy." };

	private static double? ParseSeconds(ArgumentResult result, string errorMessage)
	{
		var text = result.Tokens.Count == 1 ? result.Tokens[0].Value : null;
		if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
		{
			result.AddError(errorMessage);
			return null;
		}

		return seconds;
	}

	private static string DefaultPath(string fileName)
	{
		// TODO: this has a race condition but for now it's fine
		var besideExecutable = Path.Combine(AppContext.BaseDirectory, fileName);
		return File.Exists(besideExecutable) ? besideExecutable : Path.Combine(Environment.CurrentDirectory, fileName);
	}
}
