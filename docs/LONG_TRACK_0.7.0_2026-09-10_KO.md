# 0.7.0 장거리 기록 저장 수정 / 릴리즈 검증

## 수정
노르트슐라이페 트랙 길이 20,815.41m가 Compact V1의 20,000m 거리 상한을 넘어 저장이 실패했습니다.
트랙 길이뿐 아니라 주행·리플레이·사고·이벤트·주행 경로의 거리에도 같은 상한이 있어 6종을 함께 수정했습니다.

V1 스키마 ID와 필드 계약은 유지합니다. V1 양자화 범위를 넘는 거리 값이 있는 블록에만 V2를 선택합니다.
최대 100,000m, 거리 정밀도(Replay 0.1m / 나머지 0.01m), 필드 순서, 바이트 인코딩은 고정합니다.
V2 ID: 0x0101, 0x0110, 0x0120, 0x0121, 0x0130, 0x0140.
같은 값으로 V1/V2를 인코딩하면 2바이트 schema ID 이외에는 동일한 바이트가 나옵니다. 헤더나 기록 개수는 늘지 않습니다.
표시 갱신과 원본 수집 주기, gzip, 속도 상한, 승인·종료 gate, 개인 텔레메트리 비공개 정책은 그대로입니다.
서버 미반영 시 HTTP400 COMPACT_SCHEMA_UNKNOWN을 받은 알려진 V2 블록은 영구 격리 대신 기존 backoff(최대15분)로 재시도합니다. 다른 HTTP400·403·422와 V1 오류는 기존 처리합니다. 웹서버가 반영되면 같은 원본/식별자로 다시 전송합니다.

## 검사
- Release 빌드 경고 0 / 오류 0.
- Activity 110 통과: 20km 경계, 20,815.41m, 100km, 랩 거리 초기화, 음수/NaN/무한대/초과 범위 거부.
- 제품 저장 경로에서 V2 6종을 생성하고 종료 acknowledge 통과. V2 부분 저장 실패 재시도에서 동일 바이트 보존.
- C# 생성 벡터 → PHP 수신/조회/중복 요청/개인정보 구분 검사 124 통과.
- V1/V2 혼합 실제 SQLite replay 조회 16, incident window 36, public/private SQL 17 통과.
- 기타 PHP 결과/리플레이/사고/연습예선레이스 병합/시계 회귀 검사 통과.
- 최종 Client 135 / Activity 110: 총 245 통과. 서버 미지원→보관→재전송 성공, 원본 동일 바이트 유지 검사 포함.
- 로그: work/release-0.7.0-final-build.log. 기존 후보는 work/release-0.7.0-before-server-retry로 보존.
- 실게임 및 Quest 3/Virtual Desktop 실장비 테스트는 수행하지 않았습니다.

## 최종 공개 패키지 검사
- 디렉터리/ZIP 각 469파일, 설치 EXE 검사: 금지 항목 0.
- 최종 ZIP 추출 EXE: 버전 0.7.0, 종료 코드0, CAPTURE_COMPLETE 19, stderr0B.
- 로그·화면: work/validation-0.7.0-final/.
- ZIP 75,560,941B / SHA256 8ebf9bae9e44918e93bf32d2e74996f146a91057efbb9c15de979468b7458041.
- Setup 56,123,931B / SHA256 a2ee76a9127e05f9ceede905488ad280b0d2a1543cc2dc8be692d51d9eeabd58.
- 기존 설치 프로그램을 교체하거나 실행 중인 사용자의 오버레이를 종료하지 않았습니다.

## 과거 실패 원본
기존 실패 원본 7개를 원본 파일명의 SHA256과 대조하고 별도 work/long-track-recheck로 변환했습니다.
7/7 파일 모두 변환 가능했고 C#/PHP가 15개 Compact 블록(26,257 samples)을 검증했습니다.
Compact gzip 합계 175,808B. 저주기 호환 JSON metadata는 이 합계에 포함하지 않았습니다.
이를 전체 경기 복구나 추가 압축률로 해석하지 않습니다. 최초 실패 때 보존한 스냅샷이므로 전체 종료 시점까지의 완전성을 증명하지 않습니다.
기존 upload queue / loss ledger / 종료 무결성 플래그는 수정하지 않았고 서버에 보내지 않았습니다.
원본과 재생성 데이터는 Git/공개 패키지에 넣지 않습니다.
재검증 명령: Activity.Tests --long-track-recheck <failed-chunks 경로> <새 검증 폴더>.
교차언어 벡터 생성: Activity.Tests --long-track-vectors <새 폴더>.

## 웹서버
웹 소스는 Overlay Git 밖에서 수정하고 기존 두 app 파일과 테스트 파일을 별도로 백업했습니다.
CompactTelemetryProtocol.php의 V2 decoder뿐 아니라 RaceTelemetry.php의 결과/사고 처리 및 SQL schema ID 목록도 확장했습니다.
DB migration은 없습니다. 운영은 FileZilla 수동 FTP 반영이 필요하며 이 보고서 작성 시 배포하지 않았습니다.
웹 개발 Codex 전달용 ZIP: AMS2-0.7.0-long-track-server.zip (별도 workspace/work).
사용자가 다른 Codex에서 운영 서버 반영을 진행한다고 확인했습니다. 오버레이 GitHub 릴리즈와 운영 서버 배포는 별개입니다.
서버 패치 전에는 새 V2 블록을 거절할 수 있으므로 서버 선반영을 권장합니다. 반영이 늦으면 클라이언트가 COMPACT_SCHEMA_UNKNOWN을 재시도하며, 운영 수신은 서버 적용 후 확인해야 합니다.
0.7.0 릴리즈에는 앞서 완료한 UI/갤러리/텔레메트리/계기판/이벤트/VR 렌더 변경을 포함합니다.
