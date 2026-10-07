# RustDaemon

Rust+ cli tools.

> [!NOTE]
> This is an **unofficial, third-party** tool. It's not endorsed or approved by Facepunch.

## observe

Records events for every player seen by a camera.

## turret

Allows automatically firing a turret at predefined locations.

> [!CAUTION]
> This might be considered automation and I would recommend not using it in a real game, even though it is unlikely to be useful for anything PvP related. I wrote this to help test horse farm mechanics in my own private server.

## Prerequisites

- .NET 11 SDK.
- Rust+ credentials obtained through the RustPlusApi registration/sample workflow.
- A camera/turret identifier.

> [!IMPORTANT]
> The paired player must be offline & the turret must not be in peacekeeper mode.

SIGINT/Ctrl+C stops the daemon.

### Credentials

The daemon does not perform registration or pairing. It reads the same credential shape used by the
[RustPlusApi](https://github.com/HandyS11/RustPlusApi) samples:

For example:

```
Paired! Use these arguments:
  new RustPlus(new RustPlusConnection("12.34.56.78", 28082, 11111111000000000, 123456789));
```

```json
{
  "ip": "12.34.56.78",
  "port": 28082,
  "playerId": 11111111000000000,
  "playerToken": 123456789
}
```

## Watching a camera and reporting on players seen

> [!IMPORTANT]
> The paired player must be offline to connect to the camera. You can leave the daemon running while your player is online and it will resume recording when you logout.

```text
dotnet run --project RustDaemon.Cli -- watch --camera CAM01
```

The default credential filename is `credentials.json`, and the default event-log filename is `seen-players.jsonl`.

### Event Log and Reports

The event log can be turned into a more readable report using the `report` command:

```text
dotnet run --project RustDaemon.Cli -- report --log C:\path\seen-players.jsonl
dotnet run --project RustDaemon.Cli -- report --log C:\path\seen-players.jsonl --timezone America/Los_Angeles
```

## Controlling a turret

### Capture aim targets

Run the Rust+ app first so it holds control, then run the read-only `observe` command to log the turret's live rotation. stdout is plain `yaw,pitch` CSV in degrees so aim the app at each target and copy the pairs straight into a targets file:

```text
dotnet run --project RustDaemon.Cli -- turret observe --camera TURRET01
-18.796,-4.512
```

`observe` never sends input. If it subscribes before the app it becomes the controller and the app can no longer aim, so start the app first.

### Firing at targets

Create a targets file with one `yaw,pitch` pair per line (which you can get from observe).

```text
dotnet run --project RustDaemon.Cli -- turret fire --camera TURRET01 --targets targets.csv
```

`fire` connects and aims using the fixed aim mapping, then fires once at each target in order. A
reload is requested at startup and after every `--magazine` shots. It exits when the list is done.

## Building/Testing

Tools and tasks are managed with [mise](https://mise.jdx.dev). After `mise install`:

```text
mise run build
mise run test
mise run ci      # build then test, as CI does
```
