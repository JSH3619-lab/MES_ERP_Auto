# 실행 / 테스트 / 로그 확인

## 빌드와 단위 테스트

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj -c Release
dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj -c Release --no-restore
```

## 단일 exe publish

`dist/`는 git ignore 대상이다. 배포/실기 테스트용 단일 exe가 필요하면 로컬에서 다시 만든다.

```powershell
dotnet publish .\src\UnimesAutomation\UnimesAutomation.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o .\dist
```

## 실행 모드

```powershell
# 일반 실행
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj

# 이미 실행/로그인된 MES에 붙기
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --no-launch

# UI 트리만 덤프
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --dump-only --no-launch

# 특정 설정 파일 사용
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --config .\appsettings.json
```

일반 GUI 실행은 실제 저장 모드로 고정된다. 테스트 Part를 넣기 전에 UNIMES 대상 환경과 Part 목록을 확인한다.

`--dump-only` 또는 별도 CLI 실행에서만 `safety.dryRun/saveEnabled` 설정을 직접 확인한다.

## 산출물 위치

| 종류 | 경로 |
|---|---|
| 실행 로그 | `logs/run_YYYYMMDD_HHMMSS.log` |
| UI 덤프 | `logs/ui_dump_*.txt` |
| 스크린샷 | `screenshots/*.png` |
| 결과 xlsx | `output/result_*.xlsx` |

위 폴더는 모두 git ignore 대상이며 필요하면 삭제해도 다음 실행 때 다시 생성된다.

## 실행 로그 확인

- 대상 창은 `UNIMES - ...`여야 한다. `UNIERP`가 잡히면 안 된다.
- 로그인 실패 화면은 실제 서버 오류 + `Try again` 링크 조합으로만 처리되어야 한다.
- 자동 로그인 시 ID/PW가 같은 행의 좌/우 입력칸에 들어갔는지 확인한다.
- 품목정보관리 미존재 Part는 경고 확인 후 SKIPPED로 끝나야 한다.
- 로그인 후 Continue 팝업은 자동 조작하지 않는다. 로그상 메인 화면 감지 후 workflow가 시작되는지 확인한다.
- BIN 행 추가는 클릭 성공 로그가 아니라 신규 `BIN 정보 선택` 행 발견 로그가 기준이다.

## 실제 MES 회귀 시나리오

1. 자동 로그인: 로그인 전 화면에서 ID/PW, 언어, 시스템 값이 보존되는지 확인.
2. 작업 범위 `품목정보관리만`: 정상 Part와 미존재 Part를 섞어 실행.
3. 작업 범위 `BIN 정보 관리만`: 품목 코드 팝업 선택 후 기존 행/신규 행 케이스 실행.
4. 작업 범위 `둘 다`: 품목정보관리 완료창 없이 BIN 정보 관리로 이어지고, 전체 종료 후 결과창이 한 번만 뜨는지 확인.
5. 실행 중 GUI 창 크기가 줄어들지 않고 좌표 클릭이 빗나가지 않는지 확인.
6. 정지 버튼: 실행 중 `정지` 요청 후 안전 지점에서 중단되고 결과 파일이 남는지 확인.
7. 저장 테스트: GUI는 실제 저장 모드이므로 테스트 Part와 대상 MES 환경을 확인한 뒤 제한 실행.
