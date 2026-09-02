# AMS2 League Overlay 0.2.3-beta.1 — Closed Beta

> 이 릴리스는 **Pre-release**입니다. 안정 기준선 `v0.2.2`는 그대로 유지됩니다.

P024 Compact Telemetry 후보를 실제 멀티플레이어, 장시간 주행, 사고(Incident) 상황에서 검증하기 위한 클로즈 베타입니다. 일반 사용자용 안정판 승격이 아니라, 실제 환경의 수집 완전성·손실·서버 저장 계약을 확인하기 위한 제한 배포입니다.

## 주요 변경

- 고주기 대형 JSON 대신 고정 스키마 little-endian `A2CT V1` binary와 gzip 전송을 사용합니다.
- Replay, Race Story, Track Geometry, Incident 및 Driver 계열 스트림을 immutable chunk로 로컬 보존합니다.
- 종료 시 `LOSS_LEDGER_V1`과 `ATTEMPT_FINALIZE_V1`을 기록해 수집 누락과 서버 ACK 상태를 추적합니다.
- Shared Memory 유효 필드 `161/161`의 계보와 ordinal, scale, privacy 계약을 문서화했습니다.
- public Compact 데이터만 서버 전송 대상입니다. private Driver telemetry는 소유권을 권위 있게 증명할 수 있을 때까지 로컬에만 보존합니다.
- 기존 0.2.2 오버레이, 익명 설치 등록, 활동/Witness 업로드 계약은 유지합니다.

## 사전 검증 결과

- 60분·32대 합성 fixture: raw `2,781,797 B`, gzip wire `465,279 B`
- persisted Compact만 사용한 오프라인 재처리: `11/11 PASS`
- 로컬 PHP Server replay: 전체 `78/78` frame 저장·복원
- 실제 AMS2 v9 종료 경로: `LOSS_LEDGER_V1 → ATTEMPT_FINALIZE_V1`, accepted/durable `4,783/4,783`, completeness `COMPLETE`

## 베타 서버 요구사항

- Cafe24 Application `1.6.0`, DB schema `15`
- Compact 원본은 검증된 `.a2ct.gz` 파일로 그대로 저장하고 MariaDB에는 검색용 인덱스와 저장 키만 보존합니다.
- 기존 API/Portal/0.2.2 호환 endpoint는 유지됩니다.

## 알려진 제한

- 실제 멀티플레이어·장시간·다양한 Incident 검증은 이 베타의 목적이며 아직 안정판 승격 기준을 충족하지 않았습니다.
- clean lap, 실제 다중 사용자 동시 witness, 장시간 디스크/CPU/FPS 비교 검증이 추가로 필요합니다.
- `SESSION_METADATA`는 저주기 legacy JSON/gzip 호환 경로를 유지합니다.
- 코드 서명 인증서가 없어 Windows SmartScreen 경고가 표시될 수 있습니다.

## 설치

- Installer: `AMS2-League-Overlay-0.2.3-beta.1-Setup.exe`
- Portable: `AMS2-League-Overlay-0.2.3-beta.1-win-x64.zip`
- 해시: `SHA256SUMS-0.2.3-beta.1.txt`

설치 전 SHA-256을 확인하십시오. 문제 발생 시 안정 기준선 `v0.2.2`로 되돌릴 수 있습니다.
