# UNIMES Automation

BIZENTRO UNIMES Windows desktop app automation for item information and item-specific BIN setup.

The app uses Windows UI Automation, not image/OCR recognition. Screenshots and UI dumps are saved only for diagnostics.

## Current Workflow

1. Launch or attach to UNIMES.
2. Auto-login when credentials are configured.
3. Ask for work scope: `품목정보관리만`, `BIN 정보 관리만`, or `둘 다`.
4. Ask for Part No values.
5. Run `품목정보관리`.
6. Run `품목별 BIN 정보 관리` for valid/selected parts.
7. Write result CSV and show a completion summary.

## Safety

Default behavior is safe:

- `dryRun=true`
- `saveEnabled=false`

Buttons containing save/delete/apply style keywords are blocked unless `saveEnabled=true`.
Use `run_unimes_automation_save_test.cmd` only for an intentional save test with a local ignored config.

## Build

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj
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

## Documents

- [CLAUDE.md](CLAUDE.md): project rules and entry point
- [docs/STATUS.md](docs/STATUS.md): current status and known risks
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md): code map and UI references
- [docs/CONFIG.md](docs/CONFIG.md): config keys
- [docs/TESTING.md](docs/TESTING.md): build/run/test checklist
