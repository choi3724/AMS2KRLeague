# 0.4.1 릴리스 완료 — 2026-09-08

- 사용자 요청에 따라 v0.4.1을 정식 Latest로 게시했다. draft=false, prerelease=false.
- 릴리스: https://github.com/choi3724/AMS2KRLeague/releases/tag/v0.4.1
- 태그 커밋: b734ba61a72718e20c09a50bb7435480197ee92d. 구현 커밋 94b1007에 CI 화면 크기 의존 테스트 수정만 추가했다. 배포 프로그램 소스는 동일하다.
- GitHub CI: https://github.com/choi3724/AMS2KRLeague/actions/runs/34214246176 — success. 로컬 Release 빌드 경고 0/오류 0, 클라이언트 107/107, 활동 기록 102/102 통과.
- 설치판, ZIP, SHA256SUMS, 매니페스트 총 4개 첨부 파일의 GitHub 크기·SHA-256·uploaded 상태를 로컬 파일과 대조하여 모두 확인했다.
- 실제 GitHubReleaseClient로 Latest 0.4.1 설치 파일 다운로드와 해시 검증 통과: 51,261,045 bytes, SHA-256 9b24e8963724fca1357da440cebe86f09ae2afe7b3d91f9ac2b7fa8965f171b5.
- ZIP SHA-256: 185db84c5a211ae3f124d463fd50502394d1e05845375755229244bc73d039e4.
- 증거: work/validation-0.4.1/published-release.json, published-download.log, release-ci-fix.log. 기존 격리 폴더 0.4.0→0.4.1 설치·재실행·사용자 파일 보존 증거는 verified-install-proof.log.
- 실게임 테스트는 실행하지 않았다. 0.4.0 이하 사용자는 최초 0.4.1을 수동 설치해야 하며 이후 버전부터 자동 업데이트된다.
- README와 VERSIONING의 공개 Latest 표기를 갱신했다. 게시 후 문서 커밋은 릴리스 태그 다음에 위치하며 배포 파일 변경은 없다.
