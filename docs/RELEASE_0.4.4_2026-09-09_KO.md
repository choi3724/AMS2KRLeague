# AMS2KRLeague 0.4.4 릴리스 검증 — 2026-09-09

## 기준선과 승인
- 작업 폴더 E:\AMS2 KRLEAGUE\AMS2KRLeague, HEAD/origin/main 53b23a5, 공개 Latest v0.4.3. 원격 fetch 후 동일 상태와 v0.4.4 태그 부재 확인.
- 앞선 편집 미리보기 작업 트리만 존재했으며 사용자가 0.4.4 릴리스를 요청했다. commit/tag/push/정식 Latest 게시까지 승인된 범위다.
- 구현 전 기준선: Client 112/112, Activity 102/102. 구현 후 scripts/verify.sh: 경고/오류 0, Client 113/113, Activity 102/102.

## 포함 변경
- 빈 이벤트·레이스 컨트롤에 편집 전용 예시 표시.
- 패널 내부 입력 면을 적용하여 중앙·빈 여백에서도 선택·이동 가능. 크기 조절 손잡이 유지.
- 실제 이벤트 수신 및 편집 종료 시 실제 내용 복원. 종료 후 일반 플레이 클릭 통과 유지.
- 가상 모델과 원본 기록·업로드·레이아웃 저장 분리.
- 0.4.3의 타워 최고속 랩·자동 높이·게임 실행 중 업데이트 유지.
- 상세 구현과 WPF 증거: docs/EDIT_PREVIEW_2026-09-08_KO.md 및 work/validation-edit-preview/final/.

## 요구사항과 검증
- REQ-EDIT-01/02: DONE. 이벤트 소멸 뒤 편집 진입, 반복 갱신, 실제 이벤트 수신, 저장·취소 복원 통과.
- 패널 3개 × 3지점에서 alpha=24, 이동 커서, 클릭 통과 해제 및 입력 대상 확인. 이벤트 크기 1.25×1.5배 조절 및 미리보기 렌더 확인.
- REQ-EDIT-03 릴리스: DONE. 0.4.4 패키지·업데이트·CI·Latest 게시·공개 다운로드 검증 통과.
- 0.4.4 Release 빌드 경고 0/오류 0, Client 113/113·Activity 102/102 통과. work/validation-0.4.4/build-package.log.

## 보호 대상과 제한
- 실제 사용자 설치본·설정·기록·게임을 변경하지 않고, 테스트는 격리 경로에서 수행한다. 서버/DB 변경 없음.
- 실제 게임 위에서 물리 마우스 드래그는 NOT RUN. WPF 창·픽셀·입력 대상·네이티브 스타일로 확인했다.
- 0.4.3 사용자는 게임 종료 없는 자동 업데이트 대상이다. 0.4.1~0.4.2는 구버전 게임 종료 대기를 따르며 0.4.0 이하는 수동 설치가 필요하다.
- 설치·재실행 중 표시와 기록 수집에 공백이 있다. 기존 모드 로그 부족·세션 경계 UNKNOWN 보류 및 서버 수신 저장 미검증 범위는 유지한다.
- 요청하지 않은 기능·외부 의존성 추가 0건.

## 패키지와 설치 검증
- 공개 폴더·ZIP 각 466개 파일, 금지 파일 0. 설치 파일 검사 PASS.
- Setup 51,267,710 bytes / SHA-256 c9fc29c14c8abb88094c7c139c758dbc4bd31ff569fb5ef4738ac43122bbecbc.
- ZIP 72,363,177 bytes / SHA-256 ac544e52c5f9196a32bff44294e96fc3d455ff0647e0c9c6b94388bc17313bc8.
- 격리 업데이트 PASS: 0.4.3→0.4.4, helperExit=0, portablePreserved=true, userFilePreserved=true, 재실행 캡처 18개, 예외 로그 없음.
- 증거: work/validation-0.4.4/update-install.log 및 work/update-install-proof-ea69f2a080be49cb8dbff5404dba4cae/summary.json.
- 이번 검증 당시 게임은 실행 중이 아니었다. 게임을 켜둔 설치는 0.4.3에서 검증했고 설치 도구의 동작 코드는 이번 버전에서 변경하지 않았다.

## 최종 공개 확인
- 정식 Latest: https://github.com/choi3724/AMS2KRLeague/releases/tag/v0.4.4
- GitHub releases/latest: v0.4.4, draft=false, prerelease=false. 자산 4개 모두 uploaded.
- 릴리스 태그/소스 커밋: 577de6a4ab1f82cb71a67341fab8fd3fd15f327e.
- Windows CI 성공: https://github.com/choi3724/AMS2KRLeague/actions/runs/34248945244
- 공개 Setup·ZIP의 크기와 GitHub digest가 로컬 매니페스트와 일치함을 확인했다.
- 제품 GitHubReleaseClient 실제 다운로드 성공: LIVE_UPDATE_VERIFIED version=0.4.4 bytes=51267710 sha256=c9fc29c14c8abb88094c7c139c758dbc4bd31ff569fb5ef4738ac43122bbecbc.
- 증거: work/validation-0.4.4/publication.log, published-release.json, live-download.log.
- REQ-EDIT-01~03 DONE. 작업 트리 구현을 정식 공개했으며 새 태그는 CI 통과 소스 커밋에 유지한다.
