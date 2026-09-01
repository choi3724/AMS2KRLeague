# AMS2 League Overlay 0.2.1

일반 사용자가 별도 개발 도구 없이 설치하거나 압축을 풀어 실행할 수 있는 첫 정식 0.2 패키지입니다.

## 주요 변경

- 한국어 첫 실행 상태창에서 AMS2, Shared Memory, 서버, 계정 상태 확인
- Timing Tower, 앞차/뒤차, 세션 카드, Race Control, 동적 이벤트 및 멀티플레이어 대기 화면 제공
- AMS2 `Yellow`와 `DoubleYellow`를 각각 `황색기`, `이중 황색기`로 구분하고 관측되지 않은 전 코스 황색기를 추정하지 않음
- 로그와 개인 활동 기록을 `%LOCALAPPDATA%\AMS2KRLeague`에 저장
- Player 연결 자격을 평문 JSON이 아닌 Windows DPAPI CurrentUser로 보호
- 0.1.x 평문 자격은 보호 저장소로 이전하며, 과거 Canary/명시적 사용자 식별 설정은 자동 신뢰하지 않고 재연결 요구
- 서버 또는 계정이 오프라인이어도 Shared Memory 오버레이와 로컬 기록은 독립 동작
- 로그인·Steam·수동 페어링 없이 설치별 익명 자격을 자동 발급하고 DPAPI로 보호
- 한 명의 Overlay만 실행해도 전체 Session Witness를 불변 보존하며, 다중 Client Evidence는 중복 경기 없이 서버에서 그룹화
- `FULL_SESSION`, `MID_SESSION`, `END_ONLY` 완전성과 단계별 결과 충돌을 구분하고 모든 공식 승인은 관리자에게만 허용
- Cafe24 공유호스팅에서도 익명 자격 전송이 유지되도록 HTTPS 호환 인증 헤더를 함께 사용
- 실제 WPF OverlayWindow 로딩 검사를 릴리스 테스트에 포함
- .NET 8 self-contained `win-x64` Portable ZIP과 현재 사용자용 Installer 제공

## 설치

1. `AMS2-League-Overlay-0.2.1-Setup.exe`를 실행해 현재 사용자용으로 설치합니다.
2. AMS2 옵션의 Shared Memory를 `Project CARS 2`로 설정합니다.
3. 시작 메뉴에서 `AMS2 League Overlay`를 실행한 뒤 AMS2를 실행합니다.

설치하지 않으려면 ZIP을 원하는 폴더에 풀고 `AMS2LeagueClient.exe`를 실행하면 됩니다. .NET SDK, Visual Studio, PowerShell 명령, JSON 편집은 필요하지 않습니다.

## 보안 경계

- 이 패키지는 AMS2 Shared Memory를 읽기 전용으로 사용하며 게임 파일·설정·프로세스 메모리를 변경하지 않습니다.
- 공식 경기 결과 쓰기는 운영자용 Host Recorder만 수행합니다. Player 패키지에는 Host credential과 공식 결과 endpoint가 포함되지 않습니다.
- 익명 Client는 개인 활동과 Session Witness만 전송할 수 있고 공식 승인·관리자 권한·다른 사용자 수정은 할 수 없습니다.
- 코드 서명 인증서가 없어 설치 시 Windows SmartScreen 경고가 표시될 수 있습니다.

## SHA-256

릴리스에 함께 첨부된 `SHA256SUMS-0.2.1.txt`에서 ZIP과 Installer 해시를 확인할 수 있습니다.

지원: https://github.com/choi3724/AMS2KRLeague/issues
