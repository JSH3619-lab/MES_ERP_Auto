# 실행 / 테스트 / 로그 확인

## 빌드
```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj
```

## 실행 모드
```powershell
# 기존에 로그인된 MES에 attach (가장 흔한 테스트)
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --no-launch

# 현재 UNIMES UI 트리만 덤프(컨트롤 automation id 확인용)
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --dump-only --no-launch

# 특정 설정 파일 사용
dotnet run --project .\src\UnimesAutomation\UnimesAutomation.csproj -- --config .\appsettings.json
```
실행 시 파트 입력 다이얼로그가 뜨면 한 줄에 하나씩 Part No 입력 후 시작.
`dryRun=true`/`saveEnabled=false` 기본값이라 저장은 일어나지 않음(안전).

## 산출물 위치
| 종류 | 경로 |
|---|---|
| 실행 로그 | `logs/run_YYYYMMDD_HHMMSS.log` |
| UI 덤프 | `logs/ui_dump_*.txt` |
| 스크린샷 | `screenshots/*.png` |
| 결과 CSV | `output/result_*.csv` |

## 로그에서 확인할 핵심 줄
- 대상 창: `Main UNIMES window: name='UNIMES - UNIMES'` ← **반드시 UNIMES**(UNIERP면 실패)
- 조회 방식: `Search button was not found by name. 좌표 기반 fallback` ← 뜨면 툴바 조회 탐색 실패(불안정 신호)
- 미존재 팝업:
  - 정상: `고객사PartID 팝업 감지 → 미존재 경고창 [확인] 처리 → 기파트로 안전복구`
  - 실패: `고객사PartID 팝업 미감지... 현재 top-level 창: [...]` ← 이 창 목록을 분석 단서로 사용

## 시나리오별 테스트
1. **ERP 동시 실행**: MES+ERP 둘 다 켜고 실행 → 로그가 `UNIMES`를 잡는지.
2. **attach 포커스**: 다른 세션에서 MES 켜둔 채 실행 → 사용자가 직접 클릭 안 해도 진행되는지(STATUS #2).
3. **미존재 파트**: 없는 파트 1건 입력 → 전체조회로 멈추지 않고 SKIPPED 처리되는지(STATUS #3).
4. **회귀**: 정상 파트 다건 → 기존처럼 행 찾고 셀 비교까지 되는지.

## 주의
- 실제 MES UI 동작은 코드/빌드만으로 검증 불가 → **반드시 실행해서 로그/스크린샷으로 확인.**
- 추측 수정 금지: 실패하면 `run_*.log`의 해당 줄 + 스크린샷으로 원인 특정 후 고친다.
