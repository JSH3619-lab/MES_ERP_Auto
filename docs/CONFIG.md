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
| `launchTimeoutSeconds` / `loginTimeoutSeconds` / `popupTimeoutSeconds` | 대기 시간 |
| `uiDumpMaxDepth` | UI 덤프 최대 깊이 |

## login
| 키 | 의미 |
|---|---|
| `userId` | 로그인 ID |
| `passwordMode` | `manual`(사용자가 직접 입력 대기) / `config`(아래 password 사용) |
| `password` | `config` 모드일 때만. 정책상 평문 저장 주의 |
| `language` / `system` | 언어 / 시스템 선택 |

## safety
| 키 | 의미 |
|---|---|
| `dryRun` | 기본 `true`. 화면 변경 없이 '변경 예정'만 판별 |
| `saveEnabled` | 기본 `false`. 위험 버튼/저장 허용 게이트 |

## workflow
| 키 | 의미 |
|---|---|
| `enabled` | 품목정보관리 워크플로우 실행 여부 |
| `inputPartsPath` | 입력 CSV 경로(입력 다이얼로그가 우선) |
| `searchDelayMilliseconds` | 조회 후 대기(ms) |
| `stopOnFirstFailure` | 첫 실패 시 중단 |
| `showPartInputDialog` | 시작 시 파트 입력 다이얼로그 표시 |
| `showCompletionDialog` | 종료 시 완료 요약 다이얼로그 표시 |

## itemInfo
| 키 | 의미 |
|---|---|
| `menuName` | 대상 메뉴. 기본 `품목정보관리` |
| `binManage` / `turnKey` / `assemblyIn` | 셀 목표값(Y/N) |
| `moduleDefectWarehouse` / `compDefectWarehouse` | 분류별 불량창고 목표값 |
