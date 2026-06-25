# SIP 품목정보관리 + Marking — 설계

작성: 2026-06-25 · 브랜치 `feature/mes-gui`

## 개요

DRAM Module/Comp·SSD에 이어 **SIP** 분류를 추가한다. SIP는 품목정보관리에서
기존 카테고리와 동일하게 BIN 관리/Turn Key/조립입고/불량창고를 처리하고,
추가로 **Marking** 셀에 PID에서 파생한 값을 입력한다.

기존 카테고리 패턴(분류·설정·워크플로)을 그대로 재사용하고, **새로 만드는 것은
Marking 두 가지뿐**: ① PID→Marking 순수 파생 함수, ② 텍스트 셀(콤보 아님) 입력 메서드.

## 범위

### 포함 (이번 작업)
- 파트 분류 `SN → Sip`
- SIP 품목정보관리: BIN 관리/Turn Key/조립입고/불량창고 = `Y / N / Y / 제품 폐기창고` (DRAM Module과 동일)
- **PID 기반 Marking 파생**과 입력
- Marking 생략 예외(끝 2글자 `0S/0G/0J/0K`)
- 설정에 SIP 탭 추가
- Marking 값을 result.xlsx에 기록

### 제외 (다음 단계)
- **PID 외 추가 Marking 로직** — 사용자가 "Marking 관련 로직은 더 구성할 게 있다"고 했으나, 이번은 **PID 기반 파생까지만**. 설계는 확장 여지를 남기되 구현하지 않는다.
- **품목정보관리 추가 항목** — 사용자가 더 있다고 함(별도).

## SIP 품목별 BIN 정보 관리 (추가 구현 완료)

예외 파트 구분 없이 **모든 SIP 파트 동일 적용**(Marking 예외와 무관). DRAM/SSD와 동일 워크플로(`RunBinInfoWorkflowAsync`)를 그대로 타고, `SipBinRules`만 추가.

### 행 구성 (2행, 공정 M030 = 제품 Bin Sorting)
| 행 | 공정명 | BIN Type | Retest No | Bin완료여부 | Retest TH | BIN ID |
|---|---|---|---|---|---|---|
| 1 | M030 | Normal-1 | 0 | **미설정(Blank)** | Normal | `SIP_Normal_{용량}_AIO` |
| 2 | M030 | Normal-2 | 1 | Y | Y | `SIP_Normal_{용량}_AIO` |

- **용량**: 파트 4–5번째 글자(index 3-4, SSD와 동일 위치) → SIP 밀도표. 8G→8Gb, AG→16Gb, BG→32Gb, CG→64Gb, DG→128Gb, FG→256Gb, HG→512Gb, KG→1Tb, MG→2Tb, UG→4Tb, VG→8Tb. 예: `KG → 1Tb → SIP_Normal_1Tb_AIO`.
- **두 행 BIN ID 동일.** SIP의 Retest TH 드롭다운은 `Normal/Y/H/D/A`(DRAM/SSD와 다름) → `Y`가 유효값.

### 구현
- `SipBinRules.Resolve(part, Categories.Sip)` ([BinRules.cs](../../../src/UnimesAutomation/BinRules.cs)): 용량→BIN ID 산출, 행 설정은 `Categories.Sip.BinInfo`(기본 2행). `BinIdResolver`에 `PartClass.Sip` 분기 추가.
- `CategoryConfig.DefaultSip()`에 BIN 2행 추가(공정 M030).
- **1행 Bin완료여부 Blank**: `SetBinComboCell`에 빈 목표값 스킵 가드(해당 셀 미설정 → 현재값 유지). 품목정보 빈값 스킵과 동형.
- 설정: Retest TH 옵션에 `Y` 추가, `CategorySettingsControl`에 DataError 가드(Blank 값이 콤보에 없어도 설정창 정상).

### 검증
- `BinIdResolverTests`: SIP 2행/동일 BIN ID/용량 산출/미지원 용량 null.
- 실기 스모크: 2행 추가·셀 입력·BIN ID 선택·저장 확인.

## 절대 규칙 준수

[CLAUDE.md](../../../CLAUDE.md): MES만 조작(ERP 금지), 빈 값/무필터 전체조회 금지,
저장은 `Ctrl+S` 경로만, `SafetyGuard` 유지. Marking 입력은 품목 행의 셀 변경일 뿐이고,
저장은 기존 품목정보 1회 `Ctrl+S` 경로에 포함된다(별도 저장 없음).

## 상세 설계

### 1. 분류 — `PartClassifier.cs`
- `enum PartClass`에 `Sip` 추가.
- `Classify`: prefix `"SN"` → `PartClass.Sip`.
- `ExtractPid`는 변경 없음. SIP 입력은 PID 형태(점 1개)라 전체가 PID로 반환된다.
  Marking·예외 판정은 `ExtractPid(part)` 결과를 기준으로 한다(MFGID가 붙어 와도 안전).

### 2. 설정 모델 — `Config.cs`
- `CategoriesConfig.Sip` (`CategoryConfig` 타입) 추가.
- `CategoryConfig.DefaultSip()` = ItemInfo `{ BinManage="Y", TurnKey="N", AssemblyIn="Y", DefectWarehouse="제품 폐기창고" }`, BinInfo는 빈 행(BIN 워크플로 추후).
- `RootConfig.ResolveItemInfo`: `PartClass.Sip → Categories.Sip.ItemInfo`.
- `ConfigStore.Normalize`: SIP 특별 처리 불필요(SSD처럼 AssemblyIn 비우는 가드 없음).

### 3. Marking 파생 — 새 `SipMarking.cs` (순수 함수)

PartClassifier/BinRules처럼 화면과 분리된 순수 로직. 단위 테스트로 검증한다.

PID `SNAKGD8J0B-HPRA81` (1-based 고정 자리수):

| 토큰 | 의미 | 위치(1-based) |
|---|---|---|
| `AK` | 용량 | 3–4 |
| `B` | 세대 | 10 |
| `H` | Wafer Type | 12 (`-` 다음 첫 글자) |
| `P` | Ass'y | 13 |
| `A8` | Cont. | 15–16 |
| `YWW` | 고정 리터럴 | (붙임) |

```
Compute(pid) => pid[2..4] + pid[9] + pid[11] + pid[12] + pid[14..16] + "YWW"
            // 예: SNAKGD8J0B-HPRA81 → AKBHPA8YWW
ShouldMark(pid) => 끝 2글자가 {0S, 0G, 0J, 0K} 중 하나가 아니면 true
```

- 길이 부족 등 비정상 입력은 `Compute`가 빈 문자열 반환(워크플로에서 빈 값이면 셀 미처리 — 기존 가드와 동일).
- 자리수는 고정이므로 위치 상수로 추출한다(파싱 휴리스틱 없음).

### 4. 품목정보관리 워크플로 — `UnimesApp.cs` (`RunItemInfoWorkflowAsync`)

- 기존 4셀 루프(BIN 관리/Turn Key/조립입고 공정이동여부/불량창고)는 **그대로**.
  SIP도 동일하게 Y/N/Y/제품폐기창고로 처리된다(현재 콤보 적용 로직 재사용).
- 4셀 루프 직후, **`classification == Sip && SipMarking.ShouldMark(pid)`** 일 때만 Marking 처리:
  1. `result.Marking = SipMarking.Compute(pid)` 계산.
  2. `ApplyMarkingTextCell(row, "Marking", result.Marking, readOnlyMode)` 호출.
- 예외 파트(`ShouldMark==false`)는 Marking 단계를 건너뛴다. 로그 남김
  (`품목정보관리 SIP Marking 생략(예외). part=...`). SSD가 조립입고를 건너뛰는 가드와 동형.
- `readOnlyMode`(CLI/덤프)면 화면을 바꾸지 않고 "변경 예정"만 판별 — 기존과 동일.

### 5. Marking 셀 입력 — 새 `ApplyMarkingTextCell` (텍스트, 콤보 아님)

Marking은 자유 텍스트라 `ApplyComboCell`(목록 선택)을 쓸 수 없다. 별도 메서드:

1. 품목 행에서 `Marking` 셀을 찾는다(`FindGridCell(row, "Marking", ...)`).
2. 현재값을 읽어 목표와 같으면 `Unchanged`, `readOnlyMode`면 `WouldChange` 반환(셀 안 건드림).
3. 변경이면: 셀 더블클릭으로 편집모드 진입 → 기존값 클리어 → 목표값 타이핑 → `Tab` 커밋 → 재읽기로 검증.
   - ⚠️ 셀에 드롭다운 caret이 있으나 값은 자유 입력(실기 확인됨: 더블클릭 후 타이핑 먹음).
   - **UIA 패턴 세부(ComboBox vs Edit, ValuePattern 유무)는 구현 시 실기 `ui_dump`로 확정**하고, 안 되면 기존 좌표 더블클릭(`ClickElementCenterByMouseDouble`)+`SendKeys` 폴백을 쓴다.
- 저장은 별도로 하지 않는다. 변경된 셀들은 워크플로 말미의 기존 품목정보 `Ctrl+S` 1회에 함께 저장된다.

### 6. 결과 기록 — `Results.cs` · `ResultWorkbook.cs`
- `PartResult`에 `string Marking` 필드 추가(기본 `""`).
- 품목정보관리 시트 헤더에 `Marking` 컬럼 추가(`불량창고` 뒤). 이후 셀 인덱스 한 칸씩 이동.
- 예외/비SIP는 빈 문자열로 남는다.
- `ResultWorkbookTests`의 컬럼 기대값을 새 헤더에 맞게 갱신.

### 7. 설정 UI — SIP 탭
- `SettingsForm`에 **SIP 탭 추가**, `CategorySettingsControl(Categories.Sip, options)` **그대로 재사용**.
- 품목정보 4셀(BIN 관리/Turn Key/조립입고/불량창고)을 DRAM Module과 동일하게 편집.
- BIN 정보관리 그리드도 함께 보이지만 SIP BIN 워크플로는 추후라 지금은 무동작 — 그 워크플로 추가 시 바로 연결된다(사용자 합의).
- **Marking은 계산값이라 설정에 노출하지 않는다**(편집 대상 아님).

## 테스트 (TDD — 새 로직)

- `SipMarkingTests`
  - `Compute("SNAKGD8J0B-HPRA81") == "AKBHPA8YWW"`
  - `ShouldMark`: 정상 PID true, `...0S/0G/0J/0K` 4종 false
  - 비정상 짧은 입력 → `Compute` 빈 문자열
- `PartClassifierTests`: `Classify("SN...") == PartClass.Sip`
- `ResultWorkbookTests`: Marking 컬럼 포함 헤더/값 검증
- 실기 스모크: Marking 셀이 실제로 더블클릭→입력→저장되는지(다음 실기 로그로 확인).

## 검증 게이트
- `dotnet build` 0오류 + `dotnet test` 그린(기존 31 + SIP 신규).
- 실기 스모크 후 Marking 입력 동작 확인.

## 미해결/확장 여지
- PID 외 추가 Marking 로직(사용자 언급) — 별도 작업으로 분리.
- SIP 품목별 BIN 정보 관리 — 별도 작업으로 분리.
