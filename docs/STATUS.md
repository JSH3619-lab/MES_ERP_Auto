# STATUS

최종 갱신: 2026-06-19

## 현재 상태

- 사용자 live 확인 완료: 자동 로그인 -> `품목정보관리` 저장 -> `품목별 BIN 정보 관리` 행추가/입력/저장까지 처음부터 끝까지 정상 완료.
- `main` 기준 최신 기능은 실제 저장 테스트까지 통과했다.
- 기본 안전 설정은 계속 `dryRun=true`, `saveEnabled=false`다.

## 핵심 동작

- MES/ERP 구분은 창 제목으로 한다. `UNIMES`만 대상이고 `UNIERP`는 제외한다.
- 로그인 자동화는 UIA `Edit` 탐지를 우선 사용한다.
- 로그인 입력칸이 UIA에 노출되지 않으면 좌표 fallback을 사용한다.
  - ID/PW는 같은 행에 있다.
  - 왼쪽 칸이 ID, 오른쪽 칸이 PW다.
- `Try again`은 다음 조건을 모두 만족할 때만 처리한다.
  - 상단 서버 오류 문구가 보인다.
  - 상단 `Try again` 링크가 보인다.
  - 서버 선택칸 위치에 `UNIMES` 값이 보이지 않는다.
- BIN 행추가는 버튼 클릭 로그만으로 성공 처리하지 않는다.
  - `BIN 정보 선택` 그리드에 새 행이 실제 생성됐는지 확인한다.
  - 새 행이 없으면 그리드 포커스 후 `Ctrl+Insert` fallback을 사용한다.
- `둘 다` 실행 시 `품목정보관리` 완료창은 중간에 띄우지 않는다.
  - `품목정보관리`와 `품목별 BIN 정보 관리`를 모두 끝낸 뒤 통합 완료창을 한 번만 표시한다.

## 검증된 흐름

- 자동 로그인 정상 화면에서 `Try again` 오탐 없음.
- `Try again` 실제 화면은 서버 오류 문구와 상단 링크 기준으로 처리.
- `품목정보관리`:
  - Part 조회
  - BIN 관리, Turn Key, 조립입고 공정이동여부, 불량창고 설정
  - 저장 게이트 통과 시 저장
- `품목별 BIN 정보 관리`:
  - BIN-only 모드에서는 품목 코드 검색 팝업으로 Part 선택
  - 둘 다 모드에서는 품목정보관리에서 유효한 Part만 BIN 처리
  - 둘 다 모드에서는 BIN까지 끝난 뒤 결과창 1회 표시
  - 900014 no-data 모달 닫기
  - 행추가
  - 공정명, BIN Type, Retest No, Bin완료여부, Retest TH, BIN ID 입력
  - 저장

## 남은 주의점

- UI Automation 기반이라 화면에 보이는 것과 UIA 트리가 다를 수 있다.
- 실패 분석은 최신 `logs/run_*.log`, 대응 스크린샷, `ui_dump_*.txt` 순서로 한다.
- `logs/`, `screenshots/`, `output/`, `bin/`, `obj/`는 생성물이며 git 추적 대상이 아니다.
- `appsettings.save-test.json`은 실제 저장 테스트용 로컬 파일이며 git 추적 대상이 아니다.

## 다음 변경 전 체크

```powershell
dotnet build .\src\UnimesAutomation\UnimesAutomation.csproj
dotnet test .\tests\UnimesAutomation.Tests\UnimesAutomation.Tests.csproj
```

실 MES 동작 변경은 빌드/테스트만으로 충분하지 않다. 반드시 live 실행 로그로 확인한다.
