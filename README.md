# AMS2 KR League Player Overlay

Automobilista 2의 Shared Memory v14를 읽기 전용으로 사용하는 한국어 Player Overlay입니다.

현재 버전: **0.1.0**

## 주요 기능

- League Classification 기준 Timing Tower
- Safety Car를 순위와 참가자 수에서 제외
- 현재 플레이어 강조
- 물리적 트랙 진행거리 기준 앞차·뒤차 표시
- 랩타임, 섹터, 시간/랩 기반 세션 정보
- 황색기, 적색기, 체커드 등 Race Control 상태
- 순위 변화, Personal Best, Fastest Lap, Pit 등 동적 이벤트
- 3440×1440을 포함한 다중 해상도 대응
- 게임 창에 입력을 가로채지 않는 click-through overlay

## 실행 방법

1. [Releases](https://github.com/choi3724/AMS2KRLeague/releases)에서 최신 `win-x64` ZIP을 받습니다.
2. ZIP을 원하는 폴더에 압축 해제합니다.
3. AMS2를 Borderless Windowed 또는 Windowed 모드로 실행합니다.
4. AMS2의 `Options → System → Shared Memory`에서 `Project CARS 2` 방식을 선택합니다.
5. `AMS2LeagueClient.exe`를 실행합니다.

게임이 실행되지 않았거나 Shared Memory를 사용할 수 없으면 오버레이는 대기 상태로 유지됩니다. 프로그램은 게임 설정, 실행 파일, 저장 파일을 자동 변경하지 않습니다.

## 안전 경계

- `$pcars2$` Shared Memory mapping을 읽기 권한으로만 엽니다.
- DLL injection, DirectX hook, 프로세스 메모리 쓰기, 패킷 가로채기를 사용하지 않습니다.
- AMS2 설치 파일, 설정 파일, 레지스트리, 방화벽을 수정하지 않습니다.
- 웹서비스, Host Recorder, 서버 credential은 이 저장소와 릴리스에 포함하지 않습니다.

## 소스 빌드

요구 사항:

- Windows x64
- .NET 8 SDK

```powershell
dotnet build .\AMS2KRLeague.sln -c Release
```

사용자 배포 ZIP 생성:

```powershell
.\scripts\build-release.ps1 -Version 0.1.0
```

생성물은 `artifacts/`에 저장되며 Git에는 포함되지 않습니다.

## 버전 정책

첫 공개 버전은 `0.1.0`입니다. 같은 0.1 개발선의 수정판은 `0.1.1`, `0.1.2`, `0.1.3` 순서로 올립니다. 자세한 규칙은 [VERSIONING.md](VERSIONING.md)를 참고하십시오.

## 현재 제한사항

- Replay/Spectator에서는 viewed participant가 실제 로컬 플레이어와 다를 수 있어 Player HUD를 숨깁니다.
- AMS2 Shared Memory가 제공하지 않는 구체적인 penalty reason은 추론해서 표시하지 않습니다.
- 공식 경기 결과 기록 및 웹 연동 기능은 이 사용자용 저장소 범위에 포함하지 않습니다.
