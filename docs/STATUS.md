# STATUS

최종 갱신: 2026-06-22

## 다음 세션 작업 (이어서 할 것) — 브랜치 `feature/mes-gui`

**현재 작업 브랜치 `feature/mes-gui`, HEAD `b8caed1` (미머지/미푸시). `main`이 아님.**

이번 세션(2026-06-22) 진행분:
- `f04b37b` — GUI를 **WinExe**로(검은 콘솔 창 제거, 로그는 GUI EXEC LOG 패널만). 콘솔 인코딩 설정을 try/catch로 감쌈(WinExe=콘솔 없음일 때 `Console.OutputEncoding` 예외로 시작 크래시 나던 것 방지). `IsOwnConsoleWindow` 가드 — exe를 실행한 터미널/콘솔 창 제목이 실행 경로(`...\UnimesAutomation.exe`, "Unimes" 포함)라 MES로 오탐되던 것 차단(`CASCADIA_HOSTING_WINDOW_CLASS`/`ConsoleWindowClass` 또는 제목에 `UnimesAutomation` 포함 시 후보 제외).
- `b8caed1` — **정지 버튼**(협조적 취소). 실행 중 버튼이 `■ 정지`(활성)로 바뀜 → `CancellationToken`을 `UnimesApp.RunAsync`로 전달, 품목/BIN 파트 루프·F3 메뉴 재시도 루프에서 안전 지점 중단. **live 확인 완료**(`logs/run_20260622_170939.log`: 정지 요청 → 메뉴/파트 루프에서 중단 → 워크플로 정상 종료, 결과 xlsx 저장).

### ⚠ 미해결 버그 ① (다음 세션 핵심): 실행 중 DPI 창 축소 + 좌표 어긋남
- **증상:** 실행을 누르면 GUI 창이 갑자기 확 작아지고(최소화 아님, 실제 크기 축소), 좌표 기반 클릭이 빗나간다. **저장 여부와 무관.**
- **영향:** 읽기 기반(품목정보관리 dryRun 셀 비교)은 정상 동작하나, 좌표 클릭이 필요한 부분(F3 메뉴 검색창 활성화·BIN 메뉴 진입)이 빗나가 BIN 화면 진입 실패(`메뉴찾기 입력칸 직접 활성화 실패` → `screen was not confirmed`).
- **원인 추정:** 실행 중 프로세스 **DPI awareness 컨텍스트가 한 번 뒤집힘**(WinForms+WPF GUI가 유발). 시작은 unaware(창 큼)였다가 중간에 aware로 바뀌며 창이 실제 크기로 축소 + UIA 좌표와 실제 마우스 픽셀이 desync → 클릭 빗나감. (창 축소와 좌표 문제는 같은 원인.)
- **금지:** `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` **런타임 호출 → 하드 크래시**(빈 로그, 시작 즉시 종료). 절대 재시도 말 것.
- **다음 시도:** `src/UnimesAutomation/app.manifest`에 `dpiAware`/`dpiAwareness`를 선언해 **프로세스 시작 시점에 awareness 고정**(unaware로 잠가 GUI 도입 전 콘솔과 같은 좌표 환경 유지 → 중간 뒤집힘·축소 차단). 매니페스트는 OS가 프로세스 생성 시 읽으므로 위 런타임 크래시와 무관. 단 고DPI 실기 테스트 필수(코드 빌드만으론 검증 불가). 안 되면 GUI 도입 커밋 `b2abec0` 이전과 비교.

### 보존된 미적용 작업: `git stash@{0}`
- BIN `900013`(데이터가 변경되었습니다/조회? 예/아니오) 팝업 핸들러 — 저장 실패로 dirty된 그리드가 다음 조회에서 막는 것을 [예]로 복구용. (BIN 진입이 안 되니 아직 live 검증 안 됨.)
- 시안 색 변경(`UiTheme.TextDim = Accent`).
- 필요하면 골라 재적용. 정지 버튼은 이미 `b8caed1`로 커밋됨(stash 것 아님).
- 복구: `git stash show -p stash@{0}` 로 확인, `git stash pop`. 롤백 전 tip은 태그 `backup/tip-6a01d97`.

---

## 현재 상태 (`main` 기준 — 자동화 코어)

- 사용자 live 확인 완료: 자동 로그인 -> `품목정보관리` 저장 -> `품목별 BIN 정보 관리` 행추가/입력/저장까지 처음부터 끝까지 정상 완료.
- `main` 기준 최신 기능은 실제 저장 테스트까지 통과했다.
- 기본 안전 설정은 계속 `dryRun=true`, `saveEnabled=false`다.

## 핵심 동작

- MES/ERP 구분은 창 제목으로 한다. `UNIMES`만 대상이고 `UNIERP`는 제외한다.
- 로그인 자동화는 UIA `Edit` 탐지를 우선 사용한다.
- 로그인 입력칸이 UIA에 노출되지 않으면 좌표 fallback을 사용한다.
  - ID/PW는 같은 행에 있다.
  - 왼쪽 칸이 ID, 오른쪽 칸이 PW다.
- `Try again`은 다음 조건을 모두 만족할 때만 처리한다.
  - 상단 서버 오류 문구가 보인다.
  - 상단 `Try again` 링크가 보인다.
  - 서버 선택칸 위치에 `UNIMES` 값이 보이지 않는다.
- BIN 행추가는 버튼 클릭 로그만으로 성공 처리하지 않는다.
  - `BIN 정보 선택` 그리드에 새 행이 실제 생성됐는지 확인한다.
  - 새 행이 없으면 그리드 포커스 후 `Ctrl+Insert` fallback을 사용한다.
- `둘 다` 실행 시 `품목정보관리` 완료창은 중간에 띄우지 않는다.
  - `품목정보관리`와 `품목별 BIN 정보 관리`를 모두 끝낸 뒤 통합 완료창을 한 번만 표시한다.

## 검증된 흐름

- 자동 로그인 정상 화면에서 `Try again` 오탐 없음.
- `Try again` 실제 화면은 서버 오류 문구와 상단 링크 기준으로 처리.
- `품목정보관리`:
  - Part 조회
  - BIN 관리, Turn Key, 조립입고 공정이동여부, 불량창고 설정
  - 저장 게이트 통과 시 저장
- `품목별 BIN 정보 관리`:
  - BIN-only 모드에서는 품목 코드 검색 팝업으로 Part 선택
  - 둘 다 모드에서는 품목정보관리에서 유효한 Part만 BIN 처리
  - 둘 다 모드에서는 BIN까지 끝난 뒤 결과창 1회 표시
  - 900014 no-data 모달 닫기
  - 행추가
  - 공정명, BIN Type, Retest No, Bin완료여부, Retest TH, BIN ID 입력
  - 저장

## 남은 주의점

- UI Automation 기반이라 화면에 보이는 것과 UIA 트리가 다를 수 있다.
- 실패 분석은 최신 `logs/run_*.log`, 대응 스크린샷, `ui_dump_*.txt` 순서로 한다.
- 결과는 `output/result_<timestamp>.xlsx` 한 파일로 저장된다 — 시트 2개(`품목정보관리`/`BIN 정보관리`), MES 폼과 같은 한글 컬럼 + 행별 처리일시.
- `logs/`, `screenshots/`, `output/`, `bin/`, `obj/`는 생성물이며 git 추적 대상이 아니다.
- `appsettings.save-test.json`은 실제 저장 테스트용 로컬 파일이며 git 추적 대상이 아니다.

## 다음 변경 전 체크

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj
dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj
```

실 MES 동작 변경은 빌드/테스트만으로 충분하지 않다. 반드시 live 실행 로그로 확인한다.
