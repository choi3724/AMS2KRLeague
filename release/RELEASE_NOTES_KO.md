# AMS2 League Overlay 0.2

일반 사용자가 별도 개발 도구 없이 설치하거나 압축을 풀어 실행할 수 있는 첫 정식 0.2 패키지입니다.

## 주요 변경

- 한국어 첫 실행 상태창에서 AMS2, Shared Memory, 서버, 계정 상태 확인
- Timing Tower, 앞차/뒤차, 세션 카드, Race Control, 동적 이벤트 및 멀티플레이어 대기 화면 제공
- 로그와 개인 활동 기록을 `%LOCALAPPDATA%\AMS2KRLeague`에 저장
- Player 연결 자격을 평문 JSON이 아닌 Windows DPAPI CurrentUser로 보호
- 0.1.x 평문 자격은 보호 저장소로 이전하며, 과거 Canary/명시적 사용자 식별 설정은 자동 신뢰하지 않고 재연결 요구
- 서버 또는 계정이 오프라인이어도 Shared Memory 오버레이와 로컬 기록은 독립 동작
- .NET 8 self-contained `win-x64` Portable ZIP과 현재 사용자용 Installer 제공

## 설치

1. `AMS2-League-Overlay-0.2-Setup.exe`를 실행해 현재 사용자용으로 설치합니다.
2. AMS2 옵션의 Shared Memory를 `Project CARS 2`로 설정합니다.
3. 시작 메뉴에서 `AMS2 League Overlay`를 실행한 뒤 AMS2를 실행합니다.

설치하지 않으려면 ZIP을 원하는 폴더에 풀고 `AMS2LeagueClient.exe`를 실행하면 됩니다. .NET SDK, Visual Studio, PowerShell 명령, JSON 편집은 필요하지 않습니다.

## 보안 경계

- 이 패키지는 AMS2 Shared Memory를 읽기 전용으로 사용하며 게임 파일·설정·프로세스 메모리를 변경하지 않습니다.
- 공식 경기 결과 쓰기는 운영자용 Host Recorder만 수행합니다. Player 패키지에는 Host credential과 공식 결과 endpoint가 포함되지 않습니다.
- 공개 계정 연결이 열리기 전에는 계정이 `연결 안 됨`으로 표시되며 개인 기록 서버 전송은 비활성입니다.
- 코드 서명 인증서가 없어 설치 시 Windows SmartScreen 경고가 표시될 수 있습니다.

## SHA-256

릴리스에 함께 첨부된 `SHA256SUMS.txt`에서 ZIP과 Installer 해시를 확인할 수 있습니다.

지원: https://github.com/choi3724/AMS2KRLeague/issues
