# SignalUI

`.NET` support is configured from the repo root.

## Requirements

- .NET SDK `9.0.203` or a newer `9.0.x` feature band
- The solution targets `net8.0`

## CLI

Set Avalonia telemetry opt-out in restricted environments before building:

```bash
export AVALONIA_TELEMETRY_OPTOUT=1
dotnet restore singalUI.sln
dotnet build singalUI.sln
dotnet run --project singalUI/singalUI.csproj
```

## VS Code

The repo now includes:

- `.vscode/tasks.json` for `restore`, `build`, and `run`
- `.vscode/launch.json` for debugging `singalUI`
- `.vscode/settings.json` pointing VS Code at `singalUI.sln`

Recommended extensions are listed in `.vscode/extensions.json`.
