# AMS2 League Overlay v0.2.3-beta.3 Hotfix 보고서

작성일: 2026-09-03 KST

## 결론

`v0.2.3-beta.3` Closed Beta hotfix 후보의 코드, WPF 렌더, 자동 회귀 테스트와 배포 패키지 생성을 완료했다. Compact Telemetry Protocol, 업로드 구조, Cafe24 API/DB는 변경하지 않았다. Git commit/tag/GitHub Release 게시 전 후보 상태다.

## 클래스 배지와 글꼴

- 고정 매핑: Safety Car=white, GT3=green, GT4=cyan, GTE=amber, DPI/LMDh/P1=purple, P2=blue, P3=orange, Formula=red, Touring=yellow, Stock=blue, Classic=brown, Kart=yellow, Lancer=red.
- 알 수 없는 클래스 fallback: background `#526A7D`, foreground `#FFFFFF`.
- 게임 리소스를 런타임에 읽지 않고 Overlay 내부 고정 매핑만 사용한다.
- 클래스 17 px, 타임 18 px로 확대했고 행 높이 36 px, 클래스 열 100 px, 타임 열 104 px로 함께 조정했다.
- 15행과 `12:34.567`, `UNKNOWN` fixture를 실제 WPF로 렌더해 잘림이 없음을 확인했다.

## 주행 중 참가자 회색 표시 원인과 수정

정확한 원인은 상태 텍스트가 바뀔 때 `OverlayHudView`가 행 전체 `ContentPresenter.Opacity`를 0.35까지 낮추는 840 ms 자동복귀 애니메이션을 실행하던 것이다. beta.2에서 행 컨테이너를 20 Hz 갱신 중에도 보존하도록 바뀐 뒤 이 애니메이션도 더 오래 살아남아, 정상 주행 참가자가 일시적으로 비활성처럼 보였다.

- 수정 전: 상태 문자열 변경만으로 행 전체가 흐려졌다. Pit, timing 갱신 지연, row reorder도 시각적으로 비활성처럼 보일 수 있었다.
- 수정 후: 상태 변경은 행 opacity를 건드리지 않는다. `IsActive=false` 또는 `RET`/`DNF`/`DSQ`만 비활성 색상을 사용한다.
- `Finished`는 final 상태지만 흐리게 만들지 않는다.
- Pit, 일시적 participant timing 누락, 순위 이동 애니메이션 중인 주행 참가자는 active 밝기를 유지한다.

## Relative Gap 색상

색상 의미를 RED=사용자에게 불리, BLUE=사용자에게 유리로 통일했다.

- AHEAD 50 m → 60 m: RED.
- AHEAD 60 m → 50 m: BLUE.
- BEHIND 50 m → 40 m: RED.
- BEHIND 40 m → 50 m: BLUE.
- Shared Memory 갱신은 20 Hz이며 마지막 안정 거리 대비 ±2 m 이내는 기존 안정 trend를 유지한다. 2 m를 넘는 실제 변화에는 다음 유효 snapshot에서 반응한다.
- participant index 또는 session generation이 바뀌면 trend 상태를 초기화한다.

## 잘못된 `LAP 1` 원인과 수정

정확한 원인은 기존 `GapPresenter`가 `opponent.LapsCompleted - local.LapsCompleted`만 보고 차이가 1이면 즉시 `LAP 1`로 표시한 것이다. 앞차가 Start/Finish를 먼저 통과하면 앞차는 `lapsCompleted+1`, 플레이어는 아직 이전 lap인 짧은 구간이 생기므로 실제 20~60 m 차이도 한 바퀴 차이로 오판했다.

수정 후 판정은 다음과 같다.

`progress = lapsCompleted × trackLength + validLapDistance`

- 두 차량 progress 차이가 실제 trackLength 이상일 때만 `LAP 1`, 2배 이상이면 `LAP 2` 후보가 된다.
- 동일 participant와 동일 lap 후보가 연속 2회 유효 snapshot에서 확인돼야 확정된다.
- participant identity 또는 session generation 변경 시 확인 상태를 초기화한다.
- trackLength가 없거나 lapDistance가 0~trackLength 범위를 벗어나면 Lap Gap을 추정하지 않고 기존 meter/time gap fallback을 사용한다.

## 완료 검증

CLASS COLOR MAPPING: PASS

FONT SIZE UPDATE: PASS

ACTIVE PARTICIPANT DIM BUG: PASS

INTENDED INACTIVE STYLE ONLY: PASS

REGRESSION CHECK: PASS

WAITING CLIP: PASS

POSITION ANIMATION: PASS

PRACTICE BEST LAP: PASS

QUALIFYING BEST LAP: PASS

RACE FINISH TIMING: PASS

RELATIVE GAP COLOR: PASS

AHEAD INCREASING = RED: PASS

AHEAD DECREASING = BLUE: PASS

BEHIND DECREASING = RED: PASS

BEHIND INCREASING = BLUE: PASS

FALSE LAP GAP: PASS

START/FINISH WRAP TEST: PASS

ACTUAL LAP GAP TEST: PASS

## 검증 근거

- Release build: warning 0, error 0.
- UI/Client tests: 54/54 PASS.
- Activity/Capture/Compact tests: 96/96 PASS.
- 공개 배포 폴더 audit: 466 files, forbidden 0.
- Portable ZIP audit: 466 files, forbidden 0.
- Installer audit: 1 file, forbidden 0.
- WPF render: class/state tower, position reorder mid-frame, waiting overlay를 직접 렌더하고 시각 검수했다.
- 실게임 재확인 범위: 이 보고서의 UI 검수는 실제 WPF 렌더 fixture 기준이며, beta 설치 후 실제 AMS2 멀티플레이 화면 확인은 후속 closed-beta 단계다.

## 산출물

- `AMS2-League-Overlay-0.2.3-beta.3-win-x64.zip`
  - bytes: `70,127,355`
  - SHA256: `7f96d9c1b38e725afb0dc939f9b1c0ec57afa682de612b2a6674cb2e85c15b7b`
- `AMS2-League-Overlay-0.2.3-beta.3-Setup.exe`
  - bytes: `51,242,992`
  - SHA256: `de5b783664cce63a0748876bb58061e5d34063c4956b1cf65cbb24a13dc0ea11`
- `SHA256SUMS-0.2.3-beta.3.txt`
- `release-manifest-0.2.3-beta.3.json`

Manifest의 파일 크기와 SHA256은 실제 파일 재계산 결과와 일치한다.

## 릴리스 파이프라인 보정

- Windows PowerShell 5.1에서 `-LiteralPath`와 `-Include *.pdb` 조합이 모든 파일을 반환하던 문제를 `-Filter '*.pdb'`로 수정했다.
- PowerShell 5.1에 없는 `Convert.ToHexString`과 `utf8NoBOM` 인코딩 이름을 호환 구현으로 교체했다.
- publish 출력 경로 변경 시 증분 복사 누락을 방지하도록 publish에 `Rebuild`를 적용했다.
