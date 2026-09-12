# 자동 업데이트 첫 화면 안내 수정 — 2026-09-10

## 기준선
- 공개 버전 v0.7.1 / HEAD 85442a7.
- 기존 미커밋 AGENTS.md, PROJECT.md, docs/TASK.md, TASK.md는 그대로 보존.
- 작업 전 scripts/verify.sh: Client 145 passed, 0 failed, 145 total / Activity 111 passed, 0 failed, 111 total.
- 보호 대상: 경기 기록 저장·종료 승인 조건, 설치 파일 SHA-256/크기 검증, 설치 후 재실행, 데이터 수집·기록·전송 주기.

## 요구사항
REQ-UPD-NOTICE-01
- 원인: 실제 첫 화면 ClientStatusWindow의 UpdateText가 기본으로 접힌 ConnectionDetails 안에 있었다. 설치 직전에는 상태 문구 변경 직후 종료 콜백을 호출했다.
- 변경: UpdateText를 첫 화면 제목 아래로 이동. 상세 연결 정보는 기존 접기 영역을 유지.
- 다운로드 완료 후 설치·자동 재실행 안내를 표시하고 취소 가능한 10초 비동기 대기를 추가.
- 최종 종료 직전 TryReserveUpdateExit 확인은 유지하므로 안내 중 새 기록 수집이 시작되면 종료를 보류한다.
- 수정 파일: ClientStatusWindow.xaml, GitHubAutoUpdater.cs, OverlayGalleryTests.cs.
- WPF 화면 회귀: 연결 상세를 접은 상태에서 확인/다운로드/기록 저장 대기/설치 안내 문구 갱신 및 화면 안 배치를 검사.
- 860×660, 1120×900 렌더링 이미지에서 문구와 기존 선택 화면을 확인.
- 증거: harness/reports/update-notice-after/after/gallery-main-860x660.png 및 gallery-main-1120x900.png.
- 변경 전 화면 캡처는 별도로 생성하지 않았다. 변경 전 근거는 HEAD XAML과 기준선 테스트 로그다.

## 요청하지 않은 변경
- 0건. 버전 변경·커밋·태그·푸시·릴리스·실제 설치는 수행하지 않음.

## 최종 게이트
- scripts/verify.sh 종료 코드 0. Release build 경고 0 / 오류 0.
- RESULT: 145 passed, 0 failed, 145 total
- RESULT: 111 passed, 0 failed, 111 total (2953 ms)
- 이번 수정 파일 diff --check 통과. 기존 규칙 문서의 줄 끝 공백은 수정하지 않음.
- 코드·WPF 회귀 PASS, 실제 자동 설치 전 과정 검증 NOT RUN.
- 결과 로그: harness/reports/update-notice-before.log, harness/reports/update-notice-after.log.
- 실제 GitHub 새 버전 다운로드부터 프로그램 교체·재실행까지의 설치 검증은 실행하지 않았다.
