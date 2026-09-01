# AMS2 League Overlay 0.2.2

오버레이 구성 요소를 운전자 환경에 맞게 직접 배치하고 불필요한 UI를 끌 수 있는 사용자 설정 릴리스입니다.

## 주요 변경

- 순위 타워, 전후방 거리, 현재/섹터 타임, 세션 정보, 이벤트, Race Control, 대기 화면을 독립 창으로 분리
- 상태창의 레이아웃 편집에서 각 UI를 이동·크기 조절하고 해상도 비율 기반으로 저장
- 구성 요소별 체크박스로 필요 없는 UI를 끄거나 저장된 위치에서 다시 활성화
- Timing Tower의 중복 헤더를 제거하고 확대된 드라이버명, 클래스, 현재 랩 진행 시간을 표시
- 전후방 거리가 증가하면 파란색 `▲`, 감소하면 빨간색 `▼`와 같은 색의 거리 값을 표시
- 일반 황색기와 이중 황색기는 플래그 색상으로, 전 코스 황색기는 별도 `mYellowFlagState`로 독립 판정
- 클라이언트가 서버로 전송하는 필드의 의미·저장 위치·개인정보 범위를 설명하는 상세 보고서 포함
- 기존 익명 등록, DPAPI 자격 보호, 개인 활동 및 Session Witness 오프라인 재전송 유지
- .NET 8 self-contained `win-x64` Portable ZIP과 현재 사용자용 Installer 제공

## 설치

1. `AMS2-League-Overlay-0.2.2-Setup.exe`를 실행해 현재 사용자용으로 설치합니다.
2. AMS2 옵션의 Shared Memory를 `Project CARS 2`로 설정합니다.
3. 시작 메뉴에서 `AMS2 League Overlay`를 실행한 뒤 AMS2를 실행합니다.

설치하지 않으려면 ZIP을 원하는 폴더에 풀고 `AMS2LeagueClient.exe`를 실행하면 됩니다. .NET SDK, Visual Studio, PowerShell 명령, JSON 편집은 필요하지 않습니다.

## 보안 경계

- 이 패키지는 AMS2 Shared Memory를 읽기 전용으로 사용하며 게임 파일·설정·프로세스 메모리를 변경하지 않습니다.
- 공식 경기 결과 쓰기는 운영자용 Host Recorder만 수행합니다. Player 패키지에는 Host credential과 공식 결과 endpoint가 포함되지 않습니다.
- 익명 Client는 개인 활동과 Session Witness만 전송할 수 있고 공식 승인·관리자 권한·다른 사용자 수정은 할 수 없습니다.
- 코드 서명 인증서가 없어 설치 시 Windows SmartScreen 경고가 표시될 수 있습니다.

## SHA-256

릴리스에 함께 첨부된 `SHA256SUMS-0.2.2.txt`에서 ZIP과 Installer 해시를 확인할 수 있습니다.

지원: https://github.com/choi3724/AMS2KRLeague/issues
