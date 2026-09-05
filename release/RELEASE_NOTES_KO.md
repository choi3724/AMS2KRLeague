# AMS2 League Overlay 0.3.0 — F1 중계 스타일 오버레이

> GitHub 배포 정책에 따라 `Latest Release`로 게시됩니다. 안정 기준선 태그 `v0.2.2`는 그대로 유지됩니다.

Timing Tower와 각 오버레이 카드에 F1 TV 중계 그래픽 스타일의 전환 애니메이션을 넣고, 오버레이 화면별 켜기/끄기를 상태창에서 언제든 쓸 수 있게 했습니다. 서버 2D 리플레이를 위해 Compact 리플레이의 월드 좌표 전송 주기를 5초에서 0.5초로 올렸습니다. 서버 프로토콜/스키마와 Cafe24 API/DB 계약은 변경하지 않았습니다.

## 주요 변경

### 오버레이 애니메이션

- 순위 타워: 세션 시작이나 첫 표시 때 행이 왼쪽에서 순서대로 들어오는 타워 빌드, 참가자 진입 시 단독 슬라이드 인
- 추월 시 민트, 피추월 시 레드로 행이 플래시되고 순위 숫자가 방향에 맞춰 롤
- 세션 최속 랩(`BEST`) 시 보라색 좌→우 스윕, PIT/DT/SG 등 상태 배지 팝
- 전후방 패널: 앞차/뒷차 교체 시 각 방향에서 슬라이드 인, 거리 유불리 색 반전 시 팝
- 세션 카드의 랩 카운터·순위 롤, 현재/섹터 타임 패널의 랩 완료·개인 최고·섹터 완료 팝
- 이벤트 카드와 Race Control 배너의 액센트 바 성장, 플래그 색 스윕, 슬라이드 인/아웃(종료 애니메이션이 끝날 때까지 창 유지)
- 행 전체 opacity는 애니메이션하지 않으며 주행 중 참가자는 절대 회색 처리하지 않습니다.

### 오버레이 켜기/끄기

- 상태창의 **표시할 오버레이** 체크박스가 레이아웃 편집 모드와 무관하게 항상 활성화됩니다.
- 변경 즉시 `%LOCALAPPDATA%\AMS2KRLeague\overlay-layout.json`에 저장되고 다음 실행 때 복원됩니다.
- **모두 켜기**/**모두 끄기** 버튼을 추가했습니다. 전역 단축키는 "입력 가로채기 없음" 정책에 따라 넣지 않았습니다.

### Compact 리플레이 전송 주기

- 리플레이 downsampling 상수 4종(progress/world/extension/battle)을 `TelemetryArchiveOptions` 옵션으로 분리했습니다.
- world 좌표 기본 주기를 5,000 ms에서 **500 ms**로 변경했습니다. 5초 주기에서는 차량이 샘플 사이에 중앙값 235 m를 이동해 2D 트랙 리플레이를 만들 수 없었습니다.
- 실측 결과 리플레이 업로드는 60분/32대 fixture 기준 302,317 B에서 860,539 B로(+184.6 %), 실제 리그 세션(연습+예선+레이스) 기준 약 +53 % 늘어납니다. 대·분당 약 470 B입니다.
- P024 512 KiB fixture 제품 목표는 초과하고 1 MiB 한계 안에 있습니다. Cafe24 저장 영향은 세션당 0.2 MB 수준입니다.

## 사전 검증 결과

- Release build 경고 0 / 오류 0
- AMS2LeagueClient.Tests 64/64, AMS2LeagueActivity.Tests 97/97
- `--demo-events` 데모 실행 예외 0
- 서버 저장 원본 72 chunk 재분석: 리플레이 스트림 cadence miss 0, 업로드/저장 손실 0
- cadence별 전송량은 추정이 아니라 제품 변환 코드로 fixture를 재인코딩해 측정(`docs/REPLAY_TRANSMISSION_SUFFICIENCY_2026-09-05_KO.md`)

## 알려진 제한

- 새 애니메이션의 실게임 시각 확인은 아직 사용자 검수 전입니다. 세션 전환 첫 프레임에 여러 행이 동시에 플래시할 수 있습니다.
- Compact capture completeness는 Driver/Incident cadence miss 때문에 여전히 `PARTIAL`일 수 있습니다.
- private Driver stream은 authoritative owner attestation이 없어 서버 업로드가 차단됩니다.
- `SESSION_METADATA`는 저주기 legacy JSON/gzip 호환 경로를 유지합니다.
- 코드 서명 인증서가 없어 Windows SmartScreen 경고가 표시될 수 있습니다.

## 서버 요구사항

- Cafe24 Application `1.6.0`, DB schema `15`를 그대로 사용합니다.
- Compact 원본은 `.a2ct.gz`로 저장하고 MariaDB에는 인덱스와 저장 키만 보존합니다.
- 기존 API/Portal/0.2.2 호환 endpoint는 유지됩니다.

## 설치

- Installer: `AMS2-League-Overlay-0.3.0-Setup.exe`
- Portable: `AMS2-League-Overlay-0.3.0-win-x64.zip`
- 해시: `SHA256SUMS-0.3.0.txt`

설치 전 SHA-256을 확인하십시오. 문제 발생 시 안정 기준선 `v0.2.2`로 되돌릴 수 있습니다.
