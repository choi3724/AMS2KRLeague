# Changelog

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
