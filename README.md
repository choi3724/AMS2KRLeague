# AMS2 KR League Player Overlay

Automobilista 2의 Shared Memory v14를 읽기 전용으로 사용하는 한국어 Player Overlay입니다.

현재 버전 및 공개 Latest: **0.4.1**

안정 기준선: **0.2.2**

## 주요 기능

- League Classification 기준 Timing Tower와 크게 확장한 드라이버명
- 타워 행의 차량 클래스와 참가자별 AMS2 최고 랩 기록, 기록 인정 전 아웃랩/주행 상태 표시
- F1 중계 그래픽 스타일 전환: 타워 빌드, 추월/피추월 플래시와 순위 숫자 롤, 최속 랩 퍼플 스윕, 카드 슬라이드 인/아웃
- 상태창에서 언제든 각 오버레이 화면을 켜고 끄기(즉시 저장, 재시작 후 복원)
- Safety Car를 순위와 참가자 수에서 제외
- 현재 플레이어 강조
- 트랙에서는 진행거리, 피트에서는 같은 피트 영역의 월드 좌표 기준 앞차·뒤차 표시
- 랩타임, 섹터, 시간/랩 기반 세션 정보
- 순위 타워, 전후방 거리, 랩/섹터, 세션, 이벤트, Race Control, 대기 화면의 독립 이동·크기 조절
- 일반 황색기, 이중 황색기, 전 코스 황색기를 독립 판정하는 Race Control 상태
- AMS2 상단 알림을 가리지 않는 좌측 Race Control 카드
- 자유 연습·예선·레이스 세션 대기 화면과 싱글/멀티 표시 선택
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

1. [Latest Release](https://github.com/choi3724/AMS2KRLeague/releases/latest)에서 `AMS2-League-Overlay-0.4.1-Setup.exe`를 다운로드하고 실행합니다.
2. 설치 후 시작 메뉴의 **AMS2 League Overlay**를 실행합니다.
3. AMS2의 `Options → System → Shared Memory`에서 `Project CARS 2`를 선택합니다.
4. AMS2를 Borderless Windowed 또는 Windowed 모드로 실행합니다.

설치 프로그램을 사용하지 않으려면 `AMS2-League-Overlay-0.4.1-win-x64.zip`을 원하는 폴더에 풀고 `AMS2LeagueClient.exe`를 실행해도 됩니다. 별도 .NET 설치나 명령줄 설정은 필요하지 않습니다.

게임이 실행되지 않았거나 Shared Memory를 사용할 수 없으면 오버레이는 대기 상태로 유지됩니다. 프로그램은 게임 설정, 실행 파일, 저장 파일을 자동 변경하지 않습니다.

세션 전환·대기 중에는 세션 종류와 참가자 수를 담은 작은 대기 오버레이가 표시됩니다. 자유 연습/예선/레이스는 게임 데이터로 구분합니다. 싱글/멀티는 SHM에 확정 필드가 없어 상태창의 **대기 화면 플레이 모드**에서 선택하며, 선택 전에는 모드 미확인으로 표시합니다. 이 선택은 UI 전용이고 수집·업로드 권한을 바꾸지 않습니다. `mEventTimeRemaining`이 순간적으로 `-1`이 되면 같은 세션에서 확인한 마지막 값만 최대 3초 유지하며, 이후에는 추정 시간을 만들지 않고 `종료 처리 중`, `세션 종료 대기` 또는 관측된 결과 상태에 따른 `세션 종료`를 표시합니다.

## 자동 업데이트 (0.4.1부터)

시작 시와 6시간마다 공개 GitHub Latest 릴리스를 확인합니다. 현재 버전보다 새 버전이면 설치 파일을 자동 다운로드하고 크기와 SHA-256을 검증합니다. AMS2가 실행 중이면 게임 종료까지 설치를 미룹니다. 기록 저장을 정상 종료한 뒤 같은 폴더에 설치하고 프로그램을 다시 실행합니다. 상태창에서 확인·다운로드·설치 결과를 볼 수 있습니다.

- 다운로드나 검증 실패 시 기존 프로그램은 계속 실행되며 6시간 후 다시 확인합니다. 설치 파일의 원격 주소는 `choi3724/AMS2KRLeague` 공개 릴리스로 제한합니다.
- 설정과 기록은 기존 `%LOCALAPPDATA%\AMS2KRLeague`에 유지합니다. ZIP 실행본도 현재 폴더에 적용하며 포터블 상태를 유지합니다. 설치판은 기존 Windows 제거 항목을 갱신합니다.
- 설치 경로에 쓰기 권한이 필요합니다. 게임과 다른 오버레이 프로세스를 강제 종료하거나 Windows를 자동 재부팅하지 않습니다.
- 데모·화면 캡처·자동 종료 실행은 자동 업데이트를 생략합니다. 필요 시 `--updates-disabled`로 이번 실행의 업데이트만 끌 수 있습니다.
- 0.4.0 이하에는 이 기능이 없으므로 **0.4.1을 처음 설치할 때는 한 번 수동 설치**해야 합니다. 커밋만으로 업데이트가 배포되지 않으며 GitHub 릴리스에 새 설치 파일이 게시되어야 합니다.

## 오버레이 위치와 크기 조절

상태창에서 **레이아웃 편집**을 누르면 각 UI가 독립된 청록색 편집 테두리로 표시됩니다. 테두리 안을 드래그해 이동하고 오른쪽 아래 손잡이로 크기를 조절합니다. **저장 후 잠금**을 누르면 `%LOCALAPPDATA%\AMS2KRLeague\overlay-layout.json`에 현재 게임 해상도 대비 비율로 저장되고 다시 click-through 상태가 됩니다. **기본 위치 복원**은 저장된 배치를 삭제하고 기본 배치를 즉시 적용합니다.

타워는 폭에 맞춰 글자를 조절하고 높이에 따라 표시 행을 즉시 늘립니다(2~64행, 상위 순위 우선/범위 밖 플레이어 마지막 고정). 다른 카드도 가로·세로 크기를 독립적으로 적용하지만 글꼴은 균등 비율로 확대·축소하여 찌그러지지 않습니다. 별도의 글꼴 크기 슬라이더는 없으며 작은 창에서는 글자 크기도 작아집니다. Race Control은 현재 알림을 우선하며 화면용 히스토리 목록은 표시하지 않습니다.

타워 시간은 각 참가자의 AMS2 **최고 랩 기록**입니다. 연습·예선에서 기록이 없으면 출차 후 `아웃랩` → 계측 시작 후 `랩 타임 주행 중` → 유효 기록 인정 후 최고 기록으로 바뀝니다. 이미 최고 기록이 있으면 다음 주행에도 유지합니다. 레이스는 첫 바퀴 `아웃랩`, 두 번째 바퀴 `레이스 중`, 두 번째 바퀴 완료 이후 유효한 AMS2 최고 기록을 표시합니다. `피트`, `완주`, `중도 포기`, `미완주`, `실격`은 별도 상태로 표시합니다. 개인 현재·섹터 타임은 독립 패널에 유지하며, 랩 무효 신호가 오면 현재 타임을 고정하고 무효를 표시합니다. 과거 유효 최고 기록은 무효 처리하지 않습니다.

전후방 패널은 같은 영역의 차량만 비교합니다. 트랙에서는 트랙 진행거리, 피트에서는 차량 방향과 월드 좌표를 이용한 가까운 차량의 직선거리(`~Nm`)를 표시합니다. 시간차는 실제 선택 차량에 대응하는 유효 AMS2 split이 있을 때만 표시하며, 거리를 시간으로 환산하지 않습니다. `랩 N`은 레이스에서만 표시하고 연습·예선에서는 표시하지 않습니다.

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

첫 공개 버전은 `0.1.0`, 현재 개발 버전은 `0.4.1`, 안정 기준선은 `0.2.2`입니다. 자세한 규칙은 [VERSIONING.md](VERSIONING.md)를 참고하십시오.

## 현재 제한사항

- Replay/Spectator에서는 viewed participant가 실제 로컬 플레이어와 다를 수 있어 Player HUD를 숨깁니다.
- AMS2 Shared Memory가 제공하지 않는 구체적인 penalty reason은 추론해서 표시하지 않습니다.
- Shared Memory v14가 제공하지 않는 세션 종료 cooldown 숫자는 추정하지 않습니다.
- Player 활동과 Session Witness는 공식 경기 승인이 아니며, 공식 리그 기록은 서버 관리자 승인 후에만 확정됩니다.
- Shared Memory v14에는 권위 있는 멀티플레이어 여부 필드가 없어 현재 Witness 수집 자격은 관련 세션에서 둘 이상의 참가자가 관측되었는지로 판단합니다. 이 값은 온라인 또는 공식 경기라는 주장으로 사용하지 않습니다.
- 코드 서명 인증서가 없어 Windows SmartScreen 경고가 표시될 수 있습니다.
- 서버의 운영·저장·승인 로직과 Host 경기 결과 수집은 이 사용자용 저장소 범위에 포함하지 않습니다.
- Closed Beta Compact Telemetry는 실제 멀티플레이어·장시간·Incident 검증 중이며, private Driver stream은 authoritative owner attestation이 없어 서버 업로드가 차단됩니다.
- 실제 테스트에서 cadence 손실에 따른 PARTIAL과 세션 종료 경계의 짧은 추가 capture가 관측됐으며 해결됐다고 주장하지 않습니다. 과거 403 격리 항목은 자동으로 재등록/재전송하지 않습니다.
- 게임 부하에서 오버레이 출력 120 FPS 이상을 보장하지 않습니다. 피트 좌표 보조 계산은 근거리 직선 투영이며 피트 곡선 전체의 경로 복원이 아닙니다.

### 페널티 표시

타워 오른쪽 전용 열에 게임이 제공한 드라이브스루·스톱 앤 고·실격 상태를 표시합니다. 완주·피트·최고기록 배지는 함께 유지됩니다. 의무 피트와 수리 요청은 페널티로 취급하지 않습니다. 미지원 값은 `미확인`, 확인된 페널티가 없으면 `—`로 표시합니다. 게임이 제공하지 않는 가산 초나 사유는 추정하지 않습니다. 기본 폭은 520→648px이며 과거 저장된 타워 폭도 비율에 맞춰 한 번 확장하고 헤더 높이를 보정하여 표시 행 수를 유지합니다. 다른 패널의 사용자 배치는 유지합니다.