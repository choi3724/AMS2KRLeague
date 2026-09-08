# AMS2KRLeague 0.4.2 구현·검증 보고서

작성일: 2026-09-08. 작업 폴더: E:\AMS2 KRLEAGUE\AMS2KRLeague.

## 기준선
- 시작 HEAD/main/origin/main: db4a29a. 공개 Latest: v0.4.1. 원격 최신 소스·태그를 다시 fetch해 충돌 없음 확인.
- 변경 전 scripts/verify.sh: Release 경고/오류 0, Client 107/107, Activity 102/102.
- 보호 대상: 오버레이·페널티, 로컬 불변 원본, Compact/GZIP 바이트와 해시, 인증·재시도, 설정과 기존 서버 데이터.
- 친구 PC의 실제 전송 상태는 확인되지 않았다. 화면의 ‘서킷 정보 미전송·0명’만으로 네트워크 전송 실패를 확정하지 않는다. 원본 증거 수와 별도 메타데이터 조회는 구분해야 한다.

## 요구사항
REQ-MODE-01 [PARTIAL: 구현 및 실제 보관 로그 검증 완료, 실게임 전환 미실행]
- 공식 SharedMemory.h v14에 권위 있는 multiplayer 필드가 없으므로 공식 online.log의 로컬 생성·접속·퇴장 사건으로 판정한다. 참가자 수나 AI 이름은 모드 근거가 아니다. mSessionIsPrivate도 멀티 방 여부가 아니다.
- 현재 AMS2 프로세스 시작과 로그 헤더를 대조한다. 접속 시도는 UNKNOWN, 성공은 MULTIPLAYER, 로컬 퇴장은 SINGLE_PLAYER. 다른 참가자의 퇴장과 채팅 속 문자열은 무시한다.
- 상태창과 대기 오버레이에 자동 적용한다. 수동 선택은 제거했다.
- 사용자 보관 실제 로그 6/6에서 파싱 성공, 합계 접속 6회/퇴장 6회. 원본은 읽기만 했으며 Steam ID/IP·원문을 저장소나 전송 데이터에 포함하지 않았다.
- 공식 근거: https://forum.reizastudios.com/threads/multiplayer-logs-reports-thread.32731/ 및 설치된 Support/SharedMemory/AMS2_SharedMemoryExampleApp/SharedMemory.h.

REQ-MODE-02 [DONE: 로컬 업로드 경로 검증]
- Activity/Witness와 리플레이 대기열 모두 실제 캡처 구간이 멀티인지 전송 전에 검사한다. 싱글·미확인·시각 누락·세션 경계 혼합은 로컬 보류한다.
- 보류 21개 뒤의 멀티 1개가 batch=1에서도 선택되는 테스트 통과. 실제 백그라운드 업로드 작업자에서 멀티 리플레이 전송 및 사이드카 저장 확인.
- 기존 UNKNOWN 결과는 원본을 멀티로 재작성하지 않는다. 기존 403 격리 정책과 private Driver 소유권 제한을 유지한다.

REQ-MODE-03 [PARTIAL: 송신 구현 완료, 운영 수신 저장 미검증]
- 기존 Player Activity 결과 및 Witness 최상위 raceMode: MULTIPLAYER / SINGLE_PLAYER / UNKNOWN.
- 리플레이: TelemetryPendingUploadMetadata.raceMode 및 HTTP X-AMS2-Race-Mode. 원본 Compact/GZIP 본문과 해시 불변을 테스트했다.
- 허용된 실전 전송은 MULTIPLAYER만이다. 다른 두 값은 로컬 결과 및 전송 계약 테스트에서 확인한다.
- 서버가 새 헤더를 세 값으로 검증해 raceMode에 저장하는 매핑은 별도 서버 작업이다. 이번 클라이언트 릴리스가 운영 서버 저장·화면 반영까지 완료했다고 주장하지 않는다.

REQ-MODE-04 [DONE: 자동 상태 화면]
- 수동 ComboBox 제거, 자동 모드 및 로컬 보관/전송 보류 한국어 문구 적용.
- WPF 상태창 캡처에서 겹침·잘림 없음 확인. 기존 대기 오버레이 종류와 레이아웃 유지.

REQ-MODE-05 [진행: 패키지 및 공개 게시]
- 사용자 요청으로 0.4.2 commit/tag/push/정식 Latest 게시가 승인되었다. 설치 파일과 ZIP 생성 및 공개 패키지 검사 통과. GitHub CI·공개 다운로드 결과는 게시 보고서에 기록한다.
- 0.4.1→0.4.2 격리 설치 검증은 실행 중인 실제 AMS2/오버레이를 감지하여 설치 전 중단했다. 사용 중인 프로세스에 손대지 않았다. 이번 버전의 설치·재실행 실증은 NOT RUN이며 이전 0.4.0→0.4.1 검증과 구분한다.

## 검증 증거
- work/validation-auto-session/baseline/: 변경 전 게이트.
- work/validation-auto-session/verified.log 및 verified/: scripts/verify.sh 통과, Client 111/111, Activity 102/102, Release 경고/오류 0.
- work/validation-auto-session/real-logs-final.log: 실제 로그 6/6.
- work/validation-auto-session/second-gate/after/status-automatic-mode.png: WPF 자동 모드 표시 검증.
- work/validation-0.4.2/build-package.log: 버전 갱신 후 Release 빌드·두 스위트와 패키지 검사.

## 제한과 발견했지만 고치지 않은 문제
- 멀티 로그가 15초 이상 갱신되지 않으면 현재 모드는 UNKNOWN이다. 전송은 마지막 실제 로그 시각까지만 허용하고 캡처 종료 후 10초를 기다린다. 새로운 로그가 들어오면 큐를 재검사한다.
- 혼자 연 멀티 방처럼 로그가 조용한 경우 전송이 보류될 수 있다. 퇴장 경계가 결과나 마지막 청크에 포함되면 해당 원본 전체가 UNKNOWN으로 남을 수 있다. 이를 정상 멀티 기록의 완전한 전송 보장으로 표현하지 않는다.
- 로그 부재·형식 불일치·16 MiB 초과·불완전 쓰기·시각 모순은 미확인 처리한다. 로컬 판정 이력은 시간과 모드만 보존한다.
- 실게임 싱글→멀티→싱글 전환, 전용 서버의 실제 새 로그, 운영 서버 헤더 저장·표시: NOT RUN. 게임 실행이나 서버/DB 변경은 하지 않았다.
- 기존 cadence PARTIAL/세션 종료 짧은 추가 캡처와 실게임 FPS 보장 과제는 이 변경 범위가 아니다.

## 요청하지 않은 변경
- 0건. 새 외부 의존성, 서버 배포, DB 변경, 게임 조작 없음.

## 최종 게이트
- 구현·로컬 Release 테스트: PASS, 213/213.
- 실제 보관 로그 파싱: PASS, 6/6.
- WPF 자동 상태 화면: PASS. 웹 변경 없음.
- 실게임 전환 및 운영 수신 저장 검증: NOT RUN. 위 제한을 포함한 0.4.2 공개 요청에 따라 게시 절차를 진행한다.

## 생성 패키지
- Setup: 51,271,873 bytes, SHA-256 5e321cea52071a44b176b062f68d47081fe32cabac9ed12121c38f68dcdeb121.
- ZIP: 72,362,869 bytes, SHA-256 3efe2797fea9b50b71e37d3844e779127ef570f7cd9ead3104ee797257428085.
- 폴더·ZIP 공개 패키지 검사: 각 466개 파일, 금지 파일 0. 설치 파일 검사 PASS.
- 업그레이드 검증 중단 증거: work/validation-0.4.2/update-install.log.
