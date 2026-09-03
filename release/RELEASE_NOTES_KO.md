# AMS2 League Overlay 0.2.3-beta.3 — Closed Beta Hotfix

> 이 버전은 기능상 **Closed Beta**이지만 GitHub 배포 정책에 따라 `Latest Release`로 게시됩니다. 안정 기준선 태그 `v0.2.2`는 그대로 유지됩니다.

P024 Compact Telemetry 클로즈 베타에서 확인된 Overlay 스타일, 참가자 상태와 Relative Gap 표시를 수정한 후보입니다. 일반 사용자용 안정판 승격이 아니며, Compact binary/schema와 Cafe24 DB/API 계약은 변경하지 않았습니다.

## 주요 변경

- GT3 등 주요 차량 클래스에 AMS2 HUD 계열의 고정 배지 색상과 fallback 색상을 적용했습니다.
- 클래스·타임 글꼴을 키우고 행 높이와 열 폭을 함께 조정해 좁은 폭에서도 타임이 잘리지 않게 했습니다.
- 주행 중 참가자를 회색으로 만들던 행 전체 opacity 애니메이션을 제거했습니다. `RET`/`DNF`/`DSQ`/연결 끊김만 비활성 스타일을 사용합니다.
- 앞차·뒷차 거리 색상을 RED=사용자에게 불리, BLUE=유리로 통일하고 작은 Shared Memory 진동에는 2 m hysteresis를 적용했습니다.
- Lap Gap은 Track Length가 있는 누적 Race Progress가 실제 한 바퀴 이상 벌어지고 동일 결과가 연속 2회 확인된 경우에만 `LAP N`으로 표시합니다.
- beta.2의 대기 화면, Position Change, Practice/Qualifying Best Lap과 참가자별 Race Finish Timing 수정은 유지됩니다.
- P024 binary/schema 및 Cafe24 DB/API 계약은 beta.1과 동일합니다.

## 사전 검증 결과

- 실제 멀티플레이 2회 public Compact: Client wire `562,029 B`, Cafe24 raw `562,755 B`
- Client public chunk `72/72`와 Server GET 원본의 content hash 및 A2CT bytes 일치
- Server decode `72/72`, MariaDB index `72/72 STORED`, DB binary payload `0 B`
- Server 저장 원본만 사용한 참가자 14명, Lap 3, Position History, Race Story, 2D movement 재처리 PASS
- 실제 cadence loss ledger: session 1 `7,909`, session 2 `6,014`; 전송 손실은 없지만 capture completeness는 `PARTIAL`

## 베타 서버 요구사항

- Cafe24 Application `1.6.0`, DB schema `15`를 그대로 사용합니다.
- Compact 원본은 검증된 `.a2ct.gz` 파일로 그대로 저장하고 MariaDB에는 검색용 인덱스와 저장 키만 보존합니다.
- 기존 API/Portal/0.2.2 호환 endpoint는 유지됩니다.

## 알려진 제한

- 이번 두 세션은 업로드·저장 무결성은 통과했지만 Driver/Incident cadence miss가 기록되어 completeness가 `PARTIAL`입니다.
- 실제 다중 사용자 동시 witness와 cadence loss 원인·허용 기준 검증이 추가로 필요합니다.
- `SESSION_METADATA`는 저주기 legacy JSON/gzip 호환 경로를 유지합니다.
- 코드 서명 인증서가 없어 Windows SmartScreen 경고가 표시될 수 있습니다.

## 설치

- Installer: `AMS2-League-Overlay-0.2.3-beta.3-Setup.exe`
- Portable: `AMS2-League-Overlay-0.2.3-beta.3-win-x64.zip`
- 해시: `SHA256SUMS-0.2.3-beta.3.txt`

설치 전 SHA-256을 확인하십시오. 문제 발생 시 안정 기준선 `v0.2.2`로 되돌릴 수 있습니다.
