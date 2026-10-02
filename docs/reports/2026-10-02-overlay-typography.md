# 오버레이별 글꼴·글자 크기 검증 (2026-10-02)

`REQ-OVERLAY-TEXT-103`은 글자가 있는 12개 오버레이에 각각 글꼴과 50~200% 내부 글자 크기 설정을 추가한다. 창 크기 조절은 기존 레이아웃 기능으로 유지한다. 사용자 후속 지시에 따라 일반형·확장형 N 계기판은 제외했다. 기본 텔레메트리 그래프에는 글자가 없으며, 같은 창의 개량형 핸들 각도만 설정 대상이다.

설정은 `DrivingHudSettings.OverlayText`에 오버레이 키별로 저장한다. 이전 속도·기어 글꼴과 레이스 컨트롤 배율은 새 설정이 없으면 계속 적용된다. 새 프로필에서는 레이싱 계기판을 속도·기어 글꼴과 독립적으로 설정한다. 실제 창의 지연 생성 글자에도 설정이 적용되도록 `Loaded` 이후 다시 처리한다. 속도·기어 글자는 내부 경계를 넘지 않게 맞추고, 페달 게이지의 수치도 막대 폭 안에 맞춘다.

검증: `work/validation-20261002-font-after/build.log`의 Release 빌드 경고·오류 0개. 같은 폴더의 `typography.log`는 12개 화면의 기본/변경 렌더 차이, 설정 저장·재열기, 실제 전후방 거리 창에서 글자 크기 변경 중 창 크기 유지, 속도 글자 축소와 복귀를 확인했다. `client.log`는 197/197, `activity.log`는 111/111 통과했다. 캡처: `typography-timingTower.png`, `typography-raceControl.png`, `typography-speed.png`, `typography-drivingDashboard.png`, `driving-settings-menu.png`.

AMS2 실제 주행 화면의 시인성과 극단적인 긴 사용자 이름/문구의 배치는 이 자동 검증으로 확인하지 못했다.
