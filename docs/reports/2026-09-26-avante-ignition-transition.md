# N 계기판 진입 연출 수정

- 참고 영상: <https://youtu.be/IuiPvqVubu4?t=1>. 약 11~12초의 N 모드 전환에서 바깥 불꽃과 안쪽 파란 링이 나타난 뒤 사라진다. 프레임별 동작은 참고이며 원본 영상 자산은 복제하지 않았다.
- 기존 후보의 동일한 삼각형·곡선 반복으로 만든 불꽃은 톱니처럼 보였다. 정적인 밝은 원형 띠도 불꽃을 가렸다. 두 도형을 제거하고 투명 불꽃 PNG `Assets/Hud/avante-ignition-flame.png`를 새로 생성하여 재사용한다.
- WPF와 실험용 DComp N 경로는 같은 PNG를 준비 후 재사용하고, 약 0.9초 동안 부채꼴 클립으로 0→끝을 드러내며 안쪽 파란 링을 함께 밝힌 뒤 완전히 숨긴다. RPM·바늘은 연출 진행값에 참여하지 않는다.
- 기본 WPF 경로는 N HUD 인스턴스 최초 표시 때 한 번 재생한다. 일시적 숨김·VR/편집 표시 전환으로는 재무장하지 않으며, 옵션을 껐다 다시 켜 새 HUD 인스턴스가 만들어질 때만 다시 재생한다. DComp N 실험 경로도 새 HUD 표면의 첫 갱신 때 한 번 재생한다.
- `work/avante-081-detail/avante-ignition-{early,middle,near-end,actual-size}.png`를 확인했다. 실제 크기(820×300)에서 바깥 불꽃이 이전처럼 두꺼운 불투명 띠나 톱니로 보이지 않으며, 정지 후 연출이 남지 않는다. 이 캡처는 WPF 렌더 결과로, 실게임·물리 모니터·VR 검증은 아니다.
- 최종 0.8.1 빌드: 경고/오류 0/0, Client 180/180, Activity 111/111. 패키지 폴더·ZIP 각 480개 파일 및 Setup 감사에서 금지 항목 0개. 10초 비운영 데모 실행은 exit 0이며 시작 로그는 `version=0.8.1`, `MONITOR_PRESENTATION_PATH wpf-layered`를 기록했다. `work/hotfix-0.8.1-ignition-build.log`와 `work/hotfix-0.8.1-ignition-smoke/` 참조.
- 최종 ZIP SHA256 `4283f965b338302c060d85c481d5c4b3522be720ee655975688baf90aa5fe642`, Setup SHA256 `90844463e1d50759cbe88b064d2ed0e8cc3b764991af225deaa9d496a9804b36`. 이전 비공개 후보는 `work/candidates/0.8.1-pre-ignition/`에 보존했다. NuGet 취약점 감사와 실게임·VR·트리플 모니터 시각/성능은 미검증이며 Monitor 성능은 RED다.
