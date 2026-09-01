# Changelog

## 0.2.2 — 2026-09-02

- 순위 타워, 전후방 거리, 현재/섹터 타임을 독립 overlay window로 분리
- 세션 정보, 이벤트, Race Control, 멀티 대기 화면을 포함한 각 UI의 개별 이동·크기 조절과 해상도 비율 기반 영구 저장 추가
- 레이아웃 편집 화면에서 순위 타워, 전후방 거리, 현재/섹터 타임 등 UI를 구성 요소별로 끄거나 다시 켜는 기능 추가
- 상태창에 레이아웃 편집/저장·잠금 및 기본 위치 복원 기능 추가
- Timing Tower의 `AMS2 LEAGUE · TIMING`, `리그 순위` 헤더를 제거하고 15개 드라이버 행에 공간 재배분
- 드라이버 이름을 24 px로 확대하고 차량 클래스 및 현재 랩 진행 시간을 행에 추가
- 전후방 차량 글꼴을 확대하고 거리가 증가하면 파란색 `▲`, 감소하면 빨간색 `▼`와 같은 색의 거리 값을 표시
- 일반 황색·이중 황색은 `mHighestFlagColour`로, 전 코스 황색기는 별도 `mYellowFlagState`로 판정해 세 상태를 독립 표시
- 클라이언트 전송 payload의 의미, 서버 정규화/원문 보존, 개인정보 범위와 멀티플레이어 판정 한계를 기록한 데이터 전송 보고서 추가
- 독립 배치의 해상도 변환·경계 제한, 타워 필드, 편집 중 click-through 해제와 종료 후 복원을 자동 검증

## 0.2.1 — 2026-09-01

- 주요 오버레이 패널·글꼴·간격을 약 80% 크기로 축소하고 1920×1080, 2560×1440, 3440×1440 앵커를 고정
- 일반 황색기와 이중 황색기 의미를 분리하고 검증되지 않은 전 코스 황색기 추정을 제거
- 첫 실행 시 로그인 없이 설치별 익명 자격증명을 자동 발급받아 Windows DPAPI로 보호
- 모든 멀티 참가 세션에서 Player/Host 관찰값을 동일한 Session Witness 계약으로 로컬 불변 저장·오프라인 큐 전송
- 동일 세션 다중 증인 그룹화, ±30초 시계 허용, 재시작 attempt 분리, 충돌 보존을 위한 서버 계약 추가
- 개인 활동/Time Attack과 공식 리그 결과 권한을 분리하고 모든 Witness 결과를 관리자 승인 전 `UNCLASSIFIED`로 유지
- Cafe24 FastCGI에서 표준 Authorization이 제거되는 경우를 위한 HTTPS 전용 호환 인증 헤더 추가
- 패널 팔레트를 WPF Brush 리소스로 중앙화하고 실제 OverlayWindow 생성 회귀 테스트 추가

## 0.2 — 2026-09-01

- 일반 실행 시 한국어 첫 실행 상태창과 제품 버전, AMS2/Shared Memory/서버/계정 상태 표시
- 사용자 로그와 활동 데이터를 `%LOCALAPPDATA%\AMS2KRLeague` 아래로 통일
- Player pairing credential을 평문 JSON 대신 Windows DPAPI CurrentUser로 보호
- 기존 평문 bearer 설정이 있으면 보존 가능한 공개 설정은 유지하면서 보호 저장소로 제한적 이전
- 0.1.x의 로컬 경기 이벤트는 더 이상 신뢰하지 않고, Canary 또는 명시적 사용자 식별 필드가 남은 설정은 자격만 무효화해 재연결 요구
- 실제 테스트 닉네임과 개발 PC 절대 경로가 제품 DLL/공개 패키지에 남지 않도록 정리
- Public Player에는 Host Recorder 경로·공식 결과 endpoint·Host credential을 포함하지 않는 경계 테스트 추가
- 서버가 오프라인이거나 계정이 연결되지 않아도 Shared Memory와 오버레이를 독립 실행
- .NET 8 `win-x64` self-contained Portable ZIP 및 일반 사용자용 per-user Installer 패키징 추가
- 기존 Timing Tower, Session Card, Race Control, Waiting Overlay 레이아웃 기준 유지

## 0.1.1 — 2026-09-01

- General Race 개인 결과와 Time Attack 랩의 Player 활동 캡처 추가
- Shared Memory v14의 root 차량, 개인/월드 최고 기록, 날씨, mandatory pit, private session 메타데이터 파싱 추가
- 완료 활동의 로컬 불변 저장, 충돌 격리, 재시작 가능한 업로드 대기열 추가
- 페어링된 Player bearer 전용 HTTPS 업로드와 scheduled event bootstrap 추가
- 30 Hz telemetry callback single-flight, detach 직렬화, 종료 시 callback drain 및 비동기 로그 처리 추가
- 멀티플레이어 메뉴·세션 종료 전환에서 주행 타워 대신 compact Waiting Overlay 표시
- 남은 시간 `-1` 순간의 동일 세션 3초 제한 유지와 비추정 종료 상태 표시
- Race Control을 Session 카드 아래로 이동하고 history-only 빈 카드와 상태 라벨 잘림 제거
- 멀티플레이어 grid/menu/result 전환을 하나의 Player race attempt로 유지하고 terminal 결과를 보존
- Player 전용 telemetry/UI 15개와 활동 캡처·큐·payload 33개 테스트를 공개 빌드에 포함
- Release build/test/self-contained publish/ZIP 검증을 `build-release.ps1`와 CI에 통합
- 웹서비스/server, Host Recorder, Host 인증 형식과 내부 운영 산출물은 공개 범위에서 제외

## 0.1.0 — 2026-09-01

- 한국어 Player Overlay 첫 공개 버전
- AMS2 Shared Memory v14 읽기 전용 연동
- League Classification Timing Tower와 Safety Car 제외
- 트랙 진행거리 기준 앞차·뒤차 정보
- 세션 정보, 랩/섹터 타이밍, Race Control, 동적 이벤트 UI
- 3440×1440 실화면 검증
- click-through, no-activate, bounded multi-window overlay
