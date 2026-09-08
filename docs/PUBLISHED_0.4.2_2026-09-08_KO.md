# AMS2KRLeague 0.4.2 공개 게시 확인

- 게시: https://github.com/choi3724/AMS2KRLeague/releases/tag/v0.4.2
- GitHub releases/latest 확인: v0.4.2, draft=false, prerelease=false. 공개 자산 4개 모두 uploaded.
- 릴리스 태그/소스 커밋: 58993062ad839341f57a11423e35a2d6a9e2dcb1.
- 원격 Windows CI 성공: https://github.com/choi3724/AMS2KRLeague/actions/runs/34229797822
- 로컬 Release: 경고 0, 오류 0. Client 111/111, Activity 102/102. 공식 형식의 실제 보관 로그 6/6 확인.
- 게시된 Setup 51,271,873 bytes / SHA-256 5e321cea52071a44b176b062f68d47081fe32cabac9ed12121c38f68dcdeb121.
- 게시된 ZIP 72,362,869 bytes / SHA-256 3efe2797fea9b50b71e37d3844e779127ef570f7cd9ead3104ee797257428085.
- GitHub 자산 크기·digest가 로컬 매니페스트와 일치함을 검사했다.
- 제품 GitHubReleaseClient로 공개 Latest 설치 파일을 실제 다운로드하고 크기·SHA-256 검증 성공: LIVE_UPDATE_VERIFIED version=0.4.2.

## 적용 범위와 제한
- 자동 모드 판정, 싱글·미확인 기록 로컬 보관, 멀티 확인 구간만 전송, 결과/Witness raceMode 및 리플레이 메타데이터·HTTP 헤더 반영.
- 0.4.1 사용자는 자동 업데이트 대상이며 게임 종료 후 설치된다. 0.4.0 이하는 수동 설치가 필요하다.
- 0.4.1→0.4.2 격리 설치·재실행 검증은 실제 AMS2와 사용자 오버레이가 실행 중이라 안전 검사에서 설치 전 중단했다. 이번 버전의 설치 실증은 NOT RUN이다. 사용자 게임과 설치본을 조작하지 않았다.
- 실게임 모드 전환 및 운영 서버의 새 리플레이 헤더 저장·표시도 NOT RUN이다. 로그가 없거나 오래되거나 퇴장 경계가 섞인 기록은 UNKNOWN으로 보류될 수 있다.
- 서버/DB와 기존 원본 데이터 변경 없음. ‘미전송’ 화면의 운영 원인을 해결했다고 주장하지 않는다.

## 증거
- work/validation-0.4.2/build-package.log
- work/validation-0.4.2/publication.log
- work/validation-0.4.2/published-release.json
- work/validation-0.4.2/live-download.log
- work/validation-0.4.2/update-install.log
- 구현 상세와 실사용 제한: docs/UPDATE_0.4.2_2026-09-08_KO.md

REQ-MODE-05의 공개 게시: DONE. 실게임·운영 수신과 이번 버전의 자동 설치 실증 제한은 유지한다.
