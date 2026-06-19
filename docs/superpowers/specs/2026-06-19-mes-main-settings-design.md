# MES 자동화 — 메인/설정 화면 + 분류별 설정 + 결과 리포트 설계

작성일: 2026-06-19
대상: MES 전용(UNIMES). ERP는 범위 밖.

## 1. 목표

콘솔 + 순차 다이얼로그(작업범위 → 파트입력) 방식을 **상시 메인 창** 하나로 대체하고,
지금 `appsettings.json`에 직접 손대야 하던 값들(로그인, 분류별 품목정보/BIN 설정)을
**설정 창에서 직접 편집**할 수 있게 한다. 테스트 후 검증용 **엑셀 결과 리포트**를 남긴다.

## 2. 배경 (현재 구조에서 바꾸는 이유)

- 현재 진입은 `Program.Main`(콘솔) → `WorkScopeDialog` → `PartInputDialog` → `UnimesApp.RunAsync`.
- 설정은 `appsettings.json`이 있으면 사용, 없으면 `Models.cs`의 `CreateDefault()`.
- **설정 파일이 둘로 나뉜 진짜 이유**(확인 결과): 읽기전용 때문이 아니라
  ① 저장 안전장치(기본 런처 = 내장 기본값 `dryRun=true/saveEnabled=false` 안전 미리보기,
  save-test 런처 = `appsettings.save-test.json`로 `dryRun=false/saveEnabled=true` 실제 저장),
  ② 비밀번호 격리(`appsettings.save-test.json`에 평문 PW가 있어 gitignore).
- 새 설계가 두 이유를 흡수한다 → **단일 `appsettings.json`로 통일**.
  - 비밀번호는 DPAPI 암호화로 이동(평문 제거) → 격리용 별도 파일 불필요.
  - 저장 안전장치는 메인 창 런타임 토글 + **켤 때 확인 다이얼로그**로 마찰 유지.
  - `appsettings.save-test.json`과 `run_unimes_automation_save_test.cmd`는 제거.

## 3. 실행 흐름 (Approach 2 — 창 안 진행상태)

전용 PC(무인)에서 돌릴 것을 전제로 한다. 포커스 경합 위험의 주원인(사람이 실행 중 창 조작)이
없으므로 창 안 진행상태 표시를 채택.

1. `Program.Main`: 인자 파싱. `--dump-only`/`--help`는 기존처럼 콘솔에서 처리 후 종료.
   그 외에는 `Application.Run(new MainForm(...))`로 GUI 진입(`--config`, `--no-launch`는 GUI와
   호환 — config 경로 지정·기존 MES attach 의미 유지).
2. `MainForm`이 파트 입력·작업범위·안전모드·설정 진입·실행을 담당.
3. `실행` 클릭 → 입력 컨트롤 비활성화 → **백그라운드 스레드**에서 `UnimesApp.RunAsync` 실행.
4. 로그/진행상태는 `IProgress<string>`로 전달받아 `Control.BeginInvoke`로 메인 창 상태 패널에
   출력(표시 줄 수 제한). 완료 시 결과 요약 표시 후 컨트롤 재활성화.
5. 운영 규칙(실행 중 메인 창을 일부러 앞으로 끌어올리지 않기)은 안내 문구로 표기.

## 4. 설정 모델 재구성 (`Models.cs`)

플랫 구조를 분류별로 재편한다. 신규 형태(개념):

```jsonc
{
  "login":  { "userId", "passwordMode": "dpapi", "passwordEncrypted", "language", "system" },
  "safety": { "dryRun", "saveEnabled" },                      // 메인에서 표시/토글
  "app":    { "launchPath", "windowTitleContains", "windowTitleExcludes",
              "processNameHints", "launchTimeoutSeconds", "loginTimeoutSeconds",
              "popupTimeoutSeconds", "uiDumpMaxDepth", "launchMode" },   // 고급
  "workflow": { "enabled", "inputPartsPath", "searchDelayMilliseconds",
                "stopOnFirstFailure", "showCompletionDialog" },
  "options": {                                                 // 드롭다운 목록(신규)
    "defectWarehouses": ["제품 폐기창고", "COMPONENT 폐기창고"],
    "binTypes":   ["Normal-1"],
    "retestThs":  ["H", "L"],
    "binCompletes": ["Y", "N"]
  },
  "categories": {
    "dramModule": {
      "itemInfo": { "binManage": "Y", "turnKey": "N", "assemblyIn": "Y",
                    "defectWarehouse": "제품 폐기창고" },
      "binInfo":  { "processSearchKey": "M050",
                    "rows": [ { "processName": "M050", "binType": "Normal-1",
                                "retestNo": "0", "binComplete": "Y", "retestTh": "H" } ] }
    },
    "dramComp": {
      "itemInfo": { "binManage": "Y", "turnKey": "N", "assemblyIn": "Y",
                    "defectWarehouse": "COMPONENT 폐기창고" },
      "binInfo":  { "processSearchKey": "C010",
                    "rows": [ { "processName": "C010", "binType": "Normal-1",
                                "retestNo": "0", "binComplete": "Y", "retestTh": "H" } ] }
    }
  },
  "global": { "recoveryPart": "RMRDAG58A1B-GPWRRWM7",
              "itemInfoMenuName": "품목정보관리",
              "binInfoMenuName": "품목별 BIN 정보 관리" }   // 공유값(고급)
}
```

신규/변경 모델 타입:
- `CategoryConfig { ItemInfoValues ItemInfo; BinInfoValues BinInfo }`
- `ItemInfoValues { BinManage, TurnKey, AssemblyIn, DefectWarehouse }`
- `BinInfoValues { ProcessSearchKey; List<BinRowConfig> Rows }`
- `BinRowConfig { ProcessName, BinType, RetestNo, BinComplete, RetestTh }`
- `OptionsConfig { DefectWarehouses, BinTypes, RetestThs, BinCompletes }`
- `GlobalConfig { RecoveryPart, ItemInfoMenuName, BinInfoMenuName }`
- `LoginConfig`: `passwordMode` 에 `"dpapi"` 추가, `passwordEncrypted`(base64) 추가.
- 기존 `ItemInfoConfig`/`BinInfoConfig`(플랫)는 제거. `WorkScope` enum, `PartRequest`, `PartResult` 유지.

규칙:
- DRAM은 두 분류 모두 `rows` 1개 고정. `행 추가/삭제`는 UI·모델에 만들어두되 미사용(향후 Flash 대비).
- **BIN ID는 모델에 저장하지 않음** — 기존 `BinIdResolver` 자동 산출 유지(Flash 추가 시 재논의).

## 5. 설정 영속화 + 보안 (`ConfigStore.cs`, `SecretProtector.cs`)

- `ConfigStore`: `appsettings.json` **읽기/쓰기**. 로드 시 누락 필드는 기본값 폴백.
  - 구버전(=`categories` 없음) 감지 시 플랫 `itemInfo`/`binInfo` 값으로 `categories` 구성
    (모듈/컴포넌트 불량창고는 `moduleDefectWarehouse`/`compDefectWarehouse`에서 매핑).
  - 구버전 `passwordMode:"config"` 평문 PW가 있으면 첫 저장 시 DPAPI로 암호화 후 평문 제거.
- `SecretProtector`: `System.Security.Cryptography.ProtectedData` 래퍼. 기본 `CurrentUser` 스코프
  (그 PC·그 Windows 계정에서만 복호화). 다른 계정/PC로 복사 시 복호화 실패 → 설정 창에서 재입력.
- `appsettings.json`은 계속 gitignore(이미 처리됨). 평문 비밀번호는 어떤 파일에도 저장하지 않는다.

## 6. 메인 창 (`MainForm.cs`)

- 제목 `UNIMES 자동화`.
- **Part No** 멀티라인 입력(현재 `PartInputDialog` 파싱 규칙 재사용: 줄/쉼표/공백 구분, 중복 제거).
- **작업 범위** 3선택: `통합품목관리`(품목정보관리 → 품목 BIN정보 관리 순차) / `품목정보관리` /
  `품목 BIN정보 관리`. (내부 `WorkScope` 매핑: Both / ItemInfo / BinInfo)
- **안전 모드 배지**: `안전 모드 · 변경 미리보기 (저장 잠금)`. `변경`으로 토글.
  - 실제 저장(`dryRun=false` & `saveEnabled=true`)을 켤 때 **확인 다이얼로그**
    ("정말 실제 MES에 저장을 켜시겠습니까?") 1회. 끄는 방향은 확인 없이 허용.
- `설정` → `SettingsForm` 모달. `실행` → 백그라운드 실행 시작.
- 하단 **진행상태 패널**(로그 출력, 줄 수 제한) + 완료 요약.

## 7. 설정 창 (`SettingsForm.cs` + 컨트롤들)

- 좌측 메뉴: 로그인 정보 · DRAM Module · DRAM Comp · 고급.
- **로그인 정보**: 아이디 / 비밀번호(표시 토글, DPAPI 저장) / 언어(드롭) / 시스템(드롭).
- **분류 탭(Module/Comp)**: 동일 레이아웃 재사용 `CategorySettingsControl` 1개를 각 분류 설정에 바인딩.
  - `품목정보관리` 1행 테이블: BIN 관리 · Turn Key · 조립입고(Y/N 드롭) · 불량창고(드롭).
  - `BIN 정보관리` 테이블: 공정명 · BIN Type(드롭) · Retest No · BIN 완료여부(드롭) ·
    Retest TH(드롭) · BIN ID(자동 산출·읽기전용) + 공정 검색키, `행 추가/삭제`.
  - 드롭다운 목록은 `options`에서 공급.
- **고급**: launchPath, 창 제목 토큰, 타임아웃, `options` 목록 편집, `global`(recoveryPart, 메뉴명).
- `저장`(→ `ConfigStore` 기록) / `취소`.

## 8. 결과 리포트 (`ResultWorkbook.cs`, ClosedXML)

현재 `CsvFiles.WriteResults`(품목정보관리 전용, 영문 컬럼)를 엑셀로 교체. 입력 CSV 읽기는 유지.

- 파일: `output/result_<timestamp>.xlsx`, **시트 2개**.
- **품목정보관리 시트**: 품목 · 분류 · BIN 관리 · Turn Key · 조립입고 · 불량창고 · 저장 · 상태 ·
  메시지 · 처리일시.
- **BIN 정보관리 시트**: 품목 · 분류 · 공정명 · BIN Type · Retest No · BIN 완료여부 · Retest TH ·
  BIN ID · 저장 · 상태 · 메시지 · 처리일시.
- BIN 결과는 현재 수집되지 않으므로 `BinResult` 모델 + `UnimesApp` 내 BIN 결과 수집을 신규 추가.
- `처리일시`는 행별 타임스탬프(파일명 timestamp와 별개).

## 9. UnimesApp 연결 (`UnimesApp.cs`)

- 파트별 `PartClassifier` 결과로 `categories.dramModule`/`dramComp` 설정을 선택해 사용
  (현재 `config.ItemInfo.*` / `config.BinInfo.*` 플랫 읽기를 분류별 읽기로 교체).
- `binInfo.rows` 순회(현재 1행). `BinIdResolver.Resolve`는 분류 `processSearchKey`를 받아 그대로 사용.
- BIN 처리 결과를 `BinResult`로 수집. 메뉴명은 `global`에서 가져옴.
- `RunAsync`에 `IProgress<string>?`를 추가해 진행상태를 메인 창으로 전달(콘솔 모드면 null).
- 안전 게이트(`SafetyGuard`)는 그대로 유지(런타임 `safety` 값 기준).

## 10. 신규 / 변경 / 제거 파일

신규:
- `MainForm.cs`, `SettingsForm.cs`, `CategorySettingsControl.cs`,
  `ConfigStore.cs`, `SecretProtector.cs`, `ResultWorkbook.cs`.

변경:
- `Models.cs`(모델 재구성, `BinResult` 추가), `Program.cs`(GUI 기본 진입),
  `UnimesApp.cs`(분류별 설정·BIN 결과 수집·IProgress), `CsvFiles.cs`(입력 읽기만 유지),
  `appsettings.example.json`(새 형태), `UnimesAutomation.csproj`(패키지 추가),
  문서(`CLAUDE.md`, `docs/CONFIG.md`, `docs/ARCHITECTURE.md`, `docs/STATUS.md`).

제거(내 변경으로 고아가 되는 것):
- `WorkScopeDialog.cs`, `PartInputDialog.cs`(메인 창으로 흡수),
  `appsettings.save-test.json`, `run_unimes_automation_save_test.cmd`.

## 11. 의존성

- `ClosedXML` (xlsx 생성, 순수 관리형, Excel 설치 불필요).
- `System.Security.Cryptography.ProtectedData` (.NET 8에서 DPAPI 사용 NuGet).

## 12. 범위 밖

- ERP 일체.
- Flash 제품 규칙 / 다중 BIN ID 산출 로직(자리만 마련, 미구현).
- 추가 파트 종류의 분류 규칙(`PartClassifier` 확장).
- 창 안 로그 스트리밍의 고도화(진행률 바 등)는 이후 별도.

## 13. 검증

- 빌드/테스트: `dotnet build`, `dotnet test`.
- 설정 왕복: 설정 창에서 값 변경 → 저장 → `appsettings.json` 반영 → 재시작 시 로드 확인.
- DPAPI: 비밀번호 입력·저장 후 파일에 평문이 없고 base64만 있는지, 재시작 후 자동 로그인 동작 확인.
- 안전 가드: 안전 모드 ON에서 저장 버튼 차단, 토글 ON 시 확인 다이얼로그 동작 확인.
- 리포트: 통합 실행 후 `result_<timestamp>.xlsx`에 두 시트·한글 컬럼·처리일시 생성 확인.
- 실 MES 동작 변경은 빌드/테스트만으로 불충분 → live 실행 로그로 확인(프로젝트 원칙).
