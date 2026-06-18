# 아키텍처 / 코드 맵 / 컨트롤 참조

## 파일 맵 (`src/UnimesAutomation/`)

| 파일 | 역할 |
|---|---|
| `Program.cs` | 진입점. 인자 파싱, 설정 로드, 작업 범위/파트 입력 다이얼로그 호출 |
| `UnimesApp.cs` | 핵심 자동화. 창 탐색, 로그인, 팝업, 메뉴 이동, 품목정보/BIN 워크플로우 |
| `Models.cs` | 설정 모델과 기본값 |
| `SafetyGuard.cs` | 저장/등록/삭제/확정/승인/적용 계열 위험 버튼 차단 |
| `PartClassifier.cs`, `BinIdResolver.cs` | 파트 분류와 BIN ID 목표값 계산 |
| `WorkScopeDialog.cs`, `PartInputDialog.cs` | 시작 전 사용자 선택 UI |
| `CsvFiles.cs` | 입력 CSV 읽기와 결과 CSV 쓰기 |
| `UiDump.cs`, `ScreenshotService.cs`, `LoggerSetup.cs` | 진단 산출물과 로깅 |

## 실행 흐름

1. `Program.Main`이 설정을 로드하고 로그/스크린샷/안전가드를 준비한다.
2. 작업 범위 다이얼로그에서 `품목정보관리만`, `BIN 정보 관리만`, `둘 다` 중 하나를 받는다.
3. 파트 입력 다이얼로그 또는 CSV에서 Part No 목록을 받는다.
4. `UnimesApp.RunAsync`가 UNIMES 창에 attach 또는 launch하고, 로그인 화면이면 자동 로그인한다.
5. 선택 범위에 따라 `품목정보관리`와 `품목별 BIN 정보 관리`를 순서대로 실행한다.
6. 결과 CSV를 `output/`에 저장하고 완료 요약을 표시한다.
   - `품목정보관리만`, `BIN 정보 관리만`: 해당 작업 종료 후 완료창 1회.
   - `둘 다`: `품목정보관리` 중간 완료창 없이 BIN까지 끝낸 뒤 통합 완료창 1회.

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
