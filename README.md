# AMS2 KR League Player Overlay

Automobilista 2의 Shared Memory v14를 읽기 전용으로 사용하는 한국어 Player Overlay입니다.

현재 릴리스: **0.3.1**

안정 기준선: **0.2.2**

## 주요 기능

- League Classification 기준 Timing Tower와 크게 확장한 드라이버명
- 타워 행의 차량 클래스와 참가자별 랩 시간(게임 직접값 / 관측 진행 시간 / 직전 랩 구분)
- F1 중계 그래픽 스타일 전환: 타워 빌드, 추월/피추월 플래시와 순위 숫자 롤, 최속 랩 퍼플 스윕, 카드 슬라이드 인/아웃
- 상태창에서 언제든 각 오버레이 화면을 켜고 끄기(즉시 저장, 재시작 후 복원)
- Safety Car를 순위와 참가자 수에서 제외
- 현재 플레이어 강조
- 물리적 트랙 진행거리 기준 앞차·뒤차 표시
- 랩타임, 섹터, 시간/랩 기반 세션 정보
- 순위 타워, 전후방 거리, 랩/섹터, 세션, 이벤트, Race Control, 대기 화면의 독립 이동·크기 조절
- 일반 황색기, 이중 황색기, 전 코스 황색기를 독립 판정하는 Race Control 상태
- AMS2 상단 알림을 가리지 않는 좌측 Race Control 카드
- 멀티플레이어 세션 전환·차고 대기 전용 compact overlay
- 순위 변화, Personal Best, Fastest Lap, Pit 등 동적 이벤트
- General Race 개인 결과와 Time Attack 랩 자동 캡처
- 활동 기록의 로컬 영구 저장과 재시작 가능한 전송 대기열
- 로그인 없는 설치별 자동 등록과 DPAPI 보호 자격을 이용한 HTTPS 업로드
- 멀티 참가 세션의 전체 관측 결과를 불변 Session Witness로 저장·재전송
- 3440×1440을 포함한 다중 해상도 대응
- 게임 창에 입력을 가로채지 않는 click-through overlay
- P024 고정 Schema A2CT Compact Telemetry의 로컬 durable archive와 public replay 업로드
- 종료 시 Loss Ledger와 Attempt Finalize를 순서대로 보존하는 capture completeness 계약

## 실행 방법

1. [Latest Release](https://github.com/choi3724/AMS2KRLeague/releases/latest)에서 `AMS2-League-Overlay-0.3.1-Setup.exe`를 받습니다.
2. 설치 후 시작 메뉴의 **AMS2 League Overlay**를 실행합니다.
3. AMS2의 `Options → System → Shared Memory`에서 `Project CARS 2`를 선택합니다.
4. AMS2를 Borderless Windowed 또는 Windowed 모드로 실행합니다.

설치 프로그램을 사용하지 않으려면 `AMS2-League-Overlay-0.3.1-win-x64.zip`을 원하는 폴더에 풀고 `AMS2LeagueClient.exe`를 실행해도 됩니다. 별도 .NET 설치나 명령줄 설정은 필요하지 않습니다.

게임이 실행되지 않았거나 Shared Memory를 사용할 수 없으면 오버레이는 대기 상태로 유지됩니다. 프로그램은 게임 설정, 실행 파일, 저장 파일을 자동 변경하지 않습니다.

멀티플레이어 세션 전환 중에는 주행용 Timing Tower 대신 세션 종류와 참가자 수를 담은 작은 대기 오버레이가 표시됩니다. `mEventTimeRemaining`이 순간적으로 `-1`이 되면 같은 세션에서 확인한 마지막 값만 최대 3초 유지하며, 이후에는 추정 시간을 만들지 않고 `종료 처리 중`, `세션 종료 대기` 또는 관측된 결과 상태에 따른 `세션 종료`를 표시합니다.

## 오버레이 위치와 크기 조절

상태창에서 **레이아웃 편집**을 누르면 각 UI가 독립된 청록색 편집 테두리로 표시됩니다. 테두리 안을 드래그해 이동하고 오른쪽 아래 손잡이로 크기를 조절합니다. **저장 후 잠금**을 누르면 `%LOCALAPPDATA%\AMS2KRLeague\overlay-layout.json`에 현재 게임 해상도 대비 비율로 저장되고 다시 click-through 상태가 됩니다. **기본 위치 복원**은 저장된 배치를 삭제하고 기본 배치를 즉시 적용합니다.

타워는 폭에 맞춰 글자를 조절하고 높이에 따라 표시 행을 즉시 늘립니다(2~64행, 상위 순위 우선/범위 밖 플레이어 마지막 고정). 다른 카드는 가로·세로 크기를 독립적으로 적용합니다. Race Control은 글자를 찌그러뜨리지 않고 창 크기에 맞춰 줄바꿈·배치를 조절하며, 낮은 높이에서는 보조 이력을 숨기고 현재 알림을 우선합니다.

타워의 접두사 없는 현재 시간은 로컬 플레이어의 AMS2 직접값입니다. 상대 차량의 `~0:12.345`는 해당 차량의 실제 스타트/피니시 라인 통과 이후 관측한 UI용 시간이며 공식 기록이 아닙니다. 아직 라인 통과를 관측하지 못했거나 데이터가 끊기면 AMS2 직전 랩 `L1:40.123` 또는 `--`를 표시합니다. Practice/Qualifying 완료는 유효 Best Lap, Race 완료는 `FIN`, 중도 종료는 `RET`/`DNF`/`DSQ`로 표시합니다. 관측 시계는 결과/Compact 업로드에 사용하지 않습니다.

순위 타워와 전후방 거리, 현재/섹터 타임은 서로 독립된 창이므로 각각 다른 위치와 크기를 사용할 수 있습니다. 레이아웃 편집 중에는 평소 조건에 따라 숨겨지는 이벤트 및 Race Control UI도 함께 배치할 수 있습니다. 상태창의 **표시할 오버레이** 체크박스는 편집 모드와 무관하게 언제든 사용할 수 있으며, 해제하면 해당 화면이 즉시 꺼지고 설정이 저장되어 다음 실행에도 유지됩니다. **모두 켜기**/**모두 끄기**로 한 번에 바꿀 수 있습니다.

전후방 거리 값의 색상은 사용자에게 불리한 변화(앞차와 멀어짐, 뒷차가 가까워짐)를 빨간색, 유리한 변화를 파란색으로 표시하며 2 m 이내의 미세한 진동은 무시합니다. 일반 황색기·이중 황색기는 플래그 색상으로 구분하고, 전 코스 황색기는 AMS2 Shared Memory의 별도 FCY 진행 상태로 판정합니다.

## 상태와 Player 활동 기록

처음 실행하면 AMS2, Shared Memory, 서버, 계정 상태를 확인할 수 있습니다. 로그와 개인 활동 기록은 `%LOCALAPPDATA%\AMS2KRLeague`에 저장되며 설치 폴더에는 사용자 데이터를 기록하지 않습니다.

실제 플레이 중인 로컬 참가자의 General Race 개인 결과와 Time Attack 랩은 로컬에 기록됩니다. 기록에는 Shared Memory v14에서 관찰한 차량·트랙·랩·날씨·세션 메타데이터만 사용하며, 클라이언트가 공식 순위나 승인 상태를 주장하지 않습니다.

첫 네트워크 사용 때 로그인이나 수동 코드 입력 없이 설치별 익명 자격을 자동 발급받습니다. 자격은 Windows DPAPI CurrentUser로 보호되며 다른 Windows 사용자나 다른 PC에서 복호화할 수 없습니다. 서버가 오프라인이어도 로컬 기록과 오버레이는 계속 동작하고, 대기 중인 개인 활동 및 Session Witness는 서버 복구 후 자동 재전송됩니다.

일반 Player도 Shared Memory가 제공하는 참가자 배열과 세션 전환을 Session Witness로 기록합니다. 이는 공식 결과가 아니라 독립 Evidence이며, 한 Client만 관측해도 보존됩니다. 같은 경기를 여러 Client가 보내면 서버에서 하나의 세션 그룹으로 묶고 원본 Witness는 각각 유지합니다.

서버로 전송되는 각 정보의 의미, 저장 위치, 전송되지 않는 정보와 현재 판정 한계는 [클라이언트-서버 전송 데이터 분석 보고서](docs/CLIENT_SERVER_DATA_TRANSMISSION_REPORT_2026-09-02.md)에 정리되어 있습니다.

## 안전 경계

- `$pcars2$` Shared Memory mapping을 읽기 권한으로만 엽니다.
- DLL injection, DirectX hook, 프로세스 메모리 쓰기, 패킷 가로채기를 사용하지 않습니다.
- AMS2 설치 파일, 설정 파일, 레지스트리, 방화벽을 수정하지 않습니다.
- 업로드는 절대 HTTPS URL, 고정된 Player endpoint, 리디렉션 금지 정책을 사용합니다.
- Cafe24 FastCGI 호환 헤더는 표준 Bearer와 동일한 설치 토큰을 같은 HTTPS 요청에만 중복 전달하며 로그에 기록하지 않습니다.
- Player payload에는 서버 사용자 ID, 공식 판정, 승인 상태 같은 권한 주장을 넣지 않습니다.
- 웹서비스/server 구현, 공식 결과 업로더와 Host 인증 자격은 이 저장소와 릴리스에 포함하지 않습니다.
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

첫 공개 버전은 `0.1.0`, 현재 릴리스는 `0.3.0`, 안정 기준선은 `0.2.2`입니다. 자세한 규칙은 [VERSIONING.md](VERSIONING.md)를 참고하십시오.

## 현재 제한사항

- Replay/Spectator에서는 viewed participant가 실제 로컬 플레이어와 다를 수 있어 Player HUD를 숨깁니다.
- AMS2 Shared Memory가 제공하지 않는 구체적인 penalty reason은 추론해서 표시하지 않습니다.
- Shared Memory v14가 제공하지 않는 세션 종료 cooldown 숫자는 추정하지 않습니다.
- Player 활동과 Session Witness는 공식 경기 승인이 아니며, 공식 리그 기록은 서버 관리자 승인 후에만 확정됩니다.
- Shared Memory v14에는 권위 있는 멀티플레이어 여부 필드가 없어 현재 Witness 수집 자격은 관련 세션에서 둘 이상의 참가자가 관측되었는지로 판단합니다. 이 값은 온라인 또는 공식 경기라는 주장으로 사용하지 않습니다.
- 코드 서명 인증서가 없어 Windows SmartScreen 경고가 표시될 수 있습니다.
- 서버의 운영·저장·승인 로직과 Host 경기 결과 수집은 이 사용자용 저장소 범위에 포함하지 않습니다.
- Closed Beta Compact Telemetry는 실제 멀티플레이어·장시간·Incident 검증 중이며, private Driver stream은 authoritative owner attestation이 없어 서버 업로드가 차단됩니다.
