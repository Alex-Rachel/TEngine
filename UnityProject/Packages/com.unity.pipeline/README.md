# Unity Pipeline Package

[![Unity Version](https://img.shields.io/badge/Unity-6.0%2B-blue.svg)](https://unity3d.com/unity/whats-new/2023.3.0)

Transform Unity Editor into a programmable automation element for CI/CD pipelines and development workflows. The package exposes a running Unity Editor (or development Player) over a local HTTP API so external tools, scripts, or agents can execute commands remotely.

## Install the Unity CLI

The Pipeline package is driven through the `unity` CLI.

macOS / Linux
```
curl -fsSL https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.sh | UNITY_CLI_CHANNEL=beta bash
```

Windows (PowerShell)
```
$env:UNITY_CLI_CHANNEL='beta'; irm https://public-cdn.cloud.unity3d.com/hub/prod/cli/install.ps1 | iex
```

For more details, see the [CLI documentation](https://github.com/Unity-Technologies/unity-hub/tree/dev/src/cli).

## Install the Pipeline package

Install the package into a Unity project with:

```bash
unity pipeline install
```

By default it targets the current directory (or the running Unity instance). Use `--project-path` to target a specific project:

```bash
unity pipeline install --project-path /path/to/your/unity/project
```

Then open the project in the Unity Editor. The package automatically starts its HTTP server and registers the built-in commands.

## Connect to a running Editor

Run `unity command` with no group or command name to connect to a Unity instance and list its command groups:

```bash
# Auto-discover a Unity instance from the current directory and list its command groups
unity command

# Connect to a specific project
unity command --project-path /path/to/your/unity/project
```

Commands are organized in groups. Run one with `unity command <group> <name>`, for example `unity command editor status`, and list a group's commands with `unity command <group>`. For a list of command groups, refer to [Command reference](Documentation~/command-reference.md).

Click [here](Documentation~/connectivity.md) for more details on Connectivity troubleshooting.

## Connect to a running Player (Runtime)

To target a running development Player instead of the Editor, use `--runtime` (by process name) or `--runtime-path` (by the location of the runtime port file). These options go **after** `command` and **before** the group.

```bash
# By Player process/executable name
unity command --runtime MyGame.exe runtime status

# By the path where the runtime port file is located
unity command --runtime-path <path> runtime status
```

`--runtime-path` points to where the `.unity-pipeline-runtime-port` file lives:

- **Windows** — the port file sits next to the Player executable:

  ```bash
  unity command --runtime-path "C:\Builds\MyGame" runtime status
  ```

- **macOS** — pass the path to the `.app` bundle:

  ```bash
  unity command --runtime-path "/Users/me/Builds/MyGame.app" runtime status
  ```

Click [here](Documentation~/connectivity.md) for more details on Connectivity troubleshooting.

## Documentation

For the full command reference, connectivity details, runtime setup, and method-reload guides, see the [Pipeline documentation](Documentation~/index.md).

## License

com.unity.pipeline is made available under the [Unity Terms of Service](https://unity.com/legal/terms-of-service). See [LICENSE.md](LICENSE.md) for more legal information.

## Contributions to this repository

We are not accepting pull requests at this time. If you find an issue with the package or would like to request a new feature, please submit a [GitHub issue](https://github.com/Unity-Technologies/com.unity.pipeline/issues).
