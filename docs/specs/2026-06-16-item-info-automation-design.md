# 품목정보관리 자동화 설계 (UNIMES)

작성일: 2026-06-16

## 목적

UNIMES(BIZENTRO MES, UNIERP 플랫폼 기반) 데스크톱 클라이언트에서
**신규 Part No. 등록 시 "품목정보관리" 화면의 4개 필드를 자동 설정**한다.
여러 Part No.를 입력 순서대로(A→B→C) 반복 처리한다.

이것은 기존 "조회 전용 PoC"에서 **처음으로 데이터를 쓰는 작업**이다.

## 전체 흐름

```
[1] MES 실행 (ClickOnce .appref-ms)        ← 기존 코드 유지
[2] 접속 = 로그인 + Continue 팝업 → 메인창   ← 기존 코드 유지
[3] F3 → "품목정보관리" 입력 → Enter        ← 신규 (기존 좌표 메뉴클릭 폐기)
[4] 입력 리스트 순서대로 Part 반복
      각 Part:
        - 분류 (Module / Comp / Unknown)
        - 품목명에 Part No. 입력 → 조회
        - PID 행 선택 후 4개 드롭다운 설정      ← 그리드 구조 확인 후 구현
        - Ctrl+S 저장 (saveEnabled && !dryRun)  ← 그리드 구조 확인 후 구현
[5] 결과 CSV + 로그 + 스크린샷 기록
```

## 확정 규칙 (LOCK)

### 설정 값 (자사 기준 고정, 설정으로 외부화)

| 필드 | 값 |
|---|---|
| BIN 관리 | Y |
| Turn Key | N |
| 조립입고 | Y |
| 불량 창고 | Module → `제품 폐기창고` / Comp → `COMPONENT 폐기창고` |

→ 값은 `appsettings.json`의 `itemInfo` 섹션으로 분리. 타 팀은 값만 바꿔 재사용.

### Module / Comp 분류 — Part No. 접두 2글자

- Module: `RM` / `TM` / `BM` / `CM`
- Comp: `RC` / `TC` / `BC` / `CC`
- 그 외 → **Unknown → 건너뛰기 + 경고 로그 + 스크린샷 → 다음 Part 진행** (옵션 A)

### PID 식별 / 대상 행

- PID = Part No.에서 **2번째 `-` 앞까지**의 문자열
- 예: `RMRDAG58A1B-GAWRRWM7-XXX` → PID = `RMRDAG58A1B-GAWRRWM7`
- 조회 결과 그리드에서 품목ID가 PID와 일치하는 행에 설정
  (PID 행 = 2번째 `-` 없는 행, MFGID 행 = 접미사 더 붙은 행)

## 안전 장치

- 저장 = **Ctrl+S** (키 입력). `SafetyGuard`(버튼 캡션 검사)로는 안 잡히므로
  저장 지점에 `saveEnabled && !dryRun` 게이트를 직접 건다.
- 초기 검증: `dryRun=true`로 값만 채우고 저장 직전 멈춤 → 스크린샷 확인 → 활성화.

## 기록 구조

| 산출물 | 위치 | 용도 |
|---|---|---|
| 시스템 로그 | `logs/run_*.log` | 단계별 INFO/WARN/ERROR — "어디서 문제 생겼나" |
| 동작 이력 | `output/result_*.csv` | Part별 분류/설정값/저장여부/상태 — "제대로 돌고 있나" |
| 스크린샷 | `screenshots/*.png` | 저장 직전 + 실패/건너뛰기 시 — "실제로 그렇게 됐나" 증거 |
| 그리드 덤프 | `logs/ui_dump_iteminfo_*.txt` | 드롭다운 설정 로직 튜닝용 (구현 단계 한정) |

## 그리드 구조 (2026-06-16 실측 확인)

조회 후 그리드는 `ControlType.Tree` 안의 `ControlType.DataItem`(행)들로 구성.

- 행 식별: 행 하위 `Edit name='품목ID'`의 ValuePattern == PID
- 설정 셀(모두 `ControlType.ComboBox`, 행 하위, Name=컬럼명):
  - `BIN 관리` (선택지 Y/N)
  - `Turn Key` (선택지 Y/N)
  - `조립입고 공정이동여부` ← "조립입고"의 실제 컬럼명 (선택지 Y/N)
  - `불량창고` (선택지 표시명: COMPONENT 폐기창고 / DIE 폐기창고 / RMA창고 / 제품 폐기창고)
- **주의**: `불량창고`는 ValuePattern이 코드(예: 제품폐기창고=`RMRS`)를 반환하고, 드롭다운 ListItem은 표시명을 가짐.
  → 설정/판별은 ListItem **표시명**으로 한다(현재 선택 여부는 SelectionItemPattern으로 확인).
- 설정 방식: 셀 ComboBox 포커스 → (펼침) → 표시명 일치 ListItem `SelectionItemPattern.Select()` → 실패 시 `ValuePattern.SetValue` 폴백
- 멱등: 현재값==목표값이면 변경 안 함. dryRun/saveEnabled=false면 화면도 안 바꾸고 '변경 예정'만 판별.

## 보류 (추후)

- 조회 시 다량 결과의 행 선택 세부 규칙 (확장 Part: 자리수/Reball/Retest). 평소엔 단독 검색이라 실무 영향 적음.
- DDR4/DDR5 분기 (BIN 정보 관리에서 용량 필요).
- "품목별 BIN 정보 관리" 작업 전체 (다음 단계).
- 분류 로직 단위 테스트 (현재 테스트 프로젝트 없음).

## 구현 단계

1. (이번) 골격: F3 진입 → 품목명 입력 → 조회 → 그리드 캡처(덤프+스크린샷). 데이터 미기록.
2. (다음) 실제 그리드 덤프 확보 후: PID 행 선택 + 4개 드롭다운 설정 + Ctrl+S 저장.
3. dryRun 1개 Part 검증 → saveEnabled 활성화.
