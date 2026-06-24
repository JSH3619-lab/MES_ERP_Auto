# 단순화/리팩터 계획 (핸드오프)

> 다른 세션에서 이 문서만 읽고 그대로 진행할 수 있게 정리. 추측 말고 **빌드/테스트로 검증** 후 진행한다.
> 기준 커밋: `9355cee` (branch `feature/mes-gui`). 단위 테스트 31개 통과 상태.

## 배경

`/understand` 분석 결과 코드 구조 점검에서 나온 정리 항목. 핵심 사실:
- 파일 크기 분포가 극단적: **`UnimesApp.cs` 4,618줄 1개 + 나머지는 6~520줄.**
- "파일이 많다"는 대부분 착시(작아도 단일 책임이라 정상). 실제 빚은 아래 항목들.
- 절대 규칙은 [CLAUDE.md](../CLAUDE.md) 준수: **MES만 조작·ERP 금지**, 전체 조회 금지, 저장은 `Ctrl+S` 경로만, `SafetyGuard` 유지.

## 명령 (검증용)

```powershell
# 빌드
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj
# 테스트 (31개 통과여야 함)
dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj
```

각 작업 = "검증 가능한 목표"로: 변경 → **build 0 오류 + 31 tests green** 확인. 깨지면 그 작업만 롤백.

---

## 작업 목록 (위험 낮은 순 = 실행 순서)

### 1. BIN `Clone` 중복 제거  🟢 매우 낮음 — **최고 ROI**
- **증거(실제 빚):** `Clone(BinRowConfig)`가 토씨까지 동일하게 복붙됨
  - `src/UnimesAutomation/SsdBinRules.cs:72`
  - `src/UnimesAutomation/DramBinRules.cs:64`
- **할 일:** `BinRowConfig`(정의: `src/UnimesAutomation/Models.cs`, `public sealed class BinRowConfig`)에 `public BinRowConfig Clone() => new(){ ProcessName=…, BinType=…, RetestNo=…, BinComplete=…, RetestTh=… };` 1개 추가 → 두 파일의 private `Clone` 삭제하고 `source.Clone()` 호출로 교체.
- **검증:** build + tests (`BinIdResolverTests`가 SSD/DRAM 경로를 덮음).

### 2. BIN 3파일 → `BinRules.cs` 합침  🟢 낮음 (선택·껍데기)
- **대상:** `BinIdResolver.cs`(37) + `SsdBinRules.cs`(80) + `DramBinRules.cs`(72) → 하나의 `BinRules.cs`.
- "파트번호 → `BinInfoTarget`" 한 개념이 3파일로 흩어짐. 밀도 테이블(13/7/6개)은 작아 별도 파일 가치 없음 → 같이 둠.
- **클래스명 유지**(`BinIdResolver`/`SsdBinRules`/`DramBinRules` 그대로) → 호출부 변경 0.
- **검증:** build + tests. (1번을 먼저 하면 합칠 때 `Clone`은 이미 한 곳뿐)
- ⚠️ 순수 이동만. 동작/시그니처 바꾸지 말 것.

### 3. `Models.cs` 분리  🟢 낮음
- **현재:** `Models.cs`(384줄)에 20개 타입(설정+결과 DTO+런타임)이 grab-bag. 그래프상 연결 26개 허브.
- **분리(namespace `UnimesAutomation` 동일 유지 → 호출부 변경 0):**
  - `Config.cs` — `RootConfig, AppConfig, LoginConfig, SafetyConfig, WorkflowConfig, OptionsConfig, CategoriesConfig, CategoryConfig, SsdCategoryConfig, GlobalConfig, enum WorkScope`
  - `Results.cs` — `PartResult, BinResult, ItemInfoValues, BinInfoValues, BinRowConfig, PartRequest`
  - (잔여 `RuntimePaths, CommandLineOptions`는 `Models.cs`에 남기거나 `Runtime.cs`로)
- **검증:** build + tests. 순수 이동.

> **여기까지(1~3)가 묶음 A — 동작 변화 0, 테스트가 덮음. 1커밋 권장.**

### 4. 네이티브 MessageBox 합침  🟡 중 (P/Invoke·topmost)
- **증거(중복):** `ShowNativeMessageBox` P/Invoke 선언+상수가 양쪽에 중복
  - `src/UnimesAutomation/MainForm.cs:24`
  - `src/UnimesAutomation/UnimesApp.cs:17`
- **할 일:** `NativeMessage.cs` 헬퍼(`Show(text, caption, kind)`)로 추출, 양쪽에서 호출. `MbTopMost|MbSetForeground|MbTaskModal` 동작 보존(자동화 중 MES 위로 떠야 함).
- **검증:** build + **실행 스모크**(완료/실패 알림창이 실제로 뜨고 최전면인지). 별도 커밋.

### 5. `UnimesApp.cs` partial 분할  🟡 중 (제일 큼)
- **방식:** 새 클래스/타입 만들지 말고 **`partial class`로 파일만 분할** → 새 의존·호출부 변경 0.
  - `UnimesApp.cs` (RunAsync 흐름/복구 규칙)
  - `UnimesApp.Bin.cs` (BIN 워크플로: ClickInsertRow, FillFixedBinCells, SetBinComboCell, FocusBinSelectionGrid …)
  - `UnimesApp.Menu.cs` (메뉴 이동: NavigateToMenuByF3Async, FindMenuSearchInput, TryOpenMenuFromTree …)
  - `UnimesApp.Dialogs.cs` (다이얼로그 감지: FindOwnedMessageDialog, FindMesMessageDialog, FindWarningDialog, ReadMessageText …)
- 클래스 선언을 `public sealed partial class UnimesApp`로 바꾸고 메서드를 파일별로 이동만.
- **검증:** build + tests + 실행 스모크. 별도 세션/커밋 권장(diff 큼).
- ⚠️ 진짜 클래스 추출(collaborator 8개)은 **하지 말 것** — speculative. 그 영역을 다음에 손대고 테스트가 필요할 때만 점진적으로.

### ~~6. MainForm 컨트롤 추출~~ — **보류**
- LogConsole/타이틀바 추출 가능하나, 지금 아프지 않으면 안 한다(YAGNI).

---

## 묶음 / 커밋 지점

| 묶음 | 작업 | 비고 |
|---|---|---|
| **A** | 1 + 2 + 3 | 순수 이동·dedup, 동작 0변화, 테스트가 덮음 → 1커밋. **권장 시작점.** |
| **B** | 4 | P/Invoke 건드림 → 스모크 후 별도 커밋 |
| **C** | 5 | 최대 작업 → 별도 세션/커밋 |

**한 번에 1~5 다 하지 말 것** — diff가 커지면 깨질 때 원인 격리가 어려움(특히 4·5).

---

## 스킬 사용법 (정확도용)

1. **ponytail (모드)** — 작업 내내 켜둔다. 기본 `full`. 코드를 **최소·재사용·1줄 우선**으로 쓰게 강제.
   - 호출: `/ponytail` (레벨: `/ponytail full`). 이 작업들은 "더 만들기"가 아니라 "줄이기"라 ponytail과 정확히 맞음.
   - 단, **이해를 건너뛰지 말 것** — 바꾸기 전 해당 코드의 호출부를 먼저 읽는다.
2. **TDD** — 로직이 바뀌는 곳은 기존 테스트 그린 유지가 곧 검증. 1번은 `BinIdResolverTests`가 이미 덮음. 새 동작 추가 아니면 새 테스트 불필요(YAGNI).
3. **편집 후 `/simplify`** — 만든 **diff**를 훑어 남은 재사용/altitude 군더더기를 자동 정리.
   - ⚠️ diff가 있어야 동작(아직 안 고친 기존 코드엔 안 씀). **품질만** 본다(버그 안 봄).
4. **`/code-review`** — 4·5처럼 동작이 바뀔 수 있는 묶음은 정리 후 correctness 리뷰. (simplify는 버그를 안 보므로 보완)

**한 묶음 흐름:** ponytail로 편집 → `dotnet build` + `dotnet test` 그린 → `/simplify`(diff 마감) → (4·5는 `/code-review`+스모크) → 커밋.

## 완료 주장 전 (필수)
- build 0 오류 + 31 tests green **출력을 직접 확인**하고 말한다. 실패면 실패라고 말한다.
- 커밋은 사용자가 요청할 때만(또는 묶음별로 합의된 경우). 커밋 메시지 끝: `Co-Authored-By: Claude <noreply@anthropic.com>`.
