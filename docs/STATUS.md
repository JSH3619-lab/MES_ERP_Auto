# STATUS

최종 갱신: 2026-06-22

## 현재 기준점 — 브랜치 `feature/mes-gui`

- 현재 HEAD: `6b971bf` (`origin/feature/mes-gui`와 동기화됨).
- 사용자 live 확인 완료: manifest 기반 DPI 고정 후 `dist/UnimesAutomation.exe` 단일 실행 파일로 정상 동작 복구.
- `src/UnimesAutomation/app.manifest`가 프로세스 시작 시점에 DPI awareness를 `unaware`로 고정한다.
  - 목적: 실행 중 GUI 창 축소와 좌표 클릭 어긋남 방지.
  - 금지: `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` 런타임 호출 재시도 금지. 이전에 시작 크래시를 유발했다.
- `WinExe` 실행, GUI 로그 패널, 정지 버튼(협조적 취소), 단일 xlsx 결과 출력까지 현재 기준 기능으로 본다.

## 다음 변경 범위

이 지점 이후 우선순위는 다음 두 가지다.

1. GUI 디자인 요소 조정
   - 창 크기, 여백, 색상, 폰트 크기, 버튼/로그 패널 배치 등.
2. 세부 동작 시간 조정
   - 조회 후 대기, 메뉴 탐색 재시도, 팝업/그리드 행 대기, 클릭 후 안정화 시간 등.

자동화 핵심 흐름은 정상 기준점으로 고정한다. 디자인/타이밍 작업 중에는 MES/ERP 식별, 저장 게이트, 미존재 Part 복구, 결과 xlsx 출력 흐름을 불필요하게 리팩터링하지 않는다.

## 현재 검증 상태

- `dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj -c Release` 성공.
- `dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj -c Release --no-restore` 성공.
  - 결과: 22개 통과, 실패 0개.
- 단일 exe publish 성공:
  ```powershell
  dotnet publish .\src\UnimesAutomation\UnimesAutomation.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\dist
  ```
- `dist/`는 git ignore 대상이다. 다른 PC에서 테스트할 때는 위 publish 명령으로 다시 생성한다.

## 핵심 동작

- MES/ERP 구분은 창 제목으로 한다. `UNIMES`만 대상이고 `UNIERP`는 제외한다.
- 자동화 대상은 MES만이다. ERP는 같은 프로세스/클래스라서 제목 제외 조건이 필수다.
- 기본 안전 설정은 계속 `dryRun=true`, `saveEnabled=false`다.
- 저장/등록/삭제/확정/승인/적용 계열 버튼은 `SafetyGuard`가 저장 게이트 통과 전 차단한다.
- 실행 중 `정지` 버튼은 현재 Part의 안전 지점에서 협조적으로 중단한다.
- 결과는 `output/result_<timestamp>.xlsx` 한 파일로 저장된다.
  - 시트: `품목정보관리`, `BIN 정보관리`
  - MES 폼과 같은 한글 컬럼과 행별 처리일시를 포함한다.

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
  - 900014 no-data 모달 닫기
  - 행추가
  - 공정명, BIN Type, Retest No, Bin완료여부, Retest TH, BIN ID 입력
  - 저장 게이트 통과 시 저장
- `둘 다` 실행 시 `품목정보관리` 완료창은 중간에 띄우지 않는다.
  - `품목정보관리`와 `품목별 BIN 정보 관리`를 모두 끝낸 뒤 통합 완료창을 한 번만 표시한다.

## 남은 주의점

- UI Automation 기반이라 화면에 보이는 것과 UIA 트리가 다를 수 있다.
- 실패 분석은 최신 `logs/run_*.log`, 대응 스크린샷, `logs/ui_dump_*.txt` 순서로 한다.
- `logs/`, `screenshots/`, `output/`, `bin/`, `obj/`, `dist/`는 생성물이며 git 추적 대상이 아니다.
- `appsettings.json`, `appsettings.save-test.json`은 로컬 설정 파일이며 git 추적 대상이 아니다.

## 참고 백업

- 이전 WIP 스냅샷은 `origin/backup/session-stash`에 보존되어 있다.
- 현재 정상 기준은 `feature/mes-gui`의 `6b971bf` 이후 상태다. 백업 브랜치의 고유 변경은 필요할 때 diff로 골라 적용한다.
