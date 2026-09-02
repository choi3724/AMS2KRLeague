# v0.2.3-beta.1 Closed Beta Release and Cafe24 Deployment Report

작성일: 2026-09-02 KST

## 범위와 판정

- Client: `0.2.3-beta.1` (`FileVersion 0.2.3.0`)
- 안정 기준선: `v0.2.2` 유지
- Server: Application `1.6.0`, schema `15`, Cafe24 release `20260902-001`
- 안정판 P024 판정: `YELLOW / HOLD` 유지
- Closed Beta: 명시적 운영자 승인에 따른 `GO`; 이후 통합 배포 정책에 따라 GitHub `Latest Release`로 전환

## 릴리즈 직전 전체 검증

- Client Release build: 경고 0, 오류 0
- Client 회귀: `38/38 PASS`
- Activity/Future/Compact 회귀: `96/96 PASS`
- Server API/Portal 회귀: `235/235 PASS`
- P024 shipping proof replay: `78/78 PASS`
- 실제 AMS2 v6 replay: `8/8` Compact + legacy metadata PASS
- 실제 AMS2 v9 finalize replay: public Compact `6/6`, integrity `2/2`, byte-exact detail PASS
- 공개 Portable/Installer 감사: 금지 파일·개인 데이터 `0`

샌드박스 컨텍스트의 첫 실행에서는 Windows 사용자 프로필이 로드되지 않아 DPAPI 관련 6개 테스트만 환경 오류로 실패했다. 릴리즈는 그 상태에서 중단했으며, 실제 로그인 사용자 DPAPI 컨텍스트에서 전체 게이트를 처음부터 다시 실행해 `38/38`을 확인한 뒤에만 패키지를 생성했다.

## Cafe24 백업과 migration

배포 전 활성 Application `1.4.2`, schema `13`을 확인했다. 새 코드는 기존 활성 릴리스를 덮어쓰지 않고 `/www/ams2/releases/20260902-001`에 먼저 staging했다.

- preflight: PHP 8.4, PDO DB, authenticated encryption, 쓰기 권한 모두 PASS
- migration dry-run: 적용 13개, pending은 예상된 `014_future_telemetry_archive`, `015_compact_telemetry_protocol`만 존재
- DB 논리 백업: 38 tables, 2,740 rows, gzip 623,812 B
- DB backup logical SHA-256: `598e36c578cbc63a4d42351a0505775a849beee78e3330b136db86a5884f1984`
- 운영 파일 백업: encrypted config, current release pointer, root API, root portal
- migration 014/015: PASS
- schema 검증: migrations/table/Compact index columns 모두 PASS
- rollback: schema 13→15 실패 시 telemetry tables 제거, 설치 scope 복원, migration row 제거 경로를 사전 배치

## 실제 Compact E2E

후보 API에서는 실제 AMS2 v9 Participant Replay frame을 사용했다.

- POST: HTTP 201
- GET detail: HTTP 200
- raw A2CT: 157,141 B
- wire gzip: 2,887 B
- payload SHA-256: `6a17e266f19f784a7a151afc6d71f951b7d78463ab4878e45614a120829d019f`
- GET byte/hash: exact match
- MariaDB `payload_gzip`: 0 B
- private filesystem canonical `.a2ct.gz`: 2,887 B
- filesystem archive hash: PASS

활성화 후에는 빌드된 `AMS2LeagueClient, Version=0.2.3.0`의 실제 `Cafe24ActivityUploadTransport`와 DPAPI 익명 등록 경로로 실제 AMS2 v9 `ATTEMPT_FINALIZE_V1` frame을 다시 전송했다.

- Client declared version: `0.2.3-beta.1`
- Server health: Application `1.6.0`, schema `15`
- POST: HTTP 201 `STORED`
- GET: HTTP 200
- raw/wire: 97 B / 99 B
- payload SHA-256: `607e7f116dc1a9b10f5fc0ff82db996ca542c326a86706d91cf8eec2d5e3fd6c`
- GET byte/hash: exact match
- archive encoding: `compact-gzip`
- expanded JSON archive: 없음

Portal/API 호환성 확인 결과 root, health, bootstrap, standings는 200, 인증 없는 presence/recorder는 401, private config는 403, 삭제된 installer는 404였다. 기존 0.2.2 activity/witness 계약도 전체 Server 회귀에 포함해 통과했다.

## 배포 자산 SHA-256

- Portable ZIP: `1c749e5184fee2e3ab4ad3b2eeefe46f5f75c751e0d0398e315535c4cd846e5a`
- Installer: `9b16f64174e5fb212fe9e2674b6211a45de2028e3890712fc21a3857001af478`
- Cafe24 server package: `297a2c3eb035d4f8786f18c5f9b96b023e9a77e9d9982efcc0e25defe144b31a`

Commit SHA, tag와 GitHub Release URL은 외부 게시 완료 후 최종 보고에 기록한다.
