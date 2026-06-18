# BIN 저장 테스트 디버깅 핸드오프 (2026-06-17)

> 실 저장(save-test)으로 `품목별 BIN 정보 관리`에 한 건을 끝까지 입력·저장하는 작업의 진행 메모.
> 다음 세션은 이 문서 + [STATUS.md](STATUS.md)를 먼저 읽을 것. 상세 이력은 STATUS.md에 있다.

---

## 한눈에 (현재 상태)

- 실행: `run_unimes_automation_save_test.cmd` (= `appsettings.save-test.json`, **dryRun=false / saveEnabled=true** → 실제 저장).
- 진행도: **900014/BIN Type/완료 알림 정상 동작 확인. 닫힌 업무창 메뉴 진입, 메뉴찾기 재활성화, 로그인 자동화 1차 수정 완료. 다음 세션 live 재검증 필요.**
- 한 번은 끝까지 가서 저장됨(`run_20260617_164...`, `BIN saved`). 단 그땐 콤보 3개가 빈 값이었고(아래 해결), MES 창이 보이는 상태였음.

---

## 올바른 동작 순서 (지금 코드가 지향하는 흐름)

`UnimesApp.RunBinInfoWorkflowAsync` (대략 `UnimesApp.cs:424~`):

1. 룩업 검색 팝업(`Undefined`)에서 **품목 코드로 검색·선택** (`SelectBinProductFromLookupAsync`)
2. (룩업 자동조회로 `[900014]`가 뜨면 닫기) — `ConfirmNoDataPopupAsync`
3. **명시적 조회 클릭** (필수! 안 하면 저장 시 `[800247]조회작업을 선행한 후 저장하십시오.`)
4. (조회 결과 없으면 `[900014]` 뜸 → 닫기) — `ConfirmNoDataPopupAsync`
5. 기존 행 스캔(`FindBinRowsForPart`) → 없으면 **행추가**(`ClickInsertRow` = 행추가 버튼 → 실패 시 Ctrl+Insert)
6. 셀 채움: 공정명 팝업, **BIN Type / Bin완료여부 / Retest TH**(콤보), 고정셀, BIN ID 팝업
7. **저장**(Ctrl+S) — `SaveItemInfo`, 저장 게이트(`saveEnabled && !dryRun`) 통과 시에만

---

## 이번 세션에서 해결한 것

| 문제 | 근본 원인 | 수정 |
|---|---|---|
| 971001 경고가 안 닫히고 "처리 완료"로 거짓 종료 | `WaitForNativeWindowNoLongerForeground`가 **포그라운드 변화**를 닫힘으로 오판(거짓 양성) | 971001은 **텍스트 기반 `FindWarningDialog`**(`존재하지`)로 탐지 → 진짜 확인 클릭/Enter → 재탐색 검증 |
| `FindWarningDialog`가 엉뚱한 창(에디터/터미널) 매칭 | 시스템 전체 top-level을 스캔 + 부분일치 | `IsUnimesCandidate(window)`로 **MES 프로세스 창만** 스캔하도록 한정 |
| 룩업 팝업이 잔존 입력값을 먼저 자동조회 | `품목 ID` 칸에 이전 값 잔존 상태로 팝업 오픈 | `OpenBinProductLookupPopup` 진입 시 `SetElementText(partIdEdit, "")`로 먼저 비움 |
| 룩업 팝업 `미감지`로 즉시 종료 | 개방 후 **단발 확인**(300ms 후 1회) — 내부 라벨이 늦게 렌더 | `WaitForPopupWindowAsync`(100ms 폴링, 2s)로 교체 |
| 콤보(BIN Type/Bin완료여부/Retest TH) 빈 값 저장 | `SetBinComboCell`이 **키보드 단독** — 새 행 콤보는 ExpandCollapse가 막혀 드롭다운 안 열림 | 품목정보관리 `ApplyComboCell`과 동일한 **3단 전략**(리스트항목 직접선택→키보드→ValuePattern)으로 변경 |

빌드/테스트는 매 수정마다 통과(경고 0 / 오류 0, 테스트 10/10).

---

## 기존 막힌 지점 + 진단

### 증상
- `run_20260617_211921.log`: `BIN 900014 모달 미감지. foreground='UNIMES - UNIMES'` 후 멈춤.
- `run_20260617_213730.log`: `검색 선택 완료`(42.724) → `BIN 조회 실행`(38:32) **사이 ~50초** 후 멈춤. 화면엔 `[900014]` 모달.

### 확정된 근본 사실
1. **`[900014]` 모달은 메인 창(ShellForm)에 종속(owned)된 별도 top-level 창이다.**
   - 진단 로그 `foreground='UNIMES - UNIMES'`가 증거: 모달이 떠 있어도 `GetForegroundWindow()`는 **메인 창**을 돌려준다.
   - → **포그라운드 기반 감지(`HasForeignSmallForegroundWindow`)는 구조적으로 900014를 못 잡는다.** (이미 폐기함)
2. **모달이 떠 있으면 메인 창이 모달 루프에 잠긴다 → 메인 창 descendants UIA 스캔이 블록(50초+).**
   - 971001(룩업 팝업 위, 메인 안 막힘)과 결정적으로 다른 점.
   - 현재 `ConfirmNoDataPopupAsync`가 쓰는 `FindMesMessageDialog`는 **메인 창까지 스캔**해서 여기서 막힌다.
3. **MES 창이 자주 최소화 상태로 시작**(`L=-32000, visible=False`)된다.
   - → 컨트롤 캐시 False(`partIdEdit=False, searchButton=False`), 좌표 클릭 빗나감, 스캔 느림.
   - `BringToFront`에 `IsIconic`→`SW_RESTORE`가 있으나 **컨트롤 캐시(루프 진입 전)보다 늦게** 호출됨.

### 왜 "되던 게 갑자기 안 되나"
- 잘 됐던 run은 MES 창이 **보이는** 상태였고, 운 좋게 모달 타이밍/위치가 맞았다.
- 내가 팝업마다 **감지 방식을 바꿔가며**(텍스트↔포그라운드) 패치를 쌓아서, 한 곳 고치면 다른 가정이 깨졌다. ← 핵심 반성.

---

## 이번 세션 수정

### 2026-06-18 닫힌 업무창에서 메뉴 진입 실패

- 증상: MES 메인만 열려 있고 `품목정보관리`/`품목별 BIN 정보 관리` 업무창이 닫힌 상태에서 실행하면 F3 메뉴 진입을 하지 않고 메인 화면에서 필드 탐색을 시도했다.
- 로그 근거: `run_20260618_105822.log`에 `품목정보관리 screen already detected. F3 navigation skipped.`가 찍혔지만, `ui_dump_20260618_105822.txt`에는 실제 `ControlType.Window name='품목정보관리'`가 없고 왼쪽 메뉴 트리의 `DataItem name='품목정보관리'`만 있었다.
- 원인: `NavigateToMenuByF3Async`/`WaitForMenuScreenAsync`가 `FindFirstByNameContains(mainWindow, menuName)`로 화면 여부를 판단해 왼쪽 메뉴 텍스트를 열린 업무창으로 오인했다.
- 수정: 메뉴 화면 확인을 실제 MDI 업무창 탐색인 `FindNamedWindow(mainWindow, menuName)`로 제한했다. 이제 업무창이 닫혀 있으면 F3/메뉴찾기로 진입하고, 왼쪽 메뉴 트리 텍스트만으로는 진입 성공 처리하지 않는다.
- 추가 증상: `run_20260618_110411.log`에서 `품목정보관리`는 메뉴찾기로 열렸지만, 이어서 `품목별 BIN 정보 관리` 이동 시 오른쪽 위 메뉴찾기 패널이 첫 검색어(`품목정보관리`) 상태로 남아 3회 모두 화면 확인 실패.
- 추가 수정: 메뉴찾기 버튼 클릭 후 바로 SendKeys를 보내지 않고, 상단 오른쪽 메뉴찾기 패널의 입력칸을 직접 찾아 포커스/재입력한다. 그래도 실패하면 `가기` 버튼 클릭을 시도하고, 마지막으로 왼쪽 트리의 동일 메뉴 `DataItem`을 직접 더블클릭한다.

### 2026-06-18 로그인 자동화 1차 구현

- 실행 시작 시 로그인된 UNIMES 메인창이 이미 있으면 로그인 입력창을 띄우지 않는다.
- 로그인된 메인창이 없고 launch/attachOrLaunch 모드이면 config/env에 저장된 ID/PW만 사용한다. 실행 때마다 ID/PW 입력창은 띄우지 않는다.
- 로그인 화면이 뜨면 `Try again`/`서버가 응답하지 않습니다` 상태를 먼저 감지하고 최대 5회 클릭해 복구한 뒤 로그인 입력을 진행한다.
- `Try again` UIA 요소가 잡히지 않으면 로그인창 기준 상단 상대좌표 클릭 fallback을 사용한다.
- 로그인 버튼은 `확인`/`Login`/`OK` 후보를 활성 상태까지 기다린 뒤 클릭한다.
- 기본 설정은 `passwordMode=env`, `UNIMES_PASSWORD` 환경변수 사용. `UNIMES_USER_ID`가 없으면 `login.userId`를 사용한다.
- 추가 수정: `run_20260618_155424.log`에서 100x100 `frmInitial` 로딩창을 초기 UNIMES 창으로 잡은 뒤 실제 로그인창을 다시 감지하지 않아 멈춘 흐름을 확인했다. 초기 창이 로그인/메인이 아니면 `WaitForLoginOrMainWindowAsync`로 로그인창 또는 메인창이 뜰 때까지 다시 기다리도록 변경했다.
- Continue 팝업이 뜨면 `오늘 보지 않기` 체크박스를 먼저 찾아 클릭한 뒤 `Continue`를 누른다. 체크박스가 없거나 이미 오늘 숨김 상태라 팝업이 안 뜨면 기존처럼 넘어간다.
- `run_20260618_163600.log`에서는 화면에 `Try again`이 없는데 숨은 UIA 텍스트 때문에 Try again 상태로 오판했다. 이제 실제 보이는 `Try again` 요소가 있을 때만 클릭하고, 로그인 `Edit`는 화면 좌표 기준 위쪽 2개를 ID/PW로 선택한다.

1. `ConfirmNoDataPopupAsync`가 메인 `ShellForm` descendants를 훑지 않도록 900014 전용 `FindNoDataDialog`를 추가했다.
   - top-level MES 창 중 메인 `ShellForm`은 direct child `Window`만 검사한다.
   - 작은 메시지 창 후보 안에서만 `900014`/`검색된 Data` 토큰과 `확인` 버튼을 확인한다.
   - 찾으면 `확인` 클릭 또는 Enter 후 같은 전용 탐지로 닫힘을 검증한다.
2. `RunBinInfoWorkflowAsync`에서 BIN 컨트롤 캐시 전에 `BringToFront(mainWindow)`를 한 번 호출해 최소화 상태를 먼저 복원한다.
3. `MessageBoxForm` 본문이 `Name`이 아니라 `ValuePattern`(`RichEdit Control`)으로 노출되는 것을 확인했다.
   - 메시지 탐지 함수가 `Name` + `ValuePattern`을 모두 보도록 수정했다.
   - 이 문제 때문에 `[900014]검색된 Data가 없습니다.`와 `[970029]BIN Type...` 둘 다 감지 실패했다.
4. BIN Type 설정은 표시명 `Normal-1` 실패 시 내부 저장값 `0`도 `ValuePattern`으로 시도한다.
   - 그래도 실패하면 저장을 누르지 않고 예외로 중단한다.
   - 저장 후 `[970029]` 검증 경고가 뜨면 자동으로 닫고 `BIN saved`로 기록하지 않는다.
5. 재발 로그(`run_20260617_222041.log`) 기준 추가 원인:
   - `검색 선택 완료` → `BIN 조회 실행` 사이 33초 공백. `조회` 버튼 `InvokePattern.Invoke()`가 900014 모달에 막혔다가 사용자가 확인을 누른 뒤 풀린 흐름이다.
   - `ConfirmNoDataPopupAsync`가 타이밍상 모달을 놓치면 바로 `Invoke()`로 들어가 다시 멈출 수 있다.
6. 추가 수정:
   - 900014는 토큰을 못 읽어도 `MessageBoxForm + 확인 버튼`이면 닫는다.
   - 룩업 선택 직후 900014 대기 시간을 8초로 늘렸다.
   - BIN 조회 버튼은 `Invoke()` 대신 안전가드 통과 후 마우스 클릭으로 전송해 모달에 블록되지 않게 했다.
7. 재실행(`run_20260617_225032.log`)에서 900014가 2번 뜬 이유:
   - 첫 900014는 명시적 조회 결과 없음으로 정상.
   - 이후 "조회 클릭이 기존 900014 모달에 막혔을 가능성" 재시도 분기가 조회를 한 번 더 눌러 두 번째 900014를 만들었다.
   - 마우스 클릭으로 바뀐 뒤에는 `Invoke()` 블록 재시도가 필요 없으므로 해당 재조회 분기를 제거했다.
8. BIN workflow도 `PartResult`를 기록해 종료 시 결과 CSV와 완료 팝업을 띄우도록 했다.
9. 검증: `dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj --no-incremental` 경고 0 / 오류 0, `dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj` 10/10 통과.

## 다음 세션 바로 할 일

1. **로그인 live 재검증.**
   - 기대 로그: `Login visible Edit controls found: 2` → `Login user id filled. source=config` → `Login password filled. source=config` → `Login button clicked.`
   - 실패 시 최신 `run_*.log`의 `login edit[0/1]` 좌표와 로그인 화면 스크린샷을 먼저 확인한다.
2. **Continue 팝업 처리 확인.**
   - 팝업이 뜨면 `Continue popup '오늘 보지 않기' checked.` → `Continue popup clicked.` 로그를 기대한다.
   - MES에서 이미 오늘 숨김이면 팝업 없이 넘어가는 것이 정상.
3. **품목정보관리 → BIN 정보 관리 연속 전환 확인.**
   - 닫힌 업무창 상태에서 `품목정보관리` 진입 후 `품목별 BIN 정보 관리`로 넘어가는지 확인한다.
   - 기대 로그: `메뉴찾기 입력칸 직접 활성화` 또는 fallback `트리 메뉴 직접 더블클릭`.
4. **BIN 저장 흐름 최종 확인.**
   - 기대 로그: `BIN 900014 경고 [확인]/Enter 처리` → `BIN 조회 버튼 mouse click 전송` → `BIN cell set ... BIN Type` → `BIN saved` 또는 검증 경고 시 ERROR 처리.

---

## 핵심 교훈 (반복 금지)

- **잘 되는 코드를 기준으로, 달라진 점만 바꿔라.** 팝업마다 감지 방식을 새로 발명하지 말 것. (971001 텍스트 탐지가 레퍼런스)
- **모달의 위치가 전부다.** 룩업 팝업 위 모달(메인 안 막힘) vs 메인 창 위 모달(메인 잠김 → 메인 스캔 블록)은 처리가 다르다.
- **owned 모달은 `GetForegroundWindow()`로 안 잡힌다**(부모 창을 돌려줌). 포그라운드 기반 감지 금지.
- **저장 전 조회는 필수**(`[800247]`). 룩업 자동조회는 신뢰 불가 → 명시적 조회 클릭 유지.
- **MES 창 최소화 시 모든 게 깨진다**(스캔 블록·컨트롤 못 찾음). 복원/표시 보장.

---

## 관련 코드/로그 위치

- 흐름: `RunBinInfoWorkflowAsync` (`src/UnimesAutomation/UnimesApp.cs:424~`)
- 900014 처리: `ConfirmNoDataPopupAsync` / 971001 처리: `TryDismissBinProductMissingWarningAsync`
- 텍스트 탐지: `FindWarningDialog` → `FindMesMessageDialog(tokens)` / `IsMesMessageDialog`
- 콤보 채움: `SetBinComboCell` (3단 전략) / 참조: `ApplyComboCell`(품목정보관리)
- 최소화 복원: `BringToFront` (`src/UnimesAutomation/UnimesApp.cs:3096~`, `IsIconic`→`SW_RESTORE`)
- 저장 게이트: `SaveItemInfo`, `SafetyGuard.EnsureCanClick`
- 주요 로그: `logs/run_20260617_211921.log`(foreground 진단), `logs/run_20260617_213730.log`(50초 블록 + 최소화)
