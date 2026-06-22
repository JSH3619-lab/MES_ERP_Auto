# 아키텍처 / 코드 맵 / 컨트롤 참조

## 파일 맵 (`src/UnimesAutomation/`)

| 파일 | 역할 |
|---|---|
| `Program.cs` | 진입점. 인자 파싱, 설정 로드, GUI 실행 또는 `--dump-only` 실행 |
| `MainForm.cs` | 메인 GUI. 파트 입력, 작업 범위, 안전 상태, 실행/정지, 로그 표시 |
| `SettingsForm.cs`, `CategorySettingsControl.cs` | 설정 편집 GUI |
| `UnimesApp.cs` | 핵심 자동화. 창 탐색, 로그인, 팝업, 메뉴 이동, 품목정보/BIN 워크플로우 |
| `Models.cs` | 설정 모델과 기본값 |
| `ConfigStore.cs`, `SecretProtector.cs` | JSON 설정 저장/로드와 로컬 암호 보호 |
| `SafetyGuard.cs` | 저장/등록/삭제/확정/승인/적용 계열 위험 버튼 차단 |
| `PartClassifier.cs`, `BinIdResolver.cs` | 파트 분류와 BIN ID 목표값 계산 |
| `PartListParser.cs`, `CsvFiles.cs` | GUI/CSV 입력 Part 목록 파싱 |
| `ResultWorkbook.cs` | 결과 xlsx 생성 |
| `UiDump.cs`, `ScreenshotService.cs`, `LoggerSetup.cs` | 진단 산출물과 로깅 |
| `UiTheme.cs` | GUI 색상/폰트 테마 |
| `app.manifest` | 프로세스 시작 시 DPI awareness를 `unaware`로 고정 |

## 실행 흐름

1. `Program.Main`이 설정을 로드하고 로그/스크린샷 경로를 준비한다.
2. 일반 실행은 `MainForm` GUI를 띄운다. `--dump-only`는 GUI 없이 `UnimesApp`을 바로 실행한다.
3. GUI에서 작업 범위(`품목정보관리만`, `BIN 정보 관리만`, `둘 다`)와 Part No 목록을 받는다.
4. 실행 버튼이 `UnimesApp.RunAsync`를 호출하고, UNIMES 창에 attach 또는 launch한다.
5. 로그인 화면이면 자동 로그인한다.
6. 선택 범위에 따라 `품목정보관리`와 `품목별 BIN 정보 관리`를 순서대로 실행한다.
7. 결과 xlsx를 `output/`에 저장하고 완료 요약을 표시한다.
   - `품목정보관리만`, `BIN 정보 관리만`: 해당 작업 종료 후 완료창 1회.
   - `둘 다`: `품목정보관리` 중간 완료창 없이 BIN까지 끝낸 뒤 통합 완료창 1회.
8. 실행 중 정지 요청은 `CancellationToken`으로 전달되어 Part/메뉴 탐색 루프의 안전 지점에서 중단된다.

## 창 식별

MES와 ERP는 프로세스/클래스가 같아서 제목으로 구분한다.

| 시스템 | 창 제목 예 | automationId | process |
|---|---|---|---|
| MES | `UNIMES - UNIMES` | `ShellForm` | `Bizentro.App.MAIN.Shell` |
| ERP | `UNIERP - RMSKR` | `ShellForm` | `Bizentro.App.MAIN.Shell` |

`windowTitleContains:["UNIMES"]`와 `windowTitleExcludes:["UNIERP"]` 조합으로 MES만 대상으로 삼는다.

## 로그인 기준

로그인은 UI Automation으로 Edit/Combo/Button을 우선 찾고, UNIMES 로그인 창이 UIA 컨트롤을 노출하지 않을 때만 창 비율 좌표 fallback을 쓴다.

- ID/PW 입력칸은 같은 행의 좌/우 Edit로 판단한다.
- `Try again`은 서버 응답 오류 문구와 상단 `Try again` 링크가 같이 보이고, 서버 선택 영역의 `UNIMES`가 없는 화면일 때만 처리한다.
- 언어/시스템은 이미 원하는 값이면 건드리지 않는다.

## 품목정보관리

파트별로 `품목명` 입력 후 조회하고, `PartClassifier` 결과에 따라 BIN 관리/TurnKey/AssemblyIn/불량창고 셀을 비교한다. 미존재 Part는 `[971001]품목 코드 이(가) 존재하지 않습니다.` 경고와 `고객사PartID PopUp`을 닫고 SKIPPED 처리한다.

## 품목별 BIN 정보 관리

`BinIdResolver`가 Part No에서 Module/Comp, 공정 키, BIN ID 이름을 계산한다.

- BIN-only 실행은 `품목 코드` 팝업으로 대상 품목을 먼저 선택한다.
- 기존 BIN 행이 있으면 필요한 셀만 비교/수정한다.
- 신규 행이 필요하면 행 추가 후 실제 `BIN 정보 선택` 행이 생겼는지 확인하고 셀을 채운다.
- 저장은 `dryRun=false`와 `saveEnabled=true`가 둘 다 만족될 때만 수행한다.

## 결과 파일

결과는 `ResultWorkbook`이 `output/result_<timestamp>.xlsx`로 저장한다.

- `품목정보관리` 시트: 품목, 분류, BIN 관리, Turn Key, 조립입고, 불량창고, 저장, 상태, 메시지, 처리일시
- `BIN 정보관리` 시트: 품목, 분류, 공정명, BIN Type, Retest No, BIN 완료여부, Retest TH, BIN ID, 저장, 상태, 메시지, 처리일시

## DPI 기준

GUI 도입 후 실행 중 창 크기 축소와 좌표 클릭 어긋남을 막기 위해 `app.manifest`에서 DPI awareness를 `unaware`로 고정한다. 이 설정은 프로세스 시작 시점에 적용되어야 하므로 런타임 `SetHighDpiMode` 호출로 대체하지 않는다.
