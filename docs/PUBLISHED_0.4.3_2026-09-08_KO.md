# AMS2KRLeague 0.4.3 공개 게시 확인

- 정식 Latest: https://github.com/choi3724/AMS2KRLeague/releases/tag/v0.4.3
- releases/latest API: v0.4.3, draft=false, prerelease=false. 자산 4개 모두 uploaded.
- 소스/태그 커밋: 4cd987dc9bcf5910cbb25818672c06593a7d99ed.
- GitHub Windows CI 성공: https://github.com/choi3724/AMS2KRLeague/actions/runs/34232009505
- 로컬 Release 경고 0/오류 0, Client 112/112 및 Activity 102/102. WPF 인원 변화·저장 배치 보존 검증 통과.
- 실제 게임 실행 중 격리 설치 PASS: 0.4.2→0.4.3, 게임 프로세스 유지, 파일·포터블 상태 유지, 새 실행본 캡처 18개.
- Setup: 51,270,171 bytes / SHA-256 03a0783af7a65eb1b174f06018e0ad29af36afe8d9e105db2158e5ef9bdf207d.
- ZIP: 72,362,913 bytes / SHA-256 fa0f9d9a4059c4ba8385a666ad2058ec9da5e4e4fa414ab0c854d19348241462.
- 공개 자산 크기와 GitHub digest가 로컬 매니페스트와 일치했다.
- 제품 GitHubReleaseClient로 공개 Latest Setup을 다운로드하고 크기·SHA-256 검증 성공: LIVE_UPDATE_VERIFIED version=0.4.3.

## 적용과 호환
- 타워 최고속 랩 표기, 참가자 수에 따른 자동 높이, 게임 실행 중 오버레이 업데이트 반영.
- 0.4.3부터 게임 종료를 기다리지 않고 정상 저장·오버레이 종료·설치·백그라운드 재실행한다. 설치 중 표시·기록 수집에는 공백이 있다.
- 이미 실행 중인 0.4.1~0.4.2는 최초 0.4.3 자동 설치 시 기존 버전의 게임 종료 대기를 따른다. 이번 격리 검증은 새 설치 도구로 수행했으며 구버전 대기 루프가 변경된 것으로 표현하지 않는다.
- 사용자 실제 설치본과 게임을 조작하지 않았다. 서버/DB 변경 없음. 0.4.2의 로그 기반 모드 판정과 수신 서버 검증 제한 유지.

## 증거
- work/validation-0.4.3/build-package.log
- work/validation-0.4.3/verify-2.log
- work/validation-0.4.3/update-install.log
- work/update-install-proof-719dc29e94274674bbb4c79c836c59f8/summary.json
- work/validation-0.4.3/publication.log
- work/validation-0.4.3/published-release.json
- work/validation-0.4.3/live-download.log
- docs/UPDATE_0.4.3_2026-09-08_KO.md

REQ-043-01~04: DONE. 문서 후속 커밋과 별개로 v0.4.3 태그는 CI 통과 소스 커밋에 유지한다.
