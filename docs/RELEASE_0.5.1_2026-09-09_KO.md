# 0.5.1 릴리즈 검증 기록

## 기준선
- 공개 v0.4.5, HEAD/origin/main 9883d96. 사용자 요청으로 0.5.1 커밋·태그·Latest 게시.
- 작업 시작 시 HostRecorderEngine, SessionWitnessCaptureEngine, SessionWitnessTests에 있던 변경을 검토하고 보존했다.
- 결과 원본, 참가자 상태, 출발 그리드 및 기존 레이스 종료 후 전송 동작을 보호했다.

## 요구사항
- REQ-SEND-02 DONE: 비활성 FIN/RET/DNF/DSQ 행의 결과·증거·랩 값·이벤트를 보존한다. 빈 슬롯/종료 근거가 없는 비활성 행을 결과로 만들지 않으며 출발 그리드는 활성 참가자로 제한한다.
- 세이프티카 원본을 유지하고 실제 드라이버만 기준으로 전체/종료 구간 관측 범위를 판정한다.
- REQ-RELEASE-051: 0.5.1 버전 파일 및 배포 자료를 준비했다. 공개 릴리즈 설명과 CHANGELOG는 0.4.5 기능을 포함한 기능 변경사항만 작성했다.

## 검증
- scripts/verify.ps1(verify.sh의 동일 진입점): Release 경고 0/오류 0, Client 117/117, Activity 107/107.
- 공식 build-release.ps1: 동일한 두 테스트 재검증 후 self-contained win-x64 패키지 생성.
- 새 회귀 검사: 세이프티카가 전체/종료 관측 분류를 막지 않음, 비활성 종료 참가자 원본·이벤트 보존, 미확인 행·출발 그리드 오인 방지.
- WPF 화면/프레임 자료 128개: work/validation-0.5.1/after. 텔레메트리 A/B/C/H 및 곡선 렌더 확인.
- 증거 로그: work/validation-0.5.1/build.log, client.log, activity.log 및 work/release-0.5.1-build.log.

## 요청하지 않은 변경
- 신규 기능 추가 0건. 서버 변경 없음.

## 게시
- v0.5.1 정식 Latest, 설치 파일·ZIP·SHA256SUMS·release-manifest 4개 자산.
- 이 문서는 게시 직전의 검증 기록이며 실제 게시 상태는 GitHub Release에서 확인한다.

- PUBLIC_PACKAGE_AUDIT: publish/ZIP/installer PASS, forbidden=0.
- Real installer and WPF restart PASS: work/restart-real-current-454e85b170c1488eb5b184cc5ca9e07b. ProductVersion=0.5.1, restarted=true, visible status window; installed DLL SHA256 matches publish.
