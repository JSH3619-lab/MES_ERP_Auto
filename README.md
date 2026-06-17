# UNIMES Automation PoC

Windows desktop UI automation PoC for BIZENTRO UNIMES.

This first version focuses on a safe bootstrap flow:

- Launch the UNIMES ClickOnce shortcut (`.appref-ms`)
- Find the UNIMES window
- Detect the login screen
- Fill the user id
- Either wait for manual password/login, or use a local config password
- Click the post-login `Continue` popup when present
- Confirm the main window
- Save screenshots and a UI Automation control dump
- Navigate to `기준정보 > 품목관리 > 품목정보관리`
- Read Part No values from `input_parts.csv`
- Enter each Part No into the `품목명` search field
- Execute 조회
- Export best-effort Grid values to CSV

No save, register, delete, confirm, approve, or apply workflow is implemented.

## Requirements

- Windows
- .NET 8 Windows Desktop Runtime or newer
- UNIMES client installed for the current user

No NuGet packages are required.

## Files

```text
appsettings.example.json
input_parts.example.csv
src/UnimesAutomation/UnimesAutomation.csproj
src/UnimesAutomation/Program.cs
src/UnimesAutomation/UnimesApp.cs
src/UnimesAutomation/UiDump.cs
src/UnimesAutomation/SafetyGuard.cs
src/UnimesAutomation/Models.cs
src/UnimesAutomation/LoggerSetup.cs
```

Runtime folders are created automatically:

```text
logs/
screenshots/
output/
```

## Configuration

Copy the example config if you want to override defaults:

```powershell
Copy-Item .\appsettings.example.json .\appsettings.json
```

Default launch target:

```text
C:\Users\RAMOS\AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Bizentro\UNIMES - 1 .appref-ms
```

Password handling:

- `manual`: the program fills the user id, focuses the password field, then waits for the user to log in.
- `config`: the program uses the local `password` value from `appsettings.json`.

Do not store a password in files unless your company policy allows it.

## Build

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj
```

## Run

Double-click:

```text
run_unimes_automation.cmd
```

The program first opens a Part No input dialog. Enter one Part No per line,
then click `시작`.

```powershell
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj
```

Create the working input file:

```powershell
Copy-Item .\input_parts.example.csv .\input_parts.csv
```

Then edit `input_parts.csv` and put the target Part No list in the `part_no`
column.

Use a custom config:

```powershell
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --config .\appsettings.json
```

Attach to an already running UNIMES instance:

```powershell
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --no-launch
```

Only dump the current UNIMES UI tree:

```powershell
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --dump-only --no-launch
```

## Output

- Run log: `logs/run_YYYYMMDD_HHMMSS.log`
- UI dump: `logs/ui_dump_YYYYMMDD_HHMMSS.txt`
- Screenshots: `screenshots/*.png`
- Part lookup CSV: `output/result_YYYYMMDD_HHMMSS.csv`

Current workflow:

```text
1. 품목정보관리 탭
2. 품목명에 Part No 입력
3. 조회
4. 품목별 BIN 정보 관리 탭
5. 품목 ID에 Part No 입력
6. 조회
```

This version is query-only. It does not click save/register/delete/apply style
buttons.

## Safety

The automation has a safety guard for button captions containing:

```text
저장, 등록, 삭제, 확정, 승인, 적용,
Save, Register, Delete, Confirm, Apply
```

Those buttons are blocked unless `saveEnabled` is explicitly true. The current
PoC does not implement any save workflow.
