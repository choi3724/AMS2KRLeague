# Timing Tower 리사이즈 및 애니메이션 성능 수정 보고서

- 작성일: 2026-09-05 KST
- 기준점: `v0.3.0` / `d506a93`
- 상태: 로컬 수정 및 자동 검증 완료, 커밋·릴리스는 요청되지 않아 미실행

후속: [독립 리사이즈·황색기 반복·모션 실측 보고서](OVERLAY_RESPONSIVE_FLAGS_MOTION_2026-09-05_KO.md). 아래는 최초 수정 시점의 기록이며, 현재 테스트 수/프레임 힌트/테스트 시작 방식은 후속 보고서를 따른다.

## 결과

### 타워 하단 잘림

원인은 타워 내부 15행이 `15 × 38 = 570px`을 사용하지만 `OverlayHudView` 전체 높이도 570px로 고정되어 있었던 것이다. 외곽 Border/Padding 16px이 빠져 마지막 행 하단이 잘렸다.

- 타워 기본 높이를 실제 필요 높이인 586px로 정렬했다.
- 15행과 20행에서 마지막 행의 하단이 타워 실제 높이 안에 들어오는 WPF 회귀 테스트를 추가했다.

### 리사이즈에 따른 표시 행 증가

기존에는 `MaxRankingRows = 15`와 고정 크기 Viewbox 때문에 창을 세로로 늘려도 같은 15행만 확대되었다.

- 창의 실제 가로·세로 비율로 표시 가능한 행 수를 계산한다.
- 계산식은 설계 폭 520px, 외곽 16px, 행 높이 38px을 사용하고 2~64행으로 제한한다.
- 기존 규칙인 `상위 순위 우선 + 범위 밖 플레이어를 마지막 행에 고정`을 가변 행 수에서도 유지한다.
- 현재 PC에 저장된 3440×1440 레이아웃의 타워 영역은 약 399×692px이며 새 계산 결과는 23행이다. 참가자가 20명이면 전원이 표시된다.
- 기본 520×586 크기에서는 기존과 같은 15행이 표시된다.

### 애니메이션 끊김

`uiHz = 20`은 텔레메트리 ViewModel 갱신률이며 애니메이션 FPS가 아니다. 실제 병목은 현재 시간이 변할 때마다 최대 15개 행을 전부 Collection Replace하고 `UpdateLayout()`을 강제한 구조였다. 기존에는 최대 약 300회의 행 교체/초와 템플릿 재로드가 발생할 수 있었다.

수정 내용:

- 같은 참가자 행 객체를 유지하고 값만 `INotifyPropertyChanged`로 갱신한다.
- Timing-only 갱신에서는 Collection Replace와 강제 `UpdateLayout()`을 발생시키지 않는다.
- 실제 순위 이동·상태 변경이 있을 때만 애니메이션 경로를 실행한다.
- 20Hz 데이터 투영 작업의 Dispatcher 우선순위를 `Render`에서 `Background`로 낮춰 WPF 렌더링을 선점하지 않게 했다.
- 연속 순위 변경 또는 앞차/뒷차 변경 시 현재 Transform 위치에서 다음 애니메이션을 이어서 고정 시작점으로 튀는 현상을 줄였다.

WPF 타임라인의 `DesiredFrameRate`는 기본값 `null`일 때 시스템이 프레임 수를 제어하며, 숫자를 지정해도 보장 FPS가 아니라 최대값이다. 따라서 120Hz 타이머나 `DesiredFrameRate=120`을 추가하지 않았다. 현재 PC는 두 디스플레이가 144Hz이고 WPF Render Tier 2로 확인되어, 수정된 Transform/Opacity 애니메이션은 시스템 합성 주기를 그대로 사용할 수 있다.

참고:

- <https://learn.microsoft.com/dotnet/desktop/wpf/graphics-multimedia/how-to-render-on-a-per-frame-interval-using-compositiontarget>
- <https://learn.microsoft.com/dotnet/api/system.windows.media.animation.timeline.setdesiredframerate>

## 자동 검증

- Release build: 경고 0, 오류 0
- Client/UI/SHM/Transport: 68/68 PASS
- Activity/Archive/Compact: 97/97 PASS
- 3440×1440 렌더 캡처: 기본 15번째 표시 행 하단 잘림 없음

추가된 회귀 검증:

- 기본/확장/최대 크기의 행 수 계산과 경계값
- 15행·20행 마지막 행의 실제 레이아웃 bounds
- 가변 행 수에서 leader-first/player-last 선택
- Timing-only 120회 갱신에서 CollectionChanged 0회, 동일 컨테이너 유지, 진입 애니메이션 재시작 없음

Client 테스트는 이 실행 환경에서 설치본의 DPAPI pairing credential을 열 수 없으므로, 테스트 자체와 무관한 실제 앱 시작 부작용을 막기 위해 임시 빈 activity config와 upload-disabled 인수를 사용했다. 동일 Release 바이너리의 68개 테스트를 실행했다.

## 변경하지 않은 범위

- AMS2 및 게임 메뉴/주행은 조작하지 않았다.
- Compact Telemetry Protocol, 업로드, 서버 API/DB는 변경하지 않았다.
- 버전, 커밋, 태그, GitHub Release는 변경하지 않았다.

## 남은 실제 검수

자동 테스트는 UI thread churn 제거와 compositor animation clock 유지를 검증하지만 실제 FPS 숫자를 대신할 수 없다. 다음 실게임 검수에서는 144Hz 출력 상태에서 애니메이션을 확인하고, 여전히 끊김이 보일 때만 PresentMon/ETW로 animation 구간의 render FPS와 frame-time p95를 측정한다. 목표는 120Hz급 체감이며, 측정 없이 `120 FPS PASS`로 보고하지 않는다.
