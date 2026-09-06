AMS2 League Overlay 0.4.0
============================================

빠른 시작
1. AMS2 옵션 → 시스템 → 공유 메모리에서 Project CARS 2를 선택합니다.
2. AMS2를 Borderless Windowed 또는 Windowed 모드로 실행합니다.
3. AMS2LeagueClient.exe를 실행합니다.

첫 실행 창에서 AMS2, Shared Memory, 서버, 계정 상태를 확인할 수 있습니다.
별도 .NET 설치, PowerShell, JSON 편집, API 토큰 입력은 필요하지 않습니다.

현재 지원
- 개별 이동·크기 조절·표시 여부를 저장할 수 있는 한국어 Overlay UI
- 클래스·참가자별 최고 랩·아웃랩 단계를 표시하는 Timing Tower와 피트/트랙을 구분하는 앞차/뒤차 UI
- 가로·세로 독립 패널 크기와 찌그러지지 않는 자동 글꼴 배율
- 일반 황색기·이중 황색기·전 코스 황색기를 구분하는 Race Control
- 자유 연습·예선·레이스 대기 화면과 싱글/멀티 표시 선택(자동 감지 아님)
- 개인 General Race 및 Time Attack 기록의 로컬 저장
- 로그인 없는 설치별 자동 등록 및 DPAPI 보호 전송
- 전체 참가자 Session Witness의 불변 저장과 오프라인 자동 재전송
- P024 Compact Telemetry의 public Replay/Race Story/Incident 원본 로컬 저장과 자동 재전송
- attempt 종료 시 Loss Ledger 및 최종 ACK 보존

현재 제한
- Player가 전송하는 기록과 Session Witness는 Evidence이며 공식 승인이 아닙니다.
- 공식 리그 기록 확정은 서버 관리자만 수행할 수 있습니다.
- AMS2 Shared Memory에는 권위 있는 멀티플레이어 여부 값이 없어 둘 이상의 관측 참가자를 수집 자격으로 사용합니다.
- GitHub Latest로 배포됩니다. Compact 수집의 장시간 완전성과 세션 경계는 계속 검증 중입니다.
- 기존 403 격리 항목을 자동 재전송하지 않으며 cadence 손실은 PARTIAL로 보존합니다.
- private Driver telemetry는 로컬에만 보존되며 사용자 차량 소유권을 증명할 수 있기 전에는 서버로 전송하지 않습니다.
- 코드 서명 인증서가 없어 Windows SmartScreen 경고가 표시될 수 있습니다.

데이터 위치
%LOCALAPPDATA%\AMS2KRLeague

제거
- 설치판: Windows 설정 → 앱 → 설치된 앱 → AMS2 League Overlay에서 제거
- Portable: 압축을 푼 폴더 삭제
- 개인 기록까지 지우려면 제거 후 %LOCALAPPDATA%\AMS2KRLeague를 사용자가 직접 삭제합니다.

지원
https://github.com/choi3724/AMS2KRLeague/issues
