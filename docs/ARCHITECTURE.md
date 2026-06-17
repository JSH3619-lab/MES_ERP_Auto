# 아키텍처 / 코드 맵 / 컨트롤 참조

## 파일 맵 (`src/UnimesAutomation/`)

| 파일 | 역할 |
|---|---|
| `Program.cs` | 진입점. 인자 파싱, 설정 로드(`LoadConfig`), `UnimesApp.RunAsync` 호출 |
| `UnimesApp.cs` | **핵심.** 창 탐색/로그인/팝업/메뉴 이동/품목정보관리 워크플로우 전부 |
| `Models.cs` | 설정 모델(`RootConfig` 등) + `CreateDefault()` 기본값 |
| `SafetyGuard.cs` | 위험 버튼(저장/등록/삭제/확정/승인/적용) 클릭 차단 |
| `PartClassifier.cs` | 파트 분류(Module: RM/TM/BM/CM, Comp: RC/TC/BC/CC) + `ExtractPid` |
| `PartInputDialog.cs` | 시작 시 파트 목록 입력 WinForms 다이얼로그 |
| `CsvFiles.cs` | 입력 CSV 읽기 / 결과 CSV 쓰기 |
| `UiDump.cs` | UI 트리 텍스트 덤프(`--dump-only`) |
| `ScreenshotService.cs` | 요소/데스크톱 스크린샷 |
| `LoggerSetup.cs`, `GlobalUsings.cs` | 로깅, 전역 using |

## 동작 흐름 (`UnimesApp.RunAsync`)

1. `ResolveLaunchMode` → launch / attach / attachOrLaunch
2. 창 탐색: `WaitForUnimesWindowAsync` → `IsUnimesCandidate`(ERP 제외) → `WaitForMainWindowAsync`
3. 로그인 화면이면 `HandleLoginScreenAsync`, 이후 `HandleContinuePopupsAsync`("Continue" 팝업)
4. `RunItemInfoWorkflowAsync`:
   - 파트 목록 로드(입력 다이얼로그 우선, 없으면 CSV) → `NavigateToMenuByF3Async("품목정보관리")`
   - 파트별: `품목명`에 입력 → `CommitField`(Tab) → `ClickSearch`(툴바 조회)
     → 미존재 경고 선확인(`HandleMissingPartAsync`) → `FindGridRowByProductId(pid)`
     → 셀 비교/적용(`ApplyComboCell`) → (저장 게이트)
   - `CommitField` 직후 `고객사PartID PopUp`이 자동으로 뜨면 메인 `ClickSearch`를 건너뛰고
     `HandleOpenPartIdPopupAsync`에서 팝업부터 처리
   - **미존재 경고/빈 결과 감지 시** 경고 `확인`(UIA 미감지 시 Enter fallback)
     → `고객사PartID PopUp` 품목 코드에 `recoveryPart` 입력 → Enter → Enter → 해당 Part는 SKIPPED
5. 결과 CSV + 완료 다이얼로그

## 창 식별 (중요)

| 시스템 | 창 제목(Name) | automationId | class | process |
|---|---|---|---|---|
| **MES** | `UNIMES - UNIMES` | `ShellForm` | `WindowsForms10.Window.8.app...` | `Bizentro.App.MAIN.Shell` |
| **ERP** | `UNIERP - RMSKR` | `ShellForm` | (동일) | (동일) |

→ 제목만 다름. `windowTitleExcludes:["UNIERP"]`로 ERP 배제. 기동 중엔 제목이 빈 문자열일 수 있어
제외 토큰 방식이 안전(빈 제목은 제외에 안 걸리고, 프로세스 힌트로 MES만 통과).

## 주요 컨트롤 automation id (UI 덤프 기준)

**툴바(메인 창)**
- 조회: `[Toolbar : ToolBar_Main Tools] Tool : Query - Index : 0 ` (name `조회`)
- 저장: `... Tool : Save ...` (보통 disabled)

**`품목정보관리` 검색부**
- `품목명` 라벨 + 우측 Edit(라벨 기준 좌표로 탐색), 옆에 팝업 여는 윈도우 아이콘 `uniButton_OpenPopup`

**`고객사PartID PopUp`** (top-level 또는 `UNIMES - UNIMES` 자식 Window, name=`고객사PartID PopUp`)
- 품목 코드 입력칸: `1441912` 또는 `2427784` / 내부 Edit `txtCd_EmbeddableTextBox`
- 둘째 입력칸: `7079676`
- Like 콤보: `10161186` (Like / >=)
- 조회: `9899154` 또는 `6621524` / 확인: `3542176` 또는 `10357766` / 취소: `4655312` 또는 `1769868`
- 결과 그리드 행: `ControlType.DataItem` (Name = 품목코드 값), 컬럼 `품목코드`(PRODUCTID)/`품목명`(PRODUCTNAME)

## 미존재 경고창
- `[971001]품목 코드 이(가) 존재하지 않습니다.` — `확인` 버튼. 메인/ERP 창과 별개의 모달.
  `FindWarningDialog`가 "UNIMES/UNIERP 제목 아님 + '존재하지' 텍스트 + 확인 버튼"으로 식별.
- 이 경고도 top-level이 아니라 `UNIMES - UNIMES` 하위 Window로 잡힐 수 있어 하위 Window까지 탐색한다.
- UIA 트리에 안정적으로 노출되지 않을 수 있으므로, 자동 팝업이 열려 있고 결과 행이 없으면
  미존재로 보고 `Enter` fallback으로 경고를 닫은 뒤 기파트 키보드 복구를 수행한다.
