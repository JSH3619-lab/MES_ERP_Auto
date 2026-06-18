# 진행 상황 / 변경 이력 / 미해결 원인 (상세)

작업 재개 시 이 문서부터 읽을 것. (최종 갱신: 2026-06-17 16:xx, BIN 971001/입력칸 초기화 + 900014 모달을 행 스캔/조회 클릭 전에 닫도록 순서 조정 직후)

---

## 한눈에

| # | 항목 | 상태 |
|---|---|---|
| 1 | ERP 창 잡힘 | ✅ 해결·검증됨 |
| 2 | attach 시 포커스(직접 클릭 필요) | 🟡 견고화 구현, 단독 검증은 못 함(3에 막힘) |
| 3 | 미존재 파트 → MES 멈춤(응답없음) | 🟡 **팝업 취소 → 기파트 팝업-내부 복구로 재전환(전체조회 회피), 실 MES 검증 필요** |
| 4 | 시작/attach 후 CMD 정지 | 🟡 **원인 후보 확인 후 수정, 다음 실행 검증 필요** |
| 5 | attach 상태에서 F3 메뉴 진입 실패 후 엉뚱한 입력 | 🟡 **메뉴찾기 버튼 우선 + 3회 재시도 + 실패 시 중단으로 수정, 실 MES 검증 필요** |
| 6 | 미존재 복구 후 정상 Part 조회 뒤 진행 없음 | 🟡 **정상 Part 단계 로그 추가 + 그리드 탐색 범위 축소, 실 MES 검증 필요** |
| 7 | 불량창고 룩업 콤보만 값 미설정 | ✅ **키보드 드롭다운 선택으로 해결·검증됨(사용자 확인)** |
| 8 | 조회까지 Part당 ~8초 | 🟡 **stable 컨트롤 루프 밖 캐싱 + 팝업 폴링 700ms로 단축, 다중 Part 실측 필요** |
| 9 | BIN-only 품목 코드 검색 팝업 `[971001]` 처리 | 🟡 **품목정보관리와 동일한 텍스트 감지(FindWarningDialog)로 전환, foreground 판정 폐기, live 재검증 필요** |
| — | 툴바 조회 버튼 탐색 | ✅ automation_id 보강(정상 발사 확인) |

---

## 다음 세션 시작 시 바로 할 일

0. Claude 인계 메모(2026-06-17 16:xx) — **BIN 971001 처리를 품목정보관리 방식으로 수렴**
   - 실패 증거:
     - `logs/run_20260617_151405.log` + 사용자 스크린샷.
     - 로그는 `BIN 품목 코드 미존재 경고 foreground [확인] 처리` → `미존재로 건너뜀` → 다음 Part → `workflow finished`까지 정상 종료로 찍혔다.
     - 그러나 화면에는 첫 Part(`...W8`)의 `[971001]` 경고가 그대로 남아 있고 품목 코드 칸도 여전히 `...W8`(다음 Part `...W7`이 입력된 적 없음).
     - 즉 첫 경고가 실제로는 안 닫혔고, 그 위 모든 동작(W7 입력/조회)은 모달에 막혀 허공으로 나갔다 = 거짓 완료.
   - 근본 원인:
     - 실제로 실행된 경로는 `TryClickForegroundConfirmOutsideLookup`로, **포그라운드 작은 창 + 확인 버튼**만 보고 `971001` 텍스트는 인지하지 않았다.
     - 성공 판정도 `WaitForNativeWindowNoLongerForeground`(포그라운드 변화)였다. `GetForegroundWindow()`는 top-level만 돌려주므로, 경고가 안 닫혀도 포그라운드가 달라 보이면 즉시 `true` → 거짓 양성.
     - 품목정보관리는 정반대로 텍스트(`존재하지`)로 경고를 특정(`FindWarningDialog`)해서 잘 닫고 있었다.
   - 이번 수정(`src/UnimesAutomation/UnimesApp.cs`):
     - `TryDismissBinProductMissingWarningAsync`를 품목정보관리와 동일하게 `FindWarningDialog()`(텍스트 `존재하지` + 하위 Window 탐색)로 경고를 특정 → 진짜 `확인` Invoke(없으면 `Enter`).
     - 성공 판정은 포그라운드 변화가 아니라 **`FindWarningDialog()` 재탐색이 null** 이 될 때만 true.
     - UIA로 경고 텍스트를 못 읽어도 룩업 위에 모달 작은 창이 떠 있으면(`HasForeignSmallForegroundWindow`) 품목정보관리의 forceEnterFallback처럼 `Enter`로 닫고, 모달이 사라진 것까지 확인.
     - 폐기된 orphan 제거: `TryClickFocusedConfirmOutsideLookup`, `TryClickForegroundConfirmOutsideLookup`, `ClickForegroundConfirmFallbackByCoordinates`, `FindDirectChildButtonByAnyName`, `FindBinProductMissingWarningWindow`, `IsBinProductMissingWarningCandidate`, `WindowContainsAllText`, `EnumerateSmallPopupWindows`, `WaitForNativeWindowNoLongerForeground`.
     - 빌드 경고 0 / 오류 0, 테스트 10/10 통과.
   - 1차 전환 직후 재현된 추가 버그(`run_20260617_153713.log` + `..._153736_..._bin_exception_*.png`):
     - 로그: `Blocked dangerous button click. button='TryClickForegroundConfirmOutsideLookup:902', keyword='Confirm', ... reason='BIN product missing warning confirm'` → `BIN 처리 실패 ... Safety guard blocked`.
     - 화면: 진짜 `[971001]` 경고(확인 버튼 포함)는 정상적으로 떠 있었다.
     - 근본 원인: `FindWarningDialog()`가 **시스템 전체 top-level 창**을 훑어, 이 화면 밖 에디터/터미널 창(코드/STATUS/대화에 `존재하지`와 삭제된 메서드명 `TryClickForegroundConfirmOutsideLookup ... 902`가 보임)을 경고창으로 오인했다.
       - `IsMissingPartWarningWindow`는 `존재하지`(부분일치) + `확인`/`OK`(부분일치, 대소문자무시) 버튼만 보면 참 → `...OutsideLookup`의 `ok`가 `OK`에 매칭.
       - 오인된 버튼 Name에 `Confirm`이 들어가 SafetyGuard가 차단 → 예외로 파트 중단.
     - 수정: `FindWarningDialog()` 바깥 루프를 `IsUnimesCandidate(window)`로 한정(MES 프로세스 창만). 경고는 항상 MES 프로세스 소속이라 item-info/BIN 공통 안전. 진짜 `확인`(Name=`확인`)은 위험 키워드 미포함이라 SafetyGuard 통과.
     - 빌드 경고 0 / 오류 0, 테스트 10/10 통과.
   - 위 두 수정 후 사용자 확인: 팝업 인지 + `확인` 클릭은 정상 동작. 남은 진짜 원인은 **입력칸 잔존값**:
     - 검색 팝업은 열릴 때 `품목 ID` 칸에 남아 있는 값을 먼저 자동 조회한다. 이전 Part/실행의 잔존값이 미존재면 거기서 `[971001]` 경고가 떠 흐름이 꼬였다.
     - 수정: `OpenBinProductLookupPopup` 진입 시 `SetElementText(partIdEdit, "")`로 품목 ID 칸을 먼저 비운 뒤 팝업을 연다. (팝업 입력칸은 `ValuePattern.SetValue`로 매번 대체되므로 별도 처리 불필요.)
     - BIN 경고 닫기(`TryDismissBinProductMissingWarningAsync`)는 FindWarningDialog 텍스트 감지 + `확인` 클릭 방식 그대로 유지(정상 동작 확인됨). Enter-only 단순화는 보류.
   - 실 저장 테스트(`save-test`, dryRun=false) 중 `[900014]검색된 Data가 없습니다.` 모달 처리 실패(`run_20260617_160848.log`):
     - 로그가 `BIN 품목 코드 검색 선택 완료` 직후 멈춤(다음 `BIN 조회 실행` 로그 없음). 화면엔 900014 모달.
     - 근본 원인: 룩업에서 품목 선택이 끝나면 BIN 화면이 자동 조회 → 데이터 없음 → 900014 모달이 뜬다. 그 모달이 떠 있는 상태에서 [line 486] 메인 `조회` 버튼 클릭(BringToFront/Invoke)이 모달에 막혀 멈춤. 그래서 900014 처리(`ConfirmNoDataPopupAsync`)까지 도달조차 못 함.
     - 교훈: **900014 모달이 떠 있으면 그 위 UIA 클릭/descendants 스캔이 전부 블록**된다. 모달은 조회 클릭·행 스캔 *전에* 닫아야 한다. 모달이 떠 있는 동안엔 텍스트 스캔 자체가 블록되므로 스캔으로 찾지 말 것.
     - 수정:
       - `ConfirmNoDataPopupAsync`를 포그라운드 작은 모달 감지(`HasForeignSmallForegroundWindow`) → Enter(확인 기본 버튼, 클릭 아님 → SafetyGuard 무관) → 모달 사라짐 확인으로 재작성. 모달 없으면 무동작(엉뚱한 Enter 방지). 텍스트 스캔 제거.
       - 호출 순서 조정: ① 룩업 선택 직후(조회 클릭 전), ② 조회+딜레이 직후(행 스캔 전)에 호출. 기존 행 스캔 뒤(line 509) 호출은 제거.
       - orphan 제거: `FindWarningDialogByTextFast`. (기존 dead code `FindWarningDialogByText`는 이전부터 미사용 — 이번엔 두고 언급만.)
     - 빌드 경고 0 / 오류 0, 테스트 10/10 통과. live 재검증 필요.
   - 900014 수정 후 재실행(`run_20260617_162203.log`)에서 룩업 팝업 자체가 `미감지`로 즉시 종료:
     - `BIN 품목 코드 검색 버튼 클릭`은 찍혔는데 `검색 팝업 미감지`로 skip. 시작 UI 덤프엔 잔존 팝업/모달 없음(상태 문제 아님). 직전 run에선 같은 파트가 정상 개방됨 → 타이밍 플레이크.
     - 근본 원인: `SelectBinProductFromLookupAsync`가 팝업 개방을 `Task.Delay(300)` 후 `FindPopupWindow` **단 한 번**만 확인. UNIMES 팝업은 창은 떠도 내부 `품목 코드` 라벨이 늦게 렌더되는데 [IsBinProductLookupPopupWindow]가 그 라벨을 요구 → 단발 확인이 렌더 전에 맞으면 false.
     - 수정: 단발 확인을 기존 폴링 헬퍼 `WaitForPopupWindowAsync(IsBinProductLookupPopupWindow, 2000ms)`로 교체(100ms마다 재시도).
     - 빌드 경고 0 / 오류 0, 테스트 10/10 통과. live 재검증 필요.
   - 룩업 감지 수정 후 재실행(`run_20260617_163139.log`)에서 또 `BIN 품목 코드 검색 선택 완료` 직후 멈춤(900014 모달):
     - 근본 원인: **룩업에서 품목을 선택하면 BIN 화면이 자동 조회**한다 → 결과 없음 → 900014 모달. 그 모달이 뜬 상태에서 코드가 **불필요한 메인 조회 버튼 클릭(line 486)** 을 시도 → 모달에 막혀 멈춤. 게다가 `ConfirmNoDataPopupAsync` 단발 검사가 모달 등장 타이밍을 놓침.
     - 사용자 지침(단순화): 팝업은 Enter로 닫고 바로 Ctrl+Insert로 행추가하면 된다. 메인 조회 클릭은 군더더기.
     - 수정:
       - **룩업 플로우에서 메인 조회 클릭 제거**(else=비룩업 분기로만 한정). 룩업 선택의 자동 조회를 그대로 사용.
       - `ConfirmNoDataPopupAsync`를 ~2초 폴링으로(모달 등장 대기) → 포그라운드 작은 모달 감지 시 Enter → 사라짐 확인. 미감지 시 foreground 창 이름을 진단 로그로 남김.
       - 행추가는 기존 `ClickInsertRow`(행추가 버튼 → 실패 시 Ctrl+Insert) 그대로.
     - 참고: 포그라운드 모달 감지(`HasForeignSmallForegroundWindow`)는 971001 룩업 흐름에서 이미 동작 확인됨 → 이번 실패는 타이밍/군더더기 클릭이 원인으로 판단.
     - 빌드 경고 0 / 오류 0, 테스트 10/10 통과. live 재검증 필요(미감지 진단 로그 주시).
   - 위 수정 후 `run_20260617_164...`에서 **전체 흐름 끝까지 진행 + 실제 저장 성공**(`BIN saved`). 다만 콤보 3개(BIN Type/Bin완료여부/Retest TH)가 빈 값으로 저장됨:
     - 로그: `Combo expand (keyboard) failed. reason=Operation is not valid due to the current state of the object.` → `Keyboard combo navigation done...` → `BIN combo selected but displayed value differs. expected='...', actual=''`.
     - 근본 원인: `SetBinComboCell`이 **키보드 네비게이션 단독**으로만 셀을 채웠다. 새 행 콤보는 `ExpandCollapse.Expand()`가 'current state' 예외로 막혀 드롭다운이 안 열린 채 UP/DOWN/ENTER가 허공으로 나가 빈 값. (품목정보관리 `ApplyComboCell`은 리스트 항목 직접 선택→키보드→ValuePattern 3단 전략이라 정상.)
     - 수정: `SetBinComboCell`을 `ApplyComboCell`과 동일한 3단 전략으로 변경(리스트 항목 `TrySelectListItem` 직접 선택 우선 → 키보드 → ValuePattern). 리스트 항목 직접 선택은 드롭다운 펼침 불필요. BIN의 `expectedStoredValue` 검증은 유지.
     - 빌드 경고 0 / 오류 0, 테스트 10/10 통과. live 재검증 필요(`BIN cell set via list item` 로그 + 콤보 값 채워지는지).
   - 콤보 수정 후 재실행(`run_20260617_205119.log`)에서 `[800247]조회작업을 선행한 후 저장하십시오.` + `BIN 추가 행 미발견`:
     - **자기 실수 정정**: 직전 라운드에서 "룩업 선택이 자동 조회하니 메인 조회 클릭은 군더더기"라며 조회 클릭을 제거했는데 **틀렸다**. MES는 **저장(및 행추가) 전에 명시적 조회를 요구**한다([800247]). 룩업 자동조회가 우연히 그 역할을 한 적도 있지만(164139) 신뢰 불가.
     - 추가 관찰: 이 run은 MES 창이 **최소화 상태**(`L=-32000, visible=False`)로 시작 → 컨트롤 캐시 False. `BringToFront`의 `IsIconic`→`SW_RESTORE`로 루프 안에서 복원되긴 하나, **실행 전 MES 창을 보이게/최대화**해 두는 게 안전.
     - 수정: 메인 조회 클릭을 룩업/비룩업 **공통 경로로 복원**. 순서 = 룩업선택 → (자동조회 900014 닫기) → **명시적 조회 클릭** → (조회결과 900014 닫기) → 행 스캔 → 행추가 → 셀 채움 → 저장. 조회 전 900014 닫기로 조회 클릭이 모달에 막히는 멈춤도 방지(ConfirmNoDataPopupAsync는 폴링).
     - 빌드 경고 0 / 오류 0, 테스트 10/10 통과. live 재검증 필요.
   - 재실행(`run_20260617_211921.log`)에서 또 멈춤. **결정적**: `BIN 900014 모달 미감지. foreground='UNIMES - UNIMES'` — 화면엔 900014가 떠 있는데 `GetForegroundWindow()`가 메인 창을 돌려줌.
     - 확정된 근본 사실: **900014 모달은 메인 창에 종속(owned)** → 포그라운드 기반 `HasForeignSmallForegroundWindow`로는 **구조적으로 못 잡음**. (진단 로그로 확정)
     - 반성: 잘 되는 971001은 **텍스트 기반 `FindWarningDialog`** 인데, 900014만 포그라운드 방식으로 새로 짠 게 thrash의 원인. 감지 방식을 통일해야 함.
     - 수정(정공법): `FindWarningDialog`를 토큰 받는 공용 `FindMesMessageDialog(tokens)`로 일반화(971001은 `["존재하지"]`로 동작 동일). `ConfirmNoDataPopupAsync`를 971001 핸들러와 **동일한 텍스트 기반**(FindMesMessageDialog(900014 토큰) → 확인 클릭/Enter → 재탐색 검증)으로 재작성. 포그라운드 방식 폐기.
     - 빌드 경고 0 / 오류 0, 테스트 10/10 통과. live 재검증 필요. (조회 클릭 Invoke가 새 모달에 막히는지는 다음 로그로 확인 — 막히면 비차단 클릭으로.)
   - 다음 live 검증:
     - BIN-only dryRun, 미존재 Part 1개(`...W8`) + 존재 Part 1개(`...W7`)로 실행.
     - 기대:
       - `...W8` 조회 후 `BIN 품목 코드 미존재 경고 [확인] 처리`가 찍히고 `[971001]` 경고가 실제로 닫힘.
       - `Undefined` 검색 팝업은 유지되고, 같은 팝업의 `품목 코드` 칸에 `...W7`이 들어가 Enter 조회가 나감(화면상 칸이 W7로 바뀌어야 함).
       - 존재 Part면 `BIN 품목 코드 팝업 확인 Enter 전송` → 팝업 닫힘 → 메인 BIN 조회로 진행.
     - 실패 신호: `BIN 품목 코드 미존재 경고가 닫히지 않음` 또는 `Enter fallback 후에도 모달 잔존` 로그가 찍히면, 그때 해당 시점 화면/덤프를 본다.

1. BIN 자동화 최신 상태(2026-06-17 12:xx~15:xx)
   - 공정명 팝업 덤프: `logs/ui_dump_20260617_121738.txt`
     - 창 이름 `Undefined`, 검색 라벨 `Segment ID`, 입력 Edit는 라벨 우측 첫 Edit.
     - 결과 행은 `DataItem`, `공정ID`/`공정명` Edit 컬럼. `C010` → `Component Test1`, `M050` → `제품 실장 Test`.
   - BIN ID 팝업 덤프: `logs/ui_dump_20260617_121758.txt`
     - 창 이름 `BINID Popup`, 검색 라벨 `BINID`, 결과 행은 `DataItem`, `BIN ID`/`BIN Name` Edit 컬럼.
   - 품목별 BIN 정보 관리의 `품목 ID` 입력칸은 `uniOpenPopup1` 내부 `Edit automation_id=2953814` 또는 `FindEditNextToLabel("품목 ID")`.
   - `RunBinInfoWorkflowAsync` stub은 실제 흐름으로 교체됨:
     - 품목 ID 조회 → 900014 경고 확인 → 행추가 → 공정명 팝업 정확 선택 → 고정 셀 입력 → BIN ID 팝업 정확 선택 → 저장 게이트.
     - 공정명/BIN ID 셀의 검색 버튼은 별도 `Button`으로 안정 노출되지 않아 셀 우측 끝 좌표 클릭으로 팝업을 연다.
   - 검증 완료: `dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj`, `dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj` 통과.
   - 남은 일: live MES에서 BIN-only dryRun으로 `공정명 선택 완료`, `BIN ID 선택 완료`, `BIN 저장 게이트로 저장 생략` 로그 순서를 확인한다.
   - 13:51 live dryRun에서 기존 등록 Part(`RMRDAG58A1B-GPWRRWM7`) 조회 후 멈춤:
     - 원인: 기존 4행이 있어서 900014가 안 뜨는데, `ConfirmNoDataPopupAsync`가 전체 창 descendants에서 경고를 찾다가 블록됨.
     - 수정: 조회 후 같은 Part의 기존 BIN 행이 있으면 `BIN 기존 등록 행 발견...` 로그 후 신규 행추가 없이 skip. 900014 탐색도 작은 팝업 후보/direct child window만 보는 fast path로 축소.
     - 속도 수정: BIN `품목 ID` 입력칸/조회 버튼 캐시를 Part loop 밖으로 이동, BIN PartID 팝업 확인은 descendants 전체 스캔 대신 direct child/top-level fast check로 변경.
   - 판단 기준 정정:
     - `품목 존재 여부`와 `BIN 정보 존재 여부`는 별개다.
     - BIN-only에서는 메인 조회 전 `품목 ID` 옆 검색 팝업(`Undefined`, `품목 코드`)으로 품목을 검색·선택한다.
     - 이 품목 검색 팝업은 UIA로 결과 행/버튼을 깊게 찾지 않고 키보드 흐름으로 처리한다:
       `Ctrl+A → Part 입력 → Enter(조회) → [971001]이면 확인만 닫고 검색 팝업은 유지한 채 다음 Part 입력, 결과가 있으면 Enter(확인)`.
     - `[971001]품목 코드 이(가) 존재하지 않습니다.`가 뜨면 해당 Part만 skip하고, 남아 있는 검색 팝업에서 다음 Part를 이어서 검색한다.
     - 품목정보관리+BIN(`Both`)에서는 품목정보관리에서 이미 미존재 Part를 걸러낸 `validParts`만 BIN으로 넘기므로, BIN 화면의 품목 검색 팝업은 띄우지 않고 직접 입력한다.
     - 품목이 존재하는데 메인 조회 후 900014/행 없음이면 정상 신규 BIN 등록 대상으로 보고 행추가를 진행한다.

2. 최신 코드로 한 번 실행한다.
   - MES 켜진 상태 attach/auto attach 모두에서 로그가 아래 순서로 진행되는지 확인:
     - `Login was not performed by automation. Skipping pre-main Continue popup scan.`
     - `Main UNIMES window`
     - `Skipping post-main Continue popup scan.`
     - `UI dump saved`
     - `품목정보관리 workflow started`
     - `메뉴찾기 버튼 클릭. attempt=1`
     - 이 중 어디서 멈추는지 최신 `logs/run_*.log`를 먼저 본다. 매번 스크린샷을 볼 필요는 없고, UIA가 실제 화면과 다르게 잡힐 때만 스크린샷/덤프를 본다.

3. 위 부트스트랩이 통과하면 미존재 Part 1개 + 정상 Part 1개로 검증한다.
   - 기대 흐름:
     - 잘못된 Part 입력
     - `[971001]품목 코드 이(가) 존재하지 않습니다.` 경고 확인 또는 Enter fallback
     - 열린 `고객사PartID PopUp`은 `취소` 버튼으로 닫힘
     - `고객사PartID 팝업 [취소] 처리`
     - `품목정보관리 part started. part='정상Part'`
     - `품목정보관리 조회 실행. part='정상Part'`
     - `품목정보관리 그리드 행 탐색 시작`
     - `품목정보관리 그리드 행 발견`
     - `품목정보관리 no change` 또는 `품목정보관리 dryRun`
     - 미존재 Part는 `SKIPPED`
     - 다음 정상 Part 검색 진행

## 2026-06-17 조회 속도 캐싱 + 미존재 파트 기파트 복구 재전환

### 배경 (사용자 확인)
- 불량창고 키보드 드롭다운 선택은 **정상 동작 확인**(✅). 4~5개 Part 동시 처리도 정상.
- 두 가지 요청:
  1. 품목명 입력→조회까지 Part당 ~8.5초가 너무 길다 → 1~2초로.
  2. 팝업 취소 흐름이 트리거 불명으로 **전체조회가 나갈 때가 있어** 기파트 입력 복구로 되돌린다.

### 수정 1 — 조회 속도 (`UnimesApp.RunItemInfoWorkflowAsync`)
- 원인: Part마다 `FindItemInfoWindow`/`FindEditNextToLabel`(label+Edit 전체)/`ClickSearch`(Button 전체) 등
  **UIA 전체 descendants 탐색을 4~5회** 반복. 거대한 MES 창에서 회당 수초 → 합산 ~8초.
- `품목명` 입력칸·`조회` 버튼·`품목정보관리` 자식 창은 Part가 바뀌어도 동일하므로 **루프 밖에서 한 번만 찾아 재사용**.
  - `IsElementUsable`로 stale 시에만 재탐색(`ElementNotAvailableException` 가드).
  - 그리드 행 탐색 전 창 재탐색(중복 2회)도 제거. 그리드 내용은 `FindGridRowByProductId`가 매번 새로 읽으므로 영향 없음.
- 미존재 팝업 폴링 `WaitForPartIdPopupAsync` 1200ms → **700ms**(팝업은 Tab 직후 빠르게 뜸).
- 기대: 2번째 Part부터 품목명→조회 구간이 ~1~1.5초. (1번째 Part는 cold 탐색 1회라 다소 김.)

### 수정 2 — 미존재 파트: 팝업 취소 → 기파트 팝업-내부 복구 (커밋 04f7cd3 되돌림)
- `RecoverPartIdPopupByKeyboardAsync`(+`WaitForPartIdPopupResultAsync`/`WaitForPartIdPopupClosedAsync`/`FindPopupProductCodeEdit`) 복원.
  - 경고 닫은 뒤 **열린 `고객사PartID PopUp`의 품목 코드칸에 `recoveryPart`(기파트) 입력 → Enter(조회) → Enter(선택)**.
  - **메인 화면 재조회를 하지 않으므로** 전체조회 트리거를 피한다(STATUS의 "복구는 메인이 아니라 팝업 안에서" 원칙).
  - 원래 미존재 Part는 그대로 `SKIPPED`.
- `CancelPartIdPopupAfterMissingAsync`는 제거(3개 호출부 모두 복구로 전환). `itemInfo.recoveryPart` 재도입(기본 `RMRDAG58A1B-GPWRRWM7`).
- 빌드: 경고 0 / 오류 0.

### 검증 필요 (다음 실행)
- 다중 Part(정상 4~5개 + 미존재 1개)로 `run_unimes_automation_save_test.cmd`.
- 속도: `part started`→`조회 실행` 간격이 2번째 Part부터 ~1~1.5초인지.
- 미존재: `기파트 복구 조회 Enter 전송`→`기파트 키보드 복구 완료` 로그가 뜨고 전체조회/멈춤이 없는지.

## 2026-06-17 불량창고만 값 미설정 — 키보드 드롭다운 선택 (✅ 사용자 확인 완료)

### 증상
- BIN 관리/Turn Key/조립입고는 정상 저장되는데 **불량창고만 빈 값**으로 남았다.
- `5de5973`(commit-후-검증) 적용 후 [run_20260617_093508.log](../logs/run_20260617_093508.log)에서
  `Grid cell value did not commit. column='불량창고', expected='제품 폐기창고', actual=''` 로 드러남.
  - 직전 [run_20260617_093100.log](../logs/run_20260617_093100.log)는 검증이 없어 `ValuePattern`이 성공으로 찍혔지만 실제로는 빈 값이었다.

### 원인 (UI 덤프로 확정)
- [ui_dump_iteminfo_..._093508.txt](../logs/ui_dump_iteminfo_RMRDAG58A1P-GPWRRWM7_20260617_093508.txt) 기준:
  - BIN 관리 콤보: `text_value='ValuePattern:Y'`, ListItem 자식 없음 → Y/N을 ValuePattern에 직접 저장. `SetValue` 통함.
  - 불량창고 콤보(`automation_id='12'`, 컬럼 `BadStorageID` 바인딩): `text_value='ValuePattern:'`(빈값),
    자식이 `[Editor] [valuelist] ValueListItem 0~4` 룩업 항목(0:'', 1:COMPONENT 폐기창고, 2:DIE 폐기창고, 3:RMA창고, 4:제품 폐기창고).
- 즉 **표시텍스트→내부 ID 매핑 룩업 콤보**라 표시텍스트를 `ValuePattern.SetValue`로 넣으면 커밋 시 빈 값으로 버려진다.
  `SelectionItemPattern.Select`/`InvokePattern.Invoke`도 미지원이라 기존 3경로가 전부 막혔다.

### 수정 (`UnimesApp.cs`)
- `ApplyComboCell`에 키보드 선택 경로 추가: list-item Select 실패 후 `ValuePattern` 폴백 **전에** `TrySelectComboByKeyboard` 시도.
  - `EnsureComboExpanded`로 드롭다운 열고(ExpandCollapse 우선, 실패 시 Alt+Down),
    `{UP}`×항목수로 맨 위 고정 후 `{DOWN}`×타깃인덱스로 이동, `{ENTER}`로 선택.
  - 한글 항목은 SendKeys 타이핑이 IME로 불안정해 **인덱스 이동 방식**을 쓴다.
- ListItem이 없는 Y/N 콤보(`target==null`)는 이 경로를 타지 않으므로 기존 정상 동작 그대로.
- 빌드: 경고 0 / 오류 0.

### 검증 필요 (다음 실행)
- `run_unimes_automation_save_test.cmd`로 1건 저장 테스트.
- 기대 로그: `Keyboard combo navigation done. column='불량창고', index=4, items=5` → `Cell set via keyboard ... '제품 폐기창고'`.
- 실패 시: `Keyboard select did not commit ... actual='...'` 의 actual 값으로 다음 가설(드롭다운 미오픈/인덱스/Tab 커밋 필요 여부)을 판단한다.

## 2026-06-17 추가 확인: 미존재 복구 후 정상 Part 조회 뒤 진행 없음

### 증상
- 미존재 Part 팝업은 Enter fallback으로 닫히고, `recoveryPart`로 정상 선택까지 됐다.
- 이후 정상 Part도 화면상 검색까지 됐지만, 수정할 항목이 없으면 `변경 없음` 같은 완료 동작으로 이어지지 않았다.

### 로그 근거
- `run_20260617_090002.log`
  - `기파트 키보드 복구 완료. original='12312414', recovery='RMRDAG58A1B-GPWRRWM7'`
  - 이후 로그 없음.
- 해당 실행의 결과 CSV가 생성되지 않았다.
- 기존 정상 Part 처리 구간에는 Part 시작/조회 클릭/행 탐색/셀 비교 로그가 없어, 화면에서는 진행돼도 어느 UIA 호출에서 멈췄는지 알 수 없었다.

### 판단
- 정상 Part 조회 이후 `HandleMissingPartAsync` 또는 `FindGridRowByProductId`가 `mainWindow` 전체 descendants를 훑으면서 Home Page/공지사항 그리드까지 같이 탐색했다.
- 특히 정상 흐름에서 조회 직후 경고창 전체 스캔은 불필요하고, UIA가 느리면 CMD가 멈춘 것처럼 보일 수 있다.

### 수정
- 각 Part 처리 시작, 조회 실행, 그리드 행 탐색 시작/재시도/발견, 셀 비교 시작 로그를 추가했다.
- 정상 조회 직후의 즉시 미존재 경고 스캔을 제거하고, 행이 없을 때만 미존재 처리로 들어가게 했다.
- 품목명 입력/그리드 행 탐색/스크린샷 범위를 `UNIMES` 메인 전체가 아니라 `품목정보관리` 자식 창으로 좁혔다.
- 빌드 검증:
  - `dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj`
  - 경고 0 / 오류 0

## 2026-06-17 결정 변경: 기파트 복구 대신 팝업 취소 후 SKIPPED

### 변경 이유
- 기파트 복구 방식은 경고 확인 후 품목 코드 입력, 조회 Enter, 선택 Enter 사이에 대기 시간이 필요해 느리다.
- 사용자 확인 기준으로 더 직관적인 흐름은 경고를 닫고 열린 `고객사PartID PopUp`을 `취소`로 닫은 뒤 해당 Part를 SKIPPED 처리하는 것이다.
- 위험했던 이전 방식은 "팝업 취소 후 메인 화면에서 기파트 재조회"였고, 이번 변경은 메인 재조회 없이 바로 다음 Part로 넘어간다.

### 수정
- 미존재 경고/빈 팝업 감지 시:
  - `Enter` fallback 또는 `확인` 버튼으로 경고 닫기
  - `고객사PartID PopUp`의 `취소` 버튼(`4655312`/`1769868`/name=`취소`) 클릭
  - 해당 Part는 `SKIPPED`
- 기파트 복구 전용 코드와 `itemInfo.recoveryPart` 설정을 제거했다.

## 2026-06-17 추가 확인: attach 상태 메뉴 진입 실패 + 미존재 팝업 정지

### 증상
- MES가 이미 켜진 상태에서 실행하면 F3/메뉴찾기 흐름이 너무 빠르게 지나가 `품목정보관리` 탭으로 못 들어가고 Home Page에 남은 채 입력이 시작됐다.
- MES를 꺼둔 상태에서 직접 로그인하면 `품목정보관리` 진입과 품목명 입력까지는 됐지만, 미존재 경고 팝업 후 동작이 멈췄다.

### 로그 근거
- `run_20260617_085044.log`
  - `품목정보관리 screen was not confirmed after F3 navigation. Continuing under normal-flow assumption.`
  - 이후 `품목명 label-based search failed` 및 SendKeys fallback. 스크린샷은 Home Page 상태였다.
- `run_20260617_085226.log`
  - `품목정보관리 screen confirmed.`
  - `고객사PartID 팝업에 결과가 없어 미존재로 보고 경고 확인 후 기파트로 키보드 복구합니다.`
  - 이후 로그 없음. 무거운 경고창 UIA 탐색 또는 Enter fallback 전 단계에서 멈춘 것으로 판단.

### 수정
- 메뉴 진입은 툴바 `메뉴찾기`(`Tool : GoSearch`) 버튼을 우선 사용하고, 실패 시 F3로 대체한다.
- `품목정보관리` 확인을 최대 3회 재시도한다.
- 끝까지 화면 확인이 안 되면 다른 화면에 값을 넣지 않도록 예외로 중단한다.
- 고객사PartID 팝업이 열려 있고 결과 행이 없으면 전체 UIA 경고창 탐색을 생략하고 즉시 Enter fallback으로 경고를 닫는다.
- 당시에는 기파트 복구 Enter 2단계에 로그와 대기를 추가했지만, 이후 기본 흐름은 팝업 취소로 전환했다.
- 빌드 검증:
  - `dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj`
  - 경고 0 / 오류 0

3. `고객사PartID 팝업이 2개 감지되었습니다`가 다시 나오면 먼저 최신 로그와 해당 시점 UI dump만 확인한다.
   - 2026-06-16 23:38/23:53 로그에서는 팝업이 중복 감지되며 기파트 복구가 꼬였다.
   - 이후 코드에는 `CommitField` 직후 PartID 팝업을 먼저 1.2초 기다리는 방어가 들어갔으므로, 같은 현상이 재현되는지부터 확인한다.

4. 동작은 되지만 느리면 그때 딜레이를 줄인다.
   - 현재 우선순위는 속도보다 상태 안정성이다.
   - 특히 `[971001]` 경고는 화면에는 즉시 뜨지만 UIA 노출이 불안정해 Enter fallback을 쓴다.

5. 안정화 후 추가 후보:
   - 그리드/전체 descendants 탐색에 타임아웃 가드 추가.
   - 미존재 처리 후 메인 `품목명` 입력칸 상태를 명시적으로 초기화할지 검토.

---

## 2026-06-17 추가 확인: 시작/attach 후 CMD 정지

### 증상
- MES가 꺼진 상태에서 실행하면 MES 실행 및 로그인창 표시까지는 된다.
- 사용자가 직접 로그인한 뒤 자동화가 이후 동작을 하지 않고 CMD 창이 가만히 있다.
- MES가 이미 켜진 상태에서 시작해도 비슷하게 `detecting`을 한 번 한 뒤 멈춘 것처럼 보인다.

### 로그 근거
- `run_20260617_083935.log`
  - `Existing logged-in UNIMES detected. Skipping launch and login.`
  - `Initial UNIMES window: name='UNIMES - UNIMES' ... rect='L=1912,T=-8,R=3848,B=1040'`
  - `Login screen was not detected. Assuming UNIMES is already logged in or still loading.`
  - 이후 로그 없음.
- `run_20260617_083701.log`
  - `No running UNIMES detected. Launching.`
  - `Initial UNIMES window: name='frmInitial' ... W=100,H=100`
  - `Login screen was not detected. Assuming UNIMES is already logged in or still loading.`
  - 이후 로그 없음.

### 판단
- 모니터 위치 자체가 1차 원인은 아니다.
  - `UNIMES - UNIMES`가 오른쪽 모니터 좌표(`L=1912`)로 정상 감지됐고 `enabled=True`, `visible=True`였다.
- 멈춤 지점은 품목 처리 전이다.
  - 로그가 `Continue` 팝업 처리 완료 로그나 `Main UNIMES window`까지 가지 못했다.
- 원인 후보는 `HandleContinuePopupsAsync`였다.
  - 기존 코드는 로그인 수행 여부와 무관하게 `FindTopLevelButtonByAnyName(["Continue"])`를 호출했다.
  - 이 함수는 모든 top-level window의 descendants를 훑는다.
  - 이미 로그인된 UNIMES 메인창에서 descendants 전체 탐색이 오래 블록되면 CMD가 멈춘 것처럼 보일 수 있다.

### 수정
- 이미 로그인된 상태(`loginPerformed == false`)에서는 pre-main/post-main `Continue` 팝업 스캔을 생략한다.
- `WaitForMainWindowAsync` 안에서도 로그인 수행 시에만 `Continue` 팝업을 확인한다.
- `Continue` 탐색은 전체 top-level descendants 스캔 대신 `FindContinueButton`으로 제한한다.
  - 제목에 `Continue`가 있거나
  - Bizentro 계열의 작은 팝업 후보만 검사
  - `UNIMES - ...` 메인 Shell 창은 제외
- 빌드 검증:
  - `dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj`
  - 경고 0 / 오류 0

---

## ✅ 1. ERP 창 잡힘 — 해결·검증

- 증상: MES 작업인데 ERP(`UNIERP`) 창을 잡아 ERP에서 동작.
- 원인: `IsUnimesCandidate`/`IsProbablyMainWindow`가 프로세스명(`Bizentro.App.MAIN.Shell`)으로
  통과시켜 **제목 검사를 건너뜀** → MES/ERP 둘 다 후보가 되어 `FirstOrDefault`로 비결정적 선택.
  (MES·ERP는 프로세스·클래스·automationId가 전부 같고 **제목만** `UNIMES`/`UNIERP`로 다름)
- 수정:
  - `UnimesApp.IsUnimesCandidate`에 `WindowTitleExcludes` 검사 추가.
  - `Models.cs`/`appsettings.example.json`에 `windowTitleExcludes:["UNIERP"]`.
- 검증: run_20260616_211218.log 등에서 `Main UNIMES window: name='UNIMES - UNIMES'` 확인
  (이전엔 `UNIERP - RMSKR`였음).

---

## 🟡 2. attach 시 포커스 — 견고화 구현(단독 검증 미완)

- 증상: 다른 세션에서 MES를 켜둔 상태로 실행하면, 사용자가 **직접 MES 창을 클릭해야** 진행됨.
- 원인: `BringToFront`가 `SetForegroundWindow`만 호출 → Windows는 **비포그라운드 프로세스의
  SetForegroundWindow 호출을 무시** → SendKeys(Tab/F3)·좌표 클릭이 빗나감.
- 수정:
  - `BringToFront`를 `AttachThreadInput`(포그라운드 스레드에 잠시 붙어 포커스 강제) 방식으로 변경.
    추가 Win32: `GetForegroundWindow`, `GetWindowThreadProcessId`, `AttachThreadInput`, `BringWindowToTop`.
  - 좌표 fallback 클릭 전에도 `BringToFront` 호출.
- 상태: 효과가 있었는지 **단독으로 확인 못 함** — #3(검색 즉시 MES 멈춤)에 막혀 그 뒤 단계를
  관찰할 수 없었다. 단, run_220221에서 툴바 조회가 발사된 것으로 보아 포커스가 일부 동작했을 가능성.

---

## ✅ 툴바 조회 버튼 탐색 — automation_id 보강

- 증상: `Search button was not found by name. 좌표 기반 fallback` → 좌표 클릭(불안정).
- 수정: `ClickSearch`가 이름('조회') 실패 시 **툴바 Query automation_id**(`Tool : Query`)로 탐색
  (`FindButtonByAutomationIdContains`). 좌표는 최후수단.
- 검증: run_220221에서 조회가 실제 발사됨(이게 #3 멈춤을 유발 — 아래 참조).

---

## 🟡 3. 미존재 파트 → MES 멈춤(응답없음) — **경고 확인 후 고객사PartID 팝업 취소로 수정, 실검증 필요**

### 결정적 증거 (run_20260616_220221.log, 없는 파트 `RMRDAG58A1Q-GPWRRWM7`)
```
22:02:49.646 품목정보관리 screen already detected.   ← 검색 시작
   (약 3분 공백 — UIA가 멈춘 MES를 기다리며 블록)
22:05:49.115 [WARN] Control search failed. DataItem ... Value does not fall within the expected range.
22:06:18.852 [WARN] Control search failed. DataItem ... E_UNEXPECTED (0x8000FFFF)
22:06:21.759 [INFO] 미존재 경고 미감지... 현재 top-level 창:
             [cmd.exe | 파일탐색기 | PowerShell | Claude | ... | 메모장]   ← UNIMES 창이 목록에 없음
22:06:21.761 [WARN] Element screenshot failed ... 부모 창이 닫힘
```

### 원인 (정정)
- 잘못된/없는 Part 조회 자체는 `고객사PartID PopUp` 위에 미존재 경고를 띄운다.
- 문제는 경고를 닫은 뒤 팝업을 `취소`로 없애고 메인 화면에서 복구 조회를 이어가던 흐름이다.
- 이때 잘못된 상태/빈 조건이 남아 전체 조회가 나가면 MES가 응답없음 상태가 된다.
- 멈춘 뒤에는 UIA `FindAll`이 이미 죽은 MES를 기다리며 오래 블록되고, 이후 경고/팝업을 안정적으로 읽지 못한다.

### 현재 안전 흐름
- 잘못된/없는 Part 입력 시 이미지처럼 `고객사PartID PopUp` 위에
  `[971001]품목 코드 이(가) 존재하지 않습니다.` 경고가 뜬다.
- 현재 기준 핵심은 경고의 `확인` 또는 Enter fallback 후, 자동으로 열린 `고객사PartID PopUp`을
  `취소` 버튼으로 닫고 해당 Part를 SKIPPED 처리하는 것이다.
- 품목명 입력 후 `Tab`만으로도 이 팝업이 자동으로 뜬다. 이 상태에서 메인 툴바 `조회`를
  추가로 누르면 팝업/조회가 중복 실행될 수 있으므로 금지한다.
- 팝업을 닫은 뒤 메인 화면에서 기파트 재조회로 넘어가면,
  잘못된 상태/빈 조건이 남아 전체 조회가 나가 MES가 먹통이 될 수 있다.

### 시도했다가 폐기/무효가 된 것들 (같은 실수 반복 금지)
1. **팝업 취소 후 메인 재조회** — 무효. 팝업은 닫히지만 메인 화면이 잘못된 조회 상태가 되어 먹통 가능.
2. **메인 화면에서 기파트 재조회** — 무효. 팝업 취소 후에는 재조회하지 않고 다음 Part로 넘어간다.
3. 경고 처리보다 먼저 메인 그리드 `FindAll` 탐색 — 위험. 모달/먹통 상태에서 UIA가 오래 블록될 수 있다.

---

## 현재 수정 방향

- `ClickSearch` 후 메인 그리드 탐색 전에 먼저 `HandleMissingPartAsync`로 미존재 경고를 확인한다.
- `품목명` 입력 후 `Tab`에서 `고객사PartID PopUp`이 자동으로 뜨면 `ClickSearch`를 호출하지 않고
  `HandleOpenPartIdPopupAsync`에서 팝업을 먼저 처리한다.
- prefix가 `RM/TM/BM/CM/RC/TC/BC/CC`가 아닌 값도 UI 입력 전 즉시 스킵하지 않는다.
  미존재 팝업 처리 검증을 위해 먼저 조회 흐름을 태우고, 실제 행이 있을 때만 값 변경을 스킵한다.
- 경고가 UIA에서 보이면 `확인`을 누른다. 경고창도 top-level이 아니라 `UNIMES - UNIMES` 하위 Window일 수 있어 하위 Window까지 탐색한다.
- 경고가 UIA에서 안 보이더라도 자동 팝업이 열려 있고 결과 행이 0개면 미존재로 보고 `Enter` fallback으로
  포커스된 경고 `확인`을 닫는다.
- 이후 `고객사PartID PopUp`은 `취소` 버튼으로 닫고 원래 미존재 Part는 `SKIPPED` 처리한다.

### 추가로 넣으면 좋은 안전장치 (멈춤 재발 대비)
- **UIA 호출 타임아웃 가드**: 그리드 읽기/검색이 N초 내 응답 없으면 즉시 중단(현재는 ~3분 블록).
  최소한 자동화가 멈춘 MES에 끌려가 오래 매달리는 것은 막아준다(MES 자체 멈춤은 못 살림).
- 필요 시 다음 단계에서 미존재 처리 후 메인 `품목명` 입력칸을 명시적으로 비우는 안정화 추가.

---

## 현재 코드 상태 요약 (파일: `src/UnimesAutomation/UnimesApp.cs`)
- 살아있는 관련 메서드: `ClickSearch`, `FindButtonByAutomationIdContains`,
  `HandleMissingPartAsync`, `HandleOpenPartIdPopupAsync`, `CancelPartIdPopupAsync`, `FindPopupRowByProductCode`,
  `FindWarningDialog`(매개변수 없음), `FindPartIdPopup`, `FindByAutomationId`, `BringToFront`(견고화됨).
- 빌드: 경고 0 / 오류 0.
