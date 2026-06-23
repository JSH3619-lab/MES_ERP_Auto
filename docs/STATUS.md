# STATUS

최종 갱신: 2026-06-23

## 현재 기준점

- 브랜치: `feature/mes-gui`
- GUI 기준 실행이 현재 기본 흐름이다.
- `dist/UnimesAutomation.exe`는 단일 실행 파일로 publish해서 실기 테스트한다.
- `app.manifest`가 DPI awareness를 `unaware`로 고정한다.
  - 목적: 실행 중 GUI 창 축소와 좌표 클릭 어긋남 방지.
  - 금지: `Application.SetHighDpiMode(HighDpiMode.PerMonitorV2)` 런타임 호출 재시도 금지.
- GUI 실행은 실제 저장 모드로 고정한다.
- `SafetyGuard`는 유지하며, 저장 외 위험 버튼(`등록/삭제/확정/승인/적용`)을 계속 차단한다.

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
- README와 운영 문서를 현재 기준으로 정리했다.

## 검증 상태

항상 커밋 전 다음 명령을 통과시킨다.

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj -c Release
dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj -c Release --no-restore
dotnet publish .\src\UnimesAutomation\UnimesAutomation.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\dist
```

현재 단위 테스트 기준:

- 25개 통과
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

다음 세션에서 먼저 볼 항목:

- 최신 실기 로그: `logs/run_20260623_153353.log`
- 관련 캡처:
  - `screenshots/20260623_153440_934_menu_search_input_not_found_attempt_1.png`
  - `screenshots/20260623_153447_714_menu_search_input_not_found_attempt_2.png`
  - `screenshots/20260623_153454_463_menu_search_input_not_found_attempt_3.png`
- 현재 실패 지점:
  - F3 입력 후 `품목정보관리` 화면 진입 전, 메뉴찾기 입력칸을 찾지 못해 중단.
  - 캡처상 우측 상단 검색 입력칸이 아니라 좌측 메뉴 패널의 `기준정보` 영역에 선택/포커스가 잡힌 것으로 보인다.
  - 현재 로그에는 실제 focused element 정보가 남지 않으므로 다음 수정에서 먼저 보강해야 한다.
- 권장 수정 방향:
  - F3 또는 메뉴찾기 버튼 클릭 직후 `AutomationElement.FocusedElement`를 로그에 남긴다.
  - focused element가 MES 메인 창 내부의 writable `Edit`이면 그 입력칸에 메뉴명을 직접 설정한다.
  - focused element가 `Edit`이 아니면 메뉴명을 blind SendKeys로 치지 않고 실패 처리한다.
  - 실패 로그에는 focused element 설명과 스크린샷을 같이 남긴다.

## 주의점

- UI Automation 기반이라 화면에 보이는 것과 UIA 트리가 다를 수 있다.
- 실패 분석은 최신 `logs/run_*.log`, 대응 스크린샷, `logs/ui_dump_*.txt` 순서로 한다.
- `logs/`, `screenshots/`, `output/`, `bin/`, `obj/`, `dist/`는 생성물이며 git 추적 대상이 아니다.
- `appsettings.json`은 로컬 설정 파일이며 git 추적 대상이 아니다.
