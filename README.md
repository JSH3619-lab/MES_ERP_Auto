# UNIMES Automation

BIZENTRO UNIMES에서 반복적으로 처리해야 하는 `품목정보관리`와 `품목별 BIN 정보 관리` 작업을 자동화하는 Windows 데스크톱 도구입니다. Part No 목록을 입력하면 UNIMES에 로그인하거나 기존 실행 창에 연결한 뒤, 품목 설정과 BIN 설정을 순서대로 확인하고 필요한 값만 저장합니다.

## 목표

이 프로그램의 목표는 수작업으로 반복하던 MES 품목/BIN 세팅을 안정적으로 자동화하는 것입니다. 최종적으로는 작업자가 Part No 목록만 준비하면, 프로그램이 UNIMES 화면을 조작해 필요한 등록 상태를 맞추고 결과 엑셀을 남기는 흐름을 완성하는 것을 기준으로 합니다.

## 동작 원리

- 이미지 인식이나 OCR이 아니라 Windows UI Automation과 일부 좌표 fallback으로 UNIMES 컨트롤을 조작합니다.
- MES와 ERP가 같은 프로세스/클래스로 뜨기 때문에 창 제목에서 `UNIMES`만 대상으로 삼고 `UNIERP`는 제외합니다.
- 로그인 후 Continue 팝업은 별도로 조작하지 않고, 자동 소멸 후 메인 화면이 감지되면 즉시 다음 단계로 진행합니다.
- 메뉴 이동은 F3 메뉴 검색을 사용하고, Part 입력 뒤에는 Enter 조회를 우선 사용합니다.
- 이미 목표 값과 일치하는 Part는 저장하지 않고 `UNCHANGED`로 넘어갑니다.
- 결과는 `output/result_<timestamp>.xlsx`에 남기고, 완료 알림은 최상단에 표시합니다.

## 주요 기능

- UNIMES 실행 또는 기존 창 attach
- DPAPI 기반 비밀번호 저장
- GUI에서 작업 범위와 Part No 목록 입력
- `품목정보관리` 값 비교/수정
- `품목별 BIN 정보 관리` 기존 행 확인, 신규 행 추가, BIN ID 설정
- 실행 중 정지 요청 시 다음 안전 지점에서 중단
- 실행 로그, 스크린샷, UI dump, 결과 xlsx 생성

## 안전 기준

GUI 실행은 운영 편의를 위해 실제 저장 모드로 고정됩니다. 내부 `SafetyGuard`는 계속 유지하며, 자동화가 예상하지 않은 `등록`, `삭제`, `확정`, `승인`, `적용` 계열 위험 버튼을 누르지 못하게 막습니다. 정상 저장은 `Ctrl+S` 경로를 사용합니다.

## 빌드

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj -c Release
dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj -c Release --no-restore
```

## 단일 exe 생성

```powershell
dotnet publish .\src\UnimesAutomation\UnimesAutomation.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\dist
```

생성 파일:

```text
dist/UnimesAutomation.exe
```

## 실행

```powershell
.\run_unimes_automation.cmd
```

개발 중 직접 실행:

```powershell
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --no-launch
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --dump-only --no-launch
```

## 로컬 설정

필요하면 예시 파일을 복사해 로컬 설정을 만듭니다.

```powershell
Copy-Item .\appsettings.example.json .\appsettings.json
```

`appsettings.json`은 git에 올리지 않습니다. 비밀번호는 설정 창에서 저장하면 Windows DPAPI로 현재 사용자 계정에 암호화되어 저장됩니다.

## 산출물

다음 폴더는 실행 중 생성되며 git 추적 대상이 아닙니다.

- `logs/`
- `screenshots/`
- `output/`
- `dist/`

상세 문서는 [CLAUDE.md](CLAUDE.md)와 `docs/`를 참고합니다.
