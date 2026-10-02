# 확장 N 계기판 출력과 HUD 글꼴 보정 (2026-10-02)

## 변경

- 확장 N 계기판의 터보 대시를 `mTurboBoostPressure` 원본 값으로 교체하고, 사용자가 선택한 `bar` 단위를 붙였다. 터보와 일반 부스트·ERS가 동시에 있으면 부스트량과 ERS 운용 모드를 작은 보조 문구에 함께 표시한다. 터보 값이 없으면 일반 부스트량, ERS 운용 모드 순으로 표시한다. 원본 필드가 비정상 또는 결측이면 대시를 표시한다. WPF와 Composition 경로에 동일한 값 선택 함수를 적용했다.
- `mEngineTorque`는 원본 float의 소수 첫째 자리까지 표시하고 음수 부호를 유지한다.
- 독립 기어 오버레이는 공유 메모리 0/-1을 N/R로 변환한다.
- 레이스 컨트롤 글자 크기 전용 슬라이더(75~200%)를 추가했다. 설정은 기존 HUD 설정에 저장되고 즉시 카드에 반영된다. 카드의 자동 맞춤은 유지한다.

## 검증 경계

공유 메모리 v14 헤더는 `mTurboBoostPressure`의 범위와 미설정 값은 적지만 단위는 적지 않는다. `bar`는 사용자 표시 선택이며, 실제 AMS2 차량 계기판과 수치·변환 비율을 대조하지 못했다. `mBoostAmount`도 원본에 단위가 없어 임의 단위를 붙이지 않았다. ERS 필드는 `OFF/BUILD/BALANCED/ATTACK/QUAL` 운용 모드로서 실제 배터리 잔량이나 출력 kW가 아니다. 게임 프로세스가 실행 중이지 않아 토크 음수 발생 조건 및 실제 화면 가독성도 검증하지 못했다.

합성 공유 메모리 테스트는 터보 1.42, 부스트 56.5, ERS 공격 모드의 동시 표시와 각 단독 대체 경로, 토크 498.5 및 -12.5, 유효하지 않은 값의 대시 처리를 확인했다. 실제 독립 기어 오버레이 창에서 N/R 변경을 확인했고, 설정 저장·재열기와 레이스 컨트롤 150% 확대 및 카드 경계 테스트를 추가했다. WPF 화면 캡처에서 터보 숫자·보조 문구가 겹쳐 한 차례 간격을 수정했다. 최종 캡처는 `work/power-layout/avante-turbo-boost-ers-negative-torque.png`, `avante-generic-boost.png`, `avante-ers-mode.png`, `race-control-font-150-percent.png`이다. Composition 경로는 동일한 값 선택 함수를 사용하고 Release 빌드를 통과했지만 실제 렌더 화면은 보지 못했다.

## 자동 검증

Windows에서 `scripts/verify.ps1`을 Release 구성으로 실행했다. 빌드 경고·오류 0, Client 196/196, Activity 111/111 통과했다. 로그는 `work/validation-20261002-power-hud/{build,client,activity}.log`에 보관했다. `git diff --check`도 통과했다.

별도로 Debug 구성에서 기존 `Disabled overlays release windows views and image assets` 테스트가 한 차례 실패했다. 변경 전 HEAD를 별도 복사한 환경에서도 동일한 실패가 재현되어 이번 수정과 무관한 이미지 배열 GC 수명 검사의 실행 환경 차이로 분류했다. 저장소의 정식 Release 검증에서는 해당 테스트가 통과했다.
