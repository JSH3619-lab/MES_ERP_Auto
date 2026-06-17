# UNIMES 자동화 (MES_ERP_Auto) — 작업 필수 안내

BIZENTRO **UNIMES** Windows 데스크톱 앱을 UI Automation(System.Windows.Automation)으로
조작하는 .NET 8 / C# PoC. `품목정보관리` 화면에서 파트 목록을 받아 조회하고 BIN/불량창고
등 셀 값을 맞추는 자동화다.

> 이 파일은 **반드시 알아야 할 것만** 담는다. 상세는 각 서브 문서를 참조.

## 절대 규칙 (어기면 사고)

1. **대상은 MES만. ERP 금지.** MES와 ERP는 같은 프로세스(`Bizentro.App.MAIN.Shell`)·같은
   클래스(`ShellForm`)로 떠서 **창 제목으로만 구분**된다 — MES=`UNIMES - ...`, ERP=`UNIERP - ...`.
   `windowTitleExcludes:["UNIERP"]`로 ERP를 배제한다. → [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md)
2. **전체 조회로 MES를 멈추게 하지 말 것.** 잘못된 파트로 빈 값/무필터 조회가 나가면 전체
   품목이 로드돼 MES가 멈춘다. 미존재 시 `recoveryPart`(기파트)로 한 건만 조회해 안전 복구한다.
3. **저장 게이트.** 기본 `dryRun=true`, `saveEnabled=false`. `SafetyGuard`가
   `저장/등록/삭제/확정/승인/적용` 버튼을 차단한다. 저장 동작은 명시적으로 켜기 전엔 일어나지 않는다.

## 빌드 / 실행

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --no-launch   # 기존 MES에 attach
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --dump-only --no-launch  # UI 트리만 덤프
```

`appsettings.json`이 없으면 코드 기본값(`Models.cs`의 `CreateDefault`)을 쓴다.

## 코드 핵심

- 진입: `src/UnimesAutomation/Program.cs` → 오케스트레이션: `UnimesApp.cs`(거의 모든 로직).
- 설정 모델: `Models.cs` / 안전가드: `SafetyGuard.cs` / 파트 분류: `PartClassifier.cs`.

## 서브 문서 (상세)

- [docs/STATUS.md](docs/STATUS.md) — **진행 / 미진행 / 알려진 버그** (작업 재개 시 먼저 읽기)
- [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) — 코드 맵, 동작 흐름, 주요 창/컨트롤 automation id
- [docs/CONFIG.md](docs/CONFIG.md) — `appsettings.json` 항목 전체
- [docs/TESTING.md](docs/TESTING.md) — 실행/덤프/로그 확인 절차
- 설계 원안: [docs/specs/2026-06-16-item-info-automation-design.md](docs/specs/2026-06-16-item-info-automation-design.md)

## 작업 원칙

전역 `~/.claude/CLAUDE.md`(Karpathy 원칙: 단순함·외과적 변경·목표주도)를 따른다.
추측으로 고치지 말고 **로그/덤프로 검증** 후 진행한다.
