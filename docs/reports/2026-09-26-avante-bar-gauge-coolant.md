# N 확장 계기판 막대 윤곽과 냉각수 동작 수정 후보

## 기준과 원본

- 공개 v0.8.0 HEAD `e388489e9fb7e67ded908a63bd3672e7cd476b3b`; 0.8.1 투명 배경 수정이 남아 있는 별도 빌드 트리에서 작업. 이전 비공개 후보와 원본 저장소·설치본은 보존한다.
- 원본 `F:\Users\choi3\Downloads\AvanteN_Cluster_Preview (2).html`과 그 PNG `src/AMS2LeagueClient/Assets/Hud/avante-background.png`를 대조했다. 원본 HTML은 89°C 숫자를 표시하지만 C/H 막대를 동적으로 갱신하지 않고 PNG의 고정 색상을 그대로 사용한다. 새 움직임은 게임 원본의 온도 경계가 아니라 우리 오버레이의 표시 기능이다.
- 수정 전 WPF·명시적 DComp N은 막대 내부에 265×27 직사각형을 덧그렸다. 원본의 사선 캡과 하단 붉은 테두리까지 덮는 범위였다. 냉각수는 WPF의 `Gauge(..., null)` 및 DComp의 어두운 고정 사각형 때문에 관측 수온과 무관하게 비어 있었다.

## 변경

- `AvanteBarGauge.cs`: PNG에서 측정한 원본 좌표의 안쪽 사다리꼴 윤곽 두 개를 공유한다. 고정 트랙은 준비 시 생성하고, 채움 끝점만 관측값 변화 시 이동한다. 원본 PNG와 외곽 붉은 테두리, 글자, RPM 자원은 변경하지 않았다.
- 연료는 기존 0..1 관측 비율을 사용한다. 냉각수는 유효한 `WaterTemperatureCelsius`를 **표시 전용** 40°C(C)~120°C(H)에 선형 대응한다. 예시 89°C는 61.25%다. 실제 차량의 경고·냉각 한계 또는 게임 제공 C/H 경계로 해석하지 않는다. 결측·비정상 값에는 채움을 표시하지 않고 기존 숫자 유효성 정책을 유지한다.
- 기본 WPF는 `AvanteClusterView.cs`의 retained geometry를 재사용한다. 명시적 DComp N은 `RetainedAvanteHud.cs`에서 같은 윤곽을 사용하고 온도 변화에만 채움 geometry를 교체한다. 냉각수 변화의 dirty 영역에 막대 위치를 포함하며 종료 때 native geometry를 해제한다.
- 수집·기록·전송 cadence 및 데이터 값, 사용자 배치·폰트·이미지·RPM 정책은 변경하지 않았다.

## 검증과 제한

- WPF 2048×750 캡처 `work/avante-bar-captures/avante-bars-{empty,middle,full}.png`에서 연료·냉각수의 빈/중간/완충 및 서로 반대 방향으로 기울어진 양끝을 픽셀로 확인했다. 89°C 중간 캡처의 채움은 원본의 내부 테두리와 일치한다.
- 표시 숫자가 같아도 미세한 연료·수온 변화에서 채움이 변하고 정적 상태 텍스트를 재생성하지 않는 회귀가 통과했다. 유효하지 않은 수온은 채움을 비운다. 명시적 DComp N의 HWND·자원 수명 회귀도 통과했다.
- 불꽃·상태 표시 수정 전 0.8.1 비공개 게이지 후보: Release 빌드 warning/error 0/0, Client 179/179, Activity 111/111. 폴더·ZIP 각 480개 파일 및 Setup 감사에서 금지 파일 0건. ZIP SHA256 `45dd8292e03fc26a9608a3754c5e5a6ab3e8cb22124f1cb2224fb840ccbd9b86`, Setup SHA256 `04cfb235fb987e7956b53f6d88e25e57a0c36e002d3f72e6dbfafe6edda15c93`; manifest와 실제 파일 일치. `work/hotfix-0.8.1-gauges-build.log` 참조. 최종 후보의 새 수치는 `2026-09-26-avante-ignition-transition.md`에 기록한다.
- 패키지 Client 데모는 업데이트·운영 업로드를 비활성화하고 10초 실행 후 exit 0. `work/hotfix-0.8.1-gauges-smoke/`의 버전·기본 layered 스타일 로그를 확인한다. 실제 게임의 차량별 수온 범위와 물리 화면 시각 결과, VR·트리플 모니터, 프레임 성능은 **NOT TESTED**다. Monitor RED 유지. NuGet 취약점 감사 엔드포인트는 접근 불가해 `NuGetAudit=false`로 복원했으며 취약점 감사도 **NOT TESTED**다.
- 이 단계에서는 비공개·미커밋 후보였으며 설치 교체나 commit/tag/push/release는 하지 않았다.
