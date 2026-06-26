# STATUS

최종 갱신: 2026-06-26

## 현재 기준점

- 브랜치: `feature/mes-gui`
- GUI 기준 실행이 현재 기본 흐름이다.
- `dist/UnimesAutomation.exe`는 단일 실행 파일로 publish해서 실기 테스트한다.
- `app.manifest`가 DPI awareness를 `unaware`로 고정한다.
  - 목적: 실행 중 GUI 창 축소와 좌표 클릭 어긋남 방지.
  - 금지: `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` 런타임 호출 재시도 금지.
- GUI 실행은 실제 저장 모드로 고정한다.
- `SafetyGuard`는 유지하며, 저장 외 위험 버튼(`등록/삭제/확정/승인/적용`)을 계속 차단한다.

## 최근 반영 — 2026-06-26

- **엑셀 드래그앤드랍 import**: PART NO 입력칸에 `.xlsx`를 드롭하면 `품목코드` 열을 자동 탐색해 PID를 추출·채운다. (`ExcelPartReader` → `PartListParser.FromCodes`)
  - 분류 실패(입고 `Z4`·노이즈) 제외 → 더미(PID 끝 `00`) 제외 → PID 단위 중복 제거. 타이핑 입력(`Parse`)·MES 타이핑 경로는 불변.
  - 관리자 권한 실행 시 드롭이 UIPI에 막히지 않도록 `ChangeWindowMessageFilterEx`로 드롭 메시지 허용(`MainForm.OnShown`).
- **SSD BIN 규칙 개정**: 종단 `B0/R0` 분기 → Special Code1(대시 제외 18번째: `B/Z/X/0`=2행, `R/Y/W`=3행) + PID 끝 `00`=더미. (`PartClassifier.IsDummy` 공용화)
- **SIP MFGID 저장 수정**: 변형 행에 BIN관리/TurnKey/조립입고 = N 입력(전엔 Marking만 → 블랭크라 `[970029]` 검증 경고로 저장 거부). result.xlsx에도 N/N/N 기록. 실기 저장 성공 확인.
- **저장 후 예상치 못한 팝업 감지 → 중단**: 품목정보 저장(Ctrl+S) 후 정상 플로우와 무관한 확인 팝업(작은 owned 창+확인 버튼+텍스트)을 700ms 폴링으로 감지 → 내용 ERROR 로그 + 확인 닫기 + `_abortReason`으로 **전체 런 중단**(BIN skip). 전수 그리드 재스캔(느림)은 도입 안 함. (`DetectUnexpectedDialogAsync`)

## 최근 반영 내용

- 로그인 후 Continue 팝업 직접 제어 제거.
  - 팝업은 자동 소멸에 맡긴다.
  - 로그인 후 메인 화면 감지는 최대 12초 대기한다.
- 품목정보관리/BIN 정보 관리의 Part 입력 후 조회는 Enter 중심으로 동작한다.
- 실행 중 정지 버튼은 다음 안전 지점에서 협조적으로 중단한다.
- 완료 알림창은 최상단 메시지박스로 표시한다.
- 실행 실패 시에도 원인 요약과 로그 경로를 최상단 메시지박스로 표시한다.
- 설정 창은 저장된 DPAPI 비밀번호가 있으면 마스킹해서 보여주고, 수정하지 않으면 기존 암호값을 유지한다.
- ZC/ZM Part prefix를 추가했다.
  - `ZC`는 기존 Comp 규칙과 동일하게 처리한다.
  - `ZM`은 기존 Module 규칙과 동일하게 처리한다.
- F3 메뉴 이동 시 MES 메인 창 foreground를 확인하고, 메뉴찾기 입력칸을 찾지 못하면 메뉴명 SendKeys fallback을 생략한다.
  - 목적: Home Page나 기존 업무 화면에 메뉴명을 잘못 입력하지 않기 위함.
- F3 또는 메뉴찾기 버튼 동작 직후 `AutomationElement.FocusedElement`를 로그에 남긴다.
- 메뉴찾기 입력칸 후보를 찾지 못한 경우, focused element가 MES 메인 창 내부의 writable `Edit`일 때만 메뉴명을 직접 설정한다.
  - focused element가 입력칸이 아니면 blind SendKeys 없이 실패 처리한다.
- 품목정보관리 처리 후 BIN 정보 관리로 넘어갈 때 F3가 품목정보관리 그리드에 먹히는 경우를 보완했다.
  - focused element가 writable `Edit`이 아니면 `Home Page` 탭으로 포커스를 빼고 F3 메뉴찾기를 한 번 재시도한다.
  - 메뉴찾기 입력칸 위치 판정 범위를 실제 `txt` 입력칸 높이에 맞춰 조금 넓혔다.
- 메뉴 이동은 F3를 우선 사용하고, 실패할 때만 메뉴찾기 버튼 탐색/클릭 fallback을 사용한다.
- BIN 행추가는 `Ctrl+Insert`를 우선 사용하고, 새 행이 감지되지 않을 때만 행추가 버튼 클릭 fallback을 사용한다.
- 다음 실기 로그 분석을 위해 메뉴 이동, 품목 조회 판단, BIN 컨트롤 캐시, BIN 조회/행탐색/저장에 elapsed 로그를 남긴다.
- README와 운영 문서를 현재 기준으로 정리했다.

## 코드 정리 (리팩터) — 2026-06-25

동작 변화 없음(테스트 31 그린 유지). 구 `docs/REFACTOR_PLAN.md`의 1~5를 전부 완료하고 해당 플랜 문서는 제거했다.

- BIN: `BinRowConfig.Clone()` 인스턴스 메서드로 중복 제거. `BinIdResolver`/`SsdBinRules`/`DramBinRules`를 `BinRules.cs` 한 파일로 통합(클래스명 유지 → 호출부 변경 0).
- 모델: `Models.cs` → `Config.cs`(설정)·`Results.cs`(결과/값 DTO) 분리. `RuntimePaths`/`CommandLineOptions`만 `Models.cs`에 잔류.
- 알림창: `ShowNativeMessageBox` P/Invoke 중복을 `NativeMessage.cs`로 추출(최전면 동작 보존).
- `UnimesApp.cs`(4,608줄) → 코어 + `.Bin`·`.Menu`·`.Dialogs` partial 분할(컴파일 시 한 타입, IL 동일, 호출부 변경 0).
- 로깅: 예상된 UIA 패턴 미지원→폴백 5곳을 `Warn`→`Debug` 강등. `SimpleLogger.Debug` 추가, GUI 콘솔은 `[DEBUG]` 미표시(파일엔 기록). WARN은 `did not commit` 같은 실제 문제에만 남긴다.

## SIP 분류 추가 — 2026-06-25

신규 분류 SIP(prefix `SN`). DRAM/SSD와 같은 워크플로 재사용, 규칙만 추가. 설계: `docs/sip-design.md`.

- 품목정보관리: BIN/TurnKey/조립입고/불량창고 = Y/N/Y/제품폐기창고(DRAM MDL 동일) + `Marking`.
  - base Marking: PID 파생 `AK…A8YWW`. 끝 2글자 `0S/0G/0J/0K`면 생략.
  - **MFGID 변형**: PID 조회 시 그리드에 base + 변형 N행이 함께 뜸. `품목ID`가 `PID + "-"`로 시작하는 행에 BIN관리/TurnKey/조립입고 = N + `"{MFGID 용량} {base}"` Marking 입력(N을 안 넣으면 `[970029]` 검증 경고로 저장 거부). `-` 앵커로 `…0J/0S/00`(다른 파트·더미) 배제. 변형 행도 result.xlsx에 N/N/N + Marking 기록.
- 품목별 BIN 정보 관리: 공정 M030 2행, BIN ID=`SIP_Normal_{용량}_AIO`(용량=파트 4–5자), 1행 Bin완료여부 미설정·2행 Y, Retest TH Normal·Y.
- 설정 SIP 탭, Marking 셀은 ValuePattern→더블클릭 폴백.
- 실기 확인: 품목정보+Marking(base+변형) 동작 확인됨. BIN 2행은 스모크 단계.

## 검증 상태

항상 커밋 전 다음 명령을 통과시킨다.

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj -c Release
dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj -c Release --no-restore
dotnet publish .\src\UnimesAutomation\UnimesAutomation.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\dist
```

현재 단위 테스트 기준:

- 73개 통과 (DRAM/SSD/SIP 분류 + Marking/BIN 규칙 + 결과 워크북 + 엑셀 import/추출)
- 실패 0개

## 핵심 동작

- MES/ERP 구분은 창 제목으로 한다. `UNIMES`만 대상이고 `UNIERP`는 제외한다.
- 자동화 대상은 MES만이다.
- 자동 로그인 후 메인 화면이 감지되면 바로 F3 메뉴 검색으로 이동한다.
- `품목정보관리`:
  - Part 조회
  - BIN 관리, Turn Key, 조립입고 공정이동여부, 불량창고 비교/수정
  - 값이 이미 일치하면 저장 없이 `UNCHANGED`
- `품목별 BIN 정보 관리`:
  - BIN-only 모드에서는 품목 코드 검색 팝업으로 Part 선택
  - 둘 다 모드에서는 품목정보관리에서 유효한 Part만 BIN 처리
  - 기존 BIN 행이 있으면 신규 행추가 없이 `UNCHANGED`
  - 신규 행이 필요하면 공정명, BIN Type, Retest No, Bin완료여부, Retest TH, BIN ID 입력 후 저장
- 결과는 `output/result_<timestamp>.xlsx` 한 파일로 저장된다.

## 다음 작업 방향

코드는 현재 동작 기준으로 크게 흔들지 않는다. 이후 작업은 디자인 요소와 세부 동작 시간만 실기 로그 기준으로 좁게 조정한다.

다음 실기 테스트에서 먼저 볼 항목:

- 최신 실기 로그: `logs/run_20260623_153353.log`
- 관련 캡처:
  - `screenshots/20260623_153440_934_menu_search_input_not_found_attempt_1.png`
  - `screenshots/20260623_153447_714_menu_search_input_not_found_attempt_2.png`
  - `screenshots/20260623_153454_463_menu_search_input_not_found_attempt_3.png`
- 직전 실패 지점:
  - F3 입력 후 `품목정보관리` 화면 진입 전, 메뉴찾기 입력칸을 찾지 못해 중단.
  - 캡처상 우측 상단 검색 입력칸이 아니라 좌측 메뉴 패널의 `기준정보` 영역에 선택/포커스가 잡힌 것으로 보인다.
- 다음 확인 포인트:
  - 새 실기 로그에서 `FocusedElement` 라인을 확인한다.
  - `insideMain=True, writableEdit=True`이면 focused Edit 경로로 메뉴명이 입력되는지 확인한다.
  - `writableEdit=False`이면 `Home Page 탭 전환 후 F3 메뉴찾기 재입력` 로그가 나온 뒤 BIN 정보 관리 화면 진입 여부를 확인한다.
  - BIN 행추가에서 `BIN 행추가 Ctrl+Insert 성공`이 먼저 나오는지 확인한다. 실패 시에만 버튼 fallback 로그가 나와야 한다.
  - `elapsed=` 로그 기준으로 5초 이상 구간만 다음 최적화 대상으로 본다.

## 성능 최적화 후보 (미진행 · 다른 세션에서 진행)

근거 로그: `logs/run_20260624_142947.log` (SSD 2파트 · BIN 5행 · 총 약 124초). 코드 미수정 상태. 효과/리스크 순.

### 1) BIN 팝업 InvokePattern 선시도 제거 — 효과 최대 / 리스크 낮음
- 현상: BIN 행마다 `공정명`(~4.6s) · `BIN ID`(~4.2s) 팝업에서 매번 `InvokePattern unavailable` 로그 후 ~3초 대기하고 좌표 fallback으로 선택. 행당 약 9초.
- InvokePattern이 항상 실패하므로, 선시도/대기를 건너뛰고 바로 좌표 클릭으로 간다.
- 대상: `SelectProcessPopupAsync`, `SelectBinIdPopupExactAsync`.
- 기대 단축: 행당 4~6초, 5행 기준 20~30초.

### 2) 완료 알림 먼저 띄우고 결과 xlsx는 그 뒤/백그라운드 — 효과 중 / 리스크 낮음
- 현상: BIN 종료 → 결과 xlsx 저장(~1.8s) → 완료 → 알림창. 체감 약 3초.
- 알림창을 먼저 표시하고 `ResultWorkbook` 저장을 이후(또는 백그라운드)로 옮긴다.
- 기대 단축: 체감 약 1.8초.

### 3) 품목정보관리 → BIN 전환 단축 — 효과 중 / 리스크 높음
- 현상: 전환에 약 12초. 분해: 메뉴 입력칸 활성화 ~4.4s + 화면확인 폴링 ~3.1s + BIN 컨트롤 캐시(`FindBinPartIdEdit`) ~4.3s.
- 가능: 컨트롤 캐시 탐색 범위 축소, `WaitForMenuScreenAsync` 폴링 간격/타임아웃 조정.
- 리스크: 전부 UIA 탐색·안전폴링이라 줄이면 오탐/실패율 상승. 실기 반복 검증 필수.

### 유지: BIN 행 단위 저장(Ctrl+S)
- 행마다 저장은 의도된 구조 — 행별 검증 경고 격리 + 부분성공 보존. 배치 저장은 MES 수용 여부 미검증이라 성능 목적 변경 비권장.

## 주의점

- UI Automation 기반이라 화면에 보이는 것과 UIA 트리가 다를 수 있다.
- 실패 분석은 최신 `logs/run_*.log`, 대응 스크린샷, `logs/ui_dump_*.txt` 순서로 한다.
- `logs/`, `screenshots/`, `output/`, `bin/`, `obj/`, `dist/`는 생성물이며 git 추적 대상이 아니다.
- `appsettings.json`은 로컬 설정 파일이며 git 추적 대상이 아니다.
