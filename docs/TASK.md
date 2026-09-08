# 자동 싱글·멀티 구분 — 2026-09-08

- REQ-MODE-01: AMS2 공식 online.log의 로컬 접속 성공/호스트 생성/퇴장으로 자동 구분. 참가자 수, AI 이름, 수동 선택으로 추정하지 않는다. 로그 부재·오래된 실행·읽기 실패는 UNKNOWN.
- REQ-MODE-02: 모든 결과/Witness/리플레이 전송에서 실제 기록 시각의 모드를 검사한다. SINGLE_PLAYER 및 UNKNOWN은 로컬 보관하며 전송하지 않는다. 기존 대기열도 검사한다. 멀티 접속 사이의 싱글 구간을 멀티로 덮지 않는다.
- REQ-MODE-03: 기존 결과 raceMode는 MULTIPLAYER/SINGLE_PLAYER/UNKNOWN. Witness 및 리플레이 업로드 메타데이터에도 같은 구분값. Compact binary와 원본 증거는 그대로 보존한다.
- REQ-MODE-04: 상태창의 수동 모드 선택을 제거하고 자동 판정과 전송 상태를 표시한다. 대기 오버레이도 자동 모드를 사용한다.
- REQ-MODE-05: 경계·재시작·누락·옛 대기열·배치 선택·실제 로그 재생과 HTTP 필드 테스트. 사용자가 0.4.2 정식 Latest 릴리스를 요청했으므로 패키지·업데이트 검증 후 커밋·태그·푸시·게시한다. 게임 실행과 서버 배포는 요청 범위가 아니다.

기준선: main db4a29a, clean. scripts/verify.sh 통과: Release 경고/오류 0, Client 107/107, Activity 102/102. work/validation-auto-session/baseline/.
근거: 설치된 공식 SharedMemory.h v14는 authoritative multiplayer 필드 없음. Reiza 공식 멀티 로그 안내와 사용자 보존 online.log 6개에서 로컬 생성/접속/퇴장 메시지를 확인했다. 과거 원본 로그는 읽기만 한다.
보호 대상: 오버레이 타이밍/페널티, 로컬 영구 저장, 원본 Compact/GZIP·해시·세션 ID, 업로드 인증과 재시도, 설정·기존 서버 데이터.
변경 범위: 자동 로그 판정기와 로컬 판정 이력, ActivityCaptureRuntime, 업로드 큐의 전송 자격 검사, 결과/Witness/리플레이 mode 필드, 상태창·대기 오버레이 연결, 관련 테스트·문서. 서버/DB 변경 없음. 판정 정보는 로컬 시간 구간만 저장하고 Steam ID·로그 원문은 전송하지 않는다.
