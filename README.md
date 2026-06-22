# UNIMES Automation

BIZENTRO UNIMES Windows desktop app automation for item information and item-specific BIN setup.

The app uses Windows UI Automation, not image/OCR recognition. Screenshots and UI dumps are saved only for diagnostics.

## Current Workflow

1. Launch or attach to UNIMES.
2. Auto-login when credentials are configured.
3. Open the GUI for work scope, Part No input, safety state, progress, and logs.
4. Run `품목정보관리`, `품목별 BIN 정보 관리`, or `둘 다`.
5. Stop cooperatively from the GUI when needed.
6. Write one result xlsx and show a completion summary.

## Safety

Default behavior is safe:

- `dryRun=true`
- `saveEnabled=false`

Buttons containing save/delete/apply style keywords are blocked unless `saveEnabled=true`.
Use an explicit local ignored config, such as `appsettings.save-test.json`, only for intentional save tests.

## Build

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj -c Release
```

## Publish Single EXE

```powershell
dotnet publish .\src\UnimesAutomation\UnimesAutomation.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\dist
```

## Run

```powershell
.\run_unimes_automation.cmd
```

Or:

```powershell
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj
```

Useful modes:

```powershell
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --no-launch
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --dump-only --no-launch
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --config .\appsettings.json
```

## Configuration

Copy the example when local overrides are needed:

```powershell
Copy-Item .\appsettings.example.json .\appsettings.json
```

Passwords should stay out of git. The default config reads `UNIMES_PASSWORD` from the environment.

## Runtime Output

These folders are generated and ignored by git:

- `logs/`
- `screenshots/`
- `output/`
- `dist/`

Results are written to `output/result_<timestamp>.xlsx` with separate sheets for `품목정보관리` and `BIN 정보관리`.

## Documents

- [CLAUDE.md](CLAUDE.md): project rules and entry point
- [docs/STATUS.md](docs/STATUS.md): current status and known risks
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): code map and UI references
- [docs/CONFIG.md](docs/CONFIG.md): config keys
- [docs/TESTING.md](docs/TESTING.md): build/run/test checklist
