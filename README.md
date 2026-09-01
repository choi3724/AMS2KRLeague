# AMS2 KR League Player Overlay

Automobilista 2의 Shared Memory v14를 읽기 전용으로 사용하는 한국어 Player Overlay입니다.

현재 버전: **0.2**

## 주요 기능

- League Classification 기준 Timing Tower
- Safety Car를 순위와 참가자 수에서 제외
- 현재 플레이어 강조
- 물리적 트랙 진행거리 기준 앞차·뒤차 표시
- 랩타임, 섹터, 시간/랩 기반 세션 정보
- 황색기, 적색기, 체커드 등 Race Control 상태
- AMS2 상단 알림을 가리지 않는 좌측 Race Control 카드
- 멀티플레이어 세션 전환·차고 대기 전용 compact overlay
- 순위 변화, Personal Best, Fastest Lap, Pit 등 동적 이벤트
- General Race 개인 결과와 Time Attack 랩 자동 캡처
- 활동 기록의 로컬 영구 저장과 재시작 가능한 전송 대기열
- 페어링된 Player bearer가 있을 때만 사용하는 선택적 HTTPS 업로드
- 3440×1440을 포함한 다중 해상도 대응
- 게임 창에 입력을 가로채지 않는 click-through overlay

## 실행 방법

1. [Releases](https://github.com/choi3724/AMS2KRLeague/releases)에서 `AMS2-League-Overlay-0.2-Setup.exe`를 받습니다.
2. 설치 후 시작 메뉴의 **AMS2 League Overlay**를 실행합니다.
3. AMS2의 `Options → System → Shared Memory`에서 `Project CARS 2`를 선택합니다.
4. AMS2를 Borderless Windowed 또는 Windowed 모드로 실행합니다.

설치 프로그램을 사용하지 않으려면 `AMS2-League-Overlay-0.2-win-x64.zip`을 원하는 폴더에 풀고 `AMS2LeagueClient.exe`를 실행해도 됩니다. 별도 .NET 설치나 명령줄 설정은 필요하지 않습니다.

게임이 실행되지 않았거나 Shared Memory를 사용할 수 없으면 오버레이는 대기 상태로 유지됩니다. 프로그램은 게임 설정, 실행 파일, 저장 파일을 자동 변경하지 않습니다.

멀티플레이어 세션 전환 중에는 주행용 Timing Tower 대신 세션 종류와 참가자 수를 담은 작은 대기 오버레이가 표시됩니다. `mEventTimeRemaining`이 순간적으로 `-1`이 되면 같은 세션에서 확인한 마지막 값만 최대 3초 유지하며, 이후에는 추정 시간을 만들지 않고 `종료 처리 중`, `세션 종료 대기` 또는 관측된 결과 상태에 따른 `세션 종료`를 표시합니다.

## 상태와 Player 활동 기록

처음 실행하면 AMS2, Shared Memory, 서버, 계정 상태를 확인할 수 있습니다. 로그와 개인 활동 기록은 `%LOCALAPPDATA%\AMS2KRLeague`에 저장되며 설치 폴더에는 사용자 데이터를 기록하지 않습니다.

실제 플레이 중인 로컬 참가자의 General Race 개인 결과와 Time Attack 랩은 로컬에 기록됩니다. 기록에는 Shared Memory v14에서 관찰한 차량·트랙·랩·날씨·세션 메타데이터만 사용하며, 클라이언트가 공식 순위나 승인 상태를 주장하지 않습니다.

공개 계정 연결은 현재 운영 포털의 Steam 로그인 기능이 준비되는 동안 **연결 안 됨**으로 안전하게 동작합니다. 연결 정보가 없으면 네트워크 업로드는 실행되지 않으며 로컬 오버레이에는 영향이 없습니다. 향후 발급되는 연결 credential은 Windows DPAPI로 현재 Windows 사용자에게만 복호화되도록 저장합니다.

## 안전 경계

- `$pcars2$` Shared Memory mapping을 읽기 권한으로만 엽니다.
- DLL injection, DirectX hook, 프로세스 메모리 쓰기, 패킷 가로채기를 사용하지 않습니다.
- AMS2 설치 파일, 설정 파일, 레지스트리, 방화벽을 수정하지 않습니다.
- 업로드는 절대 HTTPS URL, 고정된 Player endpoint, 리디렉션 금지 정책을 사용합니다.
- Player payload에는 서버 사용자 ID, 공식 판정, 승인 상태 같은 권한 주장을 넣지 않습니다.
- 웹서비스/server 구현, Host Recorder 코드와 Host 인증 형식은 이 저장소와 릴리스에 포함하지 않습니다.
- pairing credential, 비밀, 실제 사용자 설정과 내부 검증 산출물은 Git과 공개 패키지에 포함하지 않습니다.

## 소스 빌드

요구 사항:

- Windows x64
- .NET 8 SDK

```powershell
dotnet restore .\AMS2KRLeague.sln
dotnet build .\AMS2KRLeague.sln -c Release
```

Player telemetry와 활동 캡처 테스트:

```powershell
dotnet run --project .\tests\AMS2LeagueClient.Tests\AMS2LeagueClient.Tests.csproj -c Release --no-build
dotnet run --project .\tests\AMS2LeagueActivity.Tests\AMS2LeagueActivity.Tests.csproj -c Release --no-build
```

사용자 배포 ZIP 생성:

```powershell
.\scripts\build-release.ps1
```

릴리스 스크립트는 선언 버전 일치 여부를 확인한 뒤 Release build, 두 Player 테스트 모음, self-contained `win-x64` publish와 ZIP SHA-256 생성을 순서대로 수행합니다. 생성물은 `artifacts/`에 저장되며 Git에는 포함되지 않습니다.

## 버전 정책

첫 공개 버전은 `0.1.0`, 현재 Player Client 제품 버전은 `0.2`입니다. 자세한 규칙은 [VERSIONING.md](VERSIONING.md)를 참고하십시오.

## 현재 제한사항

- Replay/Spectator에서는 viewed participant가 실제 로컬 플레이어와 다를 수 있어 Player HUD를 숨깁니다.
- AMS2 Shared Memory가 제공하지 않는 구체적인 penalty reason은 추론해서 표시하지 않습니다.
- Shared Memory v14가 제공하지 않는 세션 종료 cooldown 숫자는 추정하지 않습니다.
- Player 활동 기록은 개인 기록이며 공식 경기 결과를 대체하지 않습니다.
- Steam 계정 연결과 개인 기록 서버 동기화는 운영 포털 인증이 열리기 전까지 사용할 수 없습니다.
- 코드 서명 인증서가 없어 Windows SmartScreen 경고가 표시될 수 있습니다.
- 서버의 운영·저장·승인 로직과 Host 경기 결과 수집은 이 사용자용 저장소 범위에 포함하지 않습니다.
