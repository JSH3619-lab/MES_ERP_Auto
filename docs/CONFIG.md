# 설정 (`appsettings.json`)

`appsettings.json`이 있으면 그걸, 없으면 `Models.cs`의 `CreateDefault()` 기본값을 사용한다.
예시는 `appsettings.example.json`. `--config <path>`로 다른 파일 지정 가능.

## app
| 키 | 의미 |
|---|---|
| `launchPath` | UNIMES ClickOnce 바로가기(`.appref-ms`) 경로 |
| `windowTitleContains` | 대상 창 제목 포함 토큰. 기본 `["UNIMES"]` |
| `windowTitleExcludes` | **제외** 제목 토큰. 기본 `["UNIERP"]` — ERP 창 배제(필수) |
| `processNameHints` | 약한 신호용 프로세스명 힌트 |
| `launchMode` | `attachOrLaunch`(기본) / `attach`(기존 창만) / `launch`(항상 새로) |
| `launchTimeoutSeconds` / `loginTimeoutSeconds` | 실행/로그인 대기 시간 |
| `uiDumpMaxDepth` | UI 덤프 최대 깊이 |

## login
| 키 | 의미 |
|---|---|
| `userId` | 로그인 ID |
| `passwordMode` | `env`(환경변수 사용) / `config`(아래 password 사용) / `manual`(사용자가 MES 로그인창에 직접 입력) |
| `password` | `config` 모드일 때만. 정책상 평문 저장 주의 |
| `userIdEnvironmentVariable` / `passwordEnvironmentVariable` | `env` 모드에서 읽을 환경변수 이름. 기본 `UNIMES_USER_ID` / `UNIMES_PASSWORD` |
| `language` / `system` | 언어 / 시스템 선택 |

`env` 모드는 `UNIMES_PASSWORD`가 없으면 자동 로그인을 중단한다. `UNIMES_USER_ID`가 없으면 `userId` 값을 사용한다.

## safety
| 키 | 의미 |
|---|---|
| `dryRun` | CLI/덤프 실행용 저장 게이트. GUI 실행은 `false`로 고정 |
| `saveEnabled` | CLI/덤프 실행용 저장 허용 값. GUI 실행은 `true`로 고정 |

GUI에서는 읽기 전용 토글을 제공하지 않는다. 실행 직전 실제 저장 모드로 고정하며, `SafetyGuard`는 저장 외 위험 버튼 차단용으로 남는다.

## workflow
| 키 | 의미 |
|---|---|
| `enabled` | 품목정보관리 워크플로우 실행 여부 |
| `inputPartsPath` | CLI/덤프 실행용 입력 CSV 경로. 일반 GUI 실행은 화면 입력 Part 목록이 우선 |
| `searchDelayMilliseconds` | 조회 후 대기(ms) |
| `stopOnFirstFailure` | 첫 실패 시 중단 |
| `showCompletionDialog` | 종료 시 완료 요약 다이얼로그 표시 |

## 동작 시간 조정 기준

현재 설정 파일에서 바로 조정 가능한 시간 값은 다음이다.

- `app.launchTimeoutSeconds`: UNIMES 창 탐색 대기.
- `app.loginTimeoutSeconds`: 로그인/메인 창 전환 대기.
- `workflow.searchDelayMilliseconds`: 품목/BIN 조회 후 MES 그리드 안정화 대기.

로그인 후 Continue 팝업은 별도로 조작하지 않는다. 자동 소멸 이후 메인 화면이 감지되면 진행한다.

메뉴 탐색 재시도, 팝업 행 검색, BIN 행 추가 확인 같은 더 짧은 대기값은 현재 `UnimesApp.cs` 내부 고정값이다. 다음 타이밍 조정 작업에서는 live 로그 기준으로 필요한 값만 좁게 수정한다.

## itemInfo
| 키 | 의미 |
|---|---|
| `menuName` | 대상 메뉴. 기본 `품목정보관리` |
| `binManage` / `turnKey` / `assemblyIn` | 셀 목표값(Y/N) |
| `moduleDefectWarehouse` / `compDefectWarehouse` | 분류별 불량창고 목표값 |
| `recoveryPart` | **기파트.** 미존재 Part 경고 후 열린 `고객사PartID PopUp`에서 키보드 복구에 사용할 정상 Part. 기본 `RMRDAG58A1B-GPWRRWM7` |

## binInfo
| 키 | 의미 |
|---|---|
| `menuName` | 대상 메뉴. 기본 `품목별 BIN 정보 관리` |
| `moduleProcessKey` / `compProcessKey` | Module/Comp 공정 검색 키. 기본 `M050` / `C010` |
| `binType` | 신규 BIN 행의 BIN Type. 기본 `Normal-1` |
| `retestNo` | 신규 BIN 행의 Retest No. 기본 `0` |
| `binComplete` | 신규 BIN 행의 Bin완료여부. 기본 `Y` |
| `retestTh` | 신규 BIN 행의 Retest TH. 기본 `H` |
