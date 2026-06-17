# 품목별 BIN 정보 관리 자동화 — 설계 (Design)

작성: 2026-06-17. 상태: 설계 승인 대기 → 승인 후 구현계획(writing-plans).

## 1. 목적 / 범위

`품목정보관리` 자동화에 이어 **`품목별 BIN 정보 관리`** 화면 자동화를 추가한다.
신규 파트 등록 시 BIN 행 1개를 추가하고 분류·용량에 맞는 값을 채운 뒤 저장한다.

- 평소 운영: `품목정보관리`에서 대상 파트 전부 처리 → `품목별 BIN 정보 관리`로 이동해 같은 파트 처리.
- 테스트 편의: 시작 시 **작업 선택 창**(품목정보관리만 / BIN만 / 둘 다)을 띄운다.
- 두 작업은 **같은 Part 목록을 공유**한다(신규 등록 시나리오).

## 2. 전체 흐름

```
Program.Main
  └ WorkScopeDialog (신규)         → scope = ItemInfo | BinInfo | Both
  └ PartInputDialog (기존, 공유)   → parts
  └ UnimesApp.RunAsync
       ├ (scope에 ItemInfo) RunItemInfoWorkflowAsync(parts) → validParts 산출
       └ (scope에 BinInfo)  RunBinInfoWorkflowAsync(binParts)
                binParts = Both이면 validParts, BinInfo 단독이면 parts 전체
```

- **유효성 판단:** `품목정보관리`에서 조회 성공 = 정상 파트. `둘 다` 모드에서 BIN은 정상 파트만 처리한다.
  BIN 단독 모드는 유효성 판단 기준이 없어 입력 파트 전체를 처리한다(사용자 합의: 항상 둘 다 같이 쓰므로 현재는 허용).

## 3. 컴포넌트

### 3.1 WorkScopeDialog (신규, `PartInputDialog`와 동형 WinForms)
- 라디오/버튼 3개: `품목정보관리만`, `BIN 정보 관리만`, `둘 다`. 취소 시 종료.
- 반환: `enum WorkScope { ItemInfo, BinInfo, Both }`.
- 노출 위치: `Program.Main`에서 PartInputDialog **앞에** 표시. 결과는 `config.Workflow.RuntimeWorkScope`에 저장.
- `showWorkScopeDialog=false`(또는 dumpOnly)면 표시하지 않고 기본 `ItemInfo`.

### 3.2 BinIdResolver (신규, **순수 로직 / 단위테스트 대상**)
UIA 의존 없는 순수 클래스. 파트번호 → 처리에 필요한 파생값.

```
record BinInfoTarget(PartClass Class, string ProcessSearchKey, string BinIdName);
static BinInfoTarget? Resolve(string partNo, BinInfoConfig cfg);
```

- `ProcessSearchKey`: Module → `M050`, Comp → `C010` (cfg에서).
- `BinIdName`: §4 규칙으로 계산. 분류 실패 / 용량코드 파싱 실패 → `null`(호출부가 스킵).
- 분류는 기존 `PartClassifier.Classify` 재사용(Module=RM/TM/CM/BM, Comp=RC/TC/CC/BC).

### 3.3 RunBinInfoWorkflowAsync (UnimesApp 신규 메서드, 기존 헬퍼 재사용)
파트 1건 처리 절차:

1. `NavigateToMenuByF3Async(mainWindow, cfg.MenuName)` — `품목별 BIN 정보 관리` 진입(기존 메뉴찾기/F3 재사용).
2. `품목 ID` 입력칸에 파트번호 입력 → commit(Tab) → 조회. (자동 PartID 팝업 뜨면 기존 `HandleOpenPartIdPopupAsync` 재사용.)
3. **`[900014] 검색된 Data가 없습니다`** 경고 → `확인`(UIA 미감지 시 Enter fallback). 신규 파트의 정상 신호다.
4. **행추가:** `Ctrl+Insert`.
5. **공정명:** 공정명 셀 우측 검색 버튼 → 검색 팝업의 `Segment ID`에 `ProcessSearchKey`(M050/C010) 입력 → Enter(조회) → Enter(선택). (M050/C010은 각각 결과 1개: 제품 실장 Test / Component Test1.)
6. **고정 셀 입력:** `BIN Type=Normal-1`, `Retest No=0`, `Bin완료여부=Y`, `Retest TH=H` (기존 `ApplyComboCell`/`SetElementText` 재사용).
7. **BIN ID:** 검색 버튼 → 팝업에서 `BinIdName` 검색 → **정확히 일치하는 행 선택**.
   - 일치 행 없음 → §5 처리(해당 파트 ERROR/SKIP, 저장 안 함).
8. `BinIDType`은 BIN ID 입력 시 **자동 채움**(수동 입력 없음).
9. **저장:** `Ctrl+S` (기존 안전 게이트 `dryRun`/`saveEnabled` 동일 적용).
10. 결과 CSV 기록(기존 결과 스키마 확장 또는 별도 CSV).

### 3.4 오케스트레이션 변경
- `Program.Main`: WorkScopeDialog 추가, `RuntimeWorkScope` 세팅.
- `UnimesApp.RunAsync` line 124~127: scope에 따라 두 워크플로를 순차 실행.
- `RunItemInfoWorkflowAsync`: 처리한 정상 파트 목록을 반환(현재 `void`→ 결과 반환) 또는 필드로 노출.

### 3.5 Config — `BinInfoConfig` (Models.cs, ItemInfoConfig와 동형)
```
menuName        = "품목별 BIN 정보 관리"
moduleProcessKey= "M050"
compProcessKey  = "C010"
binType         = "Normal-1"
retestNo        = "0"
binComplete     = "Y"     // Bin완료여부
retestTh        = "H"
```
`Workflow`에 `showWorkScopeDialog = true` 추가.

## 4. BIN ID 도출 규칙 (승인됨)

**모듈 (RM/TM/CM/BM):** DDR 무관, 용량만. `RAM_Module_Normal_{용량}GB`
- 용량코드(파트 5~6번째 2글자): `1G/2G/4G/8G/AG/BG/CG = 1/2/4/8/16/32/64` GB
- 현재 등록: `8GB / 16GB / 32GB` 3개. 그 외 용량은 미등록일 수 있음.

**Comp (RC/TC/CC/BC):** DDR 구분(용량 단위 `Gb`). 용량코드는 파트 4~5번째 2글자.
| 용량코드 | (용량, DDR) | BIN ID | 등록 |
|---|---|---|---|
| 8G / AG | 8Gb / 16Gb, DDR4 | `DRAM_Comp_Bin_8Gb` / `_16Gb` | ✅ |
| 4G | 4Gb, DDR4 | `DRAM_Comp_Bin_4Gb` | ❌ |
| AH | 16Gb, DDR5 | `DRAM_Comp_D5_XMP72_Bin_16Gb` | ✅ |
| HE / BH | 24Gb / 32Gb, DDR5 | `..._24Gb` / `_32Gb` | ❌ |

> Comp는 용량코드가 (용량+DDR)을 모두 인코딩하므로 용량코드 단독으로 BIN ID 결정.
> **계산한 이름이 미등록일 수 있음**(파트 용량 ≠ 등록 BIN ID). 미발견 시 §5.

## 5. 에러 / 안전 처리

- **900014 (데이터 없음):** 정상 신호 → 확인 후 진행(행추가).
- **BIN ID 미발견(검색 0행):** 해당 파트 `ERROR`/`SKIP`, **저장하지 않음**(반쪽 저장 금지) + 로그.
- **분류 실패 / 용량 파싱 실패:** BIN 행 미작성, 스킵 + 로그.
- **안전 게이트:** `dryRun=true`/`saveEnabled=false`면 값 비교/로그만, `Ctrl+S` 미발사. `SafetyGuard` 동일 적용.
- **미존재 파트(둘 다 모드):** `품목정보관리`에서 이미 SKIP → BIN 대상에서 제외.
- **StopOnFirstFailure** 옵션 기존과 동일하게 적용.

## 6. 구현 시 덤프로 확정할 UI 항목 (로그/덤프 검증)

코드 작성 전 `--dump-only` 또는 실행 덤프로 automation id를 확정한다:
- `품목별 BIN 정보 관리` 메뉴 진입 + `품목 ID` 입력칸 id.
- 900014 경고창 식별 + `확인` 버튼.
- `Ctrl+Insert` 후 추가된 행의 편집 셀(공정명/BIN Type/Retest No/Bin완료여부/Retest TH) 컨트롤 타입·id.
- 공정명 검색 버튼 id + 팝업(`Segment ID` 필드, 조회/확인, 결과 그리드).
- BIN ID 검색 버튼 id + 팝업 구조(필터 필드, 결과 그리드 열).

## 7. 테스트 (TDD 대상: BinIdResolver)

현재 테스트 프로젝트 없음 → 최소 xUnit 프로젝트(`tests/UnimesAutomation.Tests`) 신규(앱 프로젝트 참조). 케이스:
- 모듈: `RMRDAG…`(AG/DDR5)→`RAM_Module_Normal_16GB`; `…8G…`→`_8GB`; `…BG…`→`_32GB`.
- 모듈 미등록 용량: `…CG…`(64GB)→ 이름은 계산되나 워크플로에서 미발견 처리(리졸버는 이름만 반환).
- Comp DDR4: `RCA8G…`→`DRAM_Comp_Bin_8Gb`; `…AG…`→`_16Gb`.
- Comp DDR5: `…AH…`→`DRAM_Comp_D5_XMP72_Bin_16Gb`.
- 분류 실패(`XX…`)/파싱 실패 → `null`.

> 워크플로(UIA) 부분은 단위테스트 불가 → dryRun 실행 + 로그로 수동 검증.

## 8. 가정 / 미해결

- 공정명/BIN ID 검색 팝업은 기존 고객사PartID 팝업과 유사한 그리드+버튼 구조로 가정(구현 시 확정).
- 검색 키 입력 방식(전체 이름 vs 용량)·선택 규칙(정확 일치)은 덤프로 확정.
- 결과 CSV: 기존 `품목정보관리` 결과와 별도 컬럼/파일로 둘지는 구현계획에서 결정.
