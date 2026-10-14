# 리그 수집·전송 수정 인계 — 2026-10-14

## 사용자 지시 / 작업 경계

오버레이 수정·검증까지만 한다. **커밋·태그·푸시·공개 릴리즈는 다른 코덱스 담당**이다.
제품을 운영 프로필로 실행하지 않았다. 운영 배포·DB 복구·승인·삭제·옛 큐 재전송도 하지 않았다.
시험 경기 없이 R13 저장 원본과 자동 회귀로 검증했다. 버전은 올리지 않았다.

- 오버레이 시작 HEAD: c51c675 (0.9.3).
- 웹: E:\AMS2KRPortal\outputs\AMS2League\server\cafe24, 시작 HEAD 603a372.
- 두 작업 트리 모두 시작부터 미커밋 변경이 있었다. 이를 보존했다.
- 범위: docs/TASK.md의 REQ-LEAGUE-INTEGRITY-20261014.

## 반복 원인과 실제 증거

1. 예선 FINISHED → Race FINISHED 잔상 → Race NOT_STARTED를 재시작으로 오인했다.
2. 관전 대상 변경에 따른 차량 랩 수 차이가 재시작 경계에 사용됐다.
3. 결과표 witness 전송까지 Compact finalize ACK/전체 종료 게이트에 묶여 있었다.
4. 최종 SHM 명단에서 사라진 참가자의 마지막 관측을 별도로 보존하지 않았다.
5. 서버의 단계 전체 해시 비교가 서로 다른 누락 명단도 충돌로 분류했다.
6. 서버의 승인 재검증에 아카이브의 독립 결과 증거가 전달되지 않았다.
7. Compact 설정이 있으면 실제 replay 유무와 무관하게 결과 등록에 아카이브를 요구했다.

R13 로그 client-20261013-222857-438-080d78.log:
23:24:37 결과 준비, 23:24:51 witness HTTP 201 STORED, 23:25:18 게임 창 없음, 23:48:34 CLIENT_STOP.
이 PC는 결과 전송 전 종료한 사례가 아니다. 서버 본선 witness 8개/텔레메트리 출처 2개도 읽기 전용 확인했다.

본선 원본:
C:\Users\User\AppData\Local\AMS2KRLeague\activity\witness\sessions\witness-71b1a843f6124b0c90315fd311bc1953--attempt-a95a9ff05b41438bb0ff6745be020bf4\source-evidence.json.gz

- SHA-256: de3b60c3154a540e3e33e90cd625eac0bd70d92ef01939342bf8472b71a95ee1
- 실제 스냅샷 73개 재생: 최종 명단 사람 11명, 전체 관측 사람 15명.
- RAG라그 18랩 RETIRED, DDookVeGi_RoK 11랩 RETIRED, LovelyCharry 1랩 RETIRED의 관측 보존.
- 세이나Seina는 7랩 RACING이 마지막이다. 최종 순위/리타이어를 만들지 않는다.
- 이전 slot/generation 이력도 보존하므로 observations 행 수를 그대로 참가자 수로 세지 않는다.
- 결과: work/league-integrity-audit-20261014/actual-r13/proof.json. 원본 불변, 전송 0회.

예선은 같은 sessions 폴더의 witness-b7a132560fb24d318b786ea8f68dafd2--attempt-5aa586bcada64441a9db7473513b42ad.
사람 15명+안전차 1명이며 운영 큐 전송 시도 0회 상태를 변경하지 않았다.
연습은 실제 진행되지 않아 복구 대상이 아니다.

## 이번 오버레이 변경 파일

- HostRecording/HostRecorderEngine.cs: 예선 잔상/첫 그리드 리셋과 재시작 구분.
  마지막 관측을 slot/generation/name별 최대 128개 별도 보존.
- HostRecording/HostRecorderModels.cs: 선택적 raceParticipantObservations 추가.
  관측 순위/RACING을 최종 순위/RETIRED로 바꾸지 않는다.
- ActivityCapture/ActivityCaptureEngine.cs: 예선 잔상 차단, local participant 기준 랩 rollback 비교.
- SessionWitnessUploadPayloadBuilder.cs, ActivityUploadPayloadBuilder.cs: 선택적 관측 전송.
- ActivityUploadQueue.cs: 새 메타데이터 정책 버전 2.
- RaceUploadCompletionGate.cs: 새 캡처의 immutable witness 전송을 Compact ACK와 분리.
  Compact는 자체 durable finalize ACK 필수. 기존 V0/V1 큐와 활성화 이전 캡처는 자동 해제하지 않는다.
  상세 기록 초기화 실패로 captureSessionId가 없어도 새 witness 자체 ID로 전송하며 Compact를 해제하지 않는다.
  경기 연결에 참가자 일치/교집합을 사용하지 않고 후보가 여럿이면 임의 선택하지 않는다.
- ActivityCaptureRuntime.cs: 새 정책 활성화 상태 로그.
- 테스트: SessionWitnessTests, RaceBatchTests, UploadQueueTests, Program, 새 LeagueEvidenceReplay.cs.
- 새 scripts/verify-league-integrity.ps1: .NET/PHP/실제 원본의 반복 검증 진입점.

재생 payload는 이벤트·날씨 포함 JSON 93,950 / gzip 9,054 bytes.
희소 73개 샘플 재생 결과이며 실제 고빈도 경기의 최악 크기/네트워크 성능 보장이 아니다.
원본에 없는 안정 프레임이나 종료 상태를 만들지 않았다.

## 대응 웹 변경 (함께 반영 필요)

서버의 부분 기록 수신 보호·정규화 분리를 먼저 반영한 뒤 오버레이를 릴리즈해야 한다.
새 오버레이만 먼저 배포하면 구 서버가 조기 종료한 부분 기록을 첫 본선값으로 고정할 수 있다.

- ClassificationRepair/FinalRaceResult: 독립 출처별 최종값으로 빠진 행 보충.
  같은 설치 재전송은 독립 투표가 아니다. 실제 최종값 충돌은 유지한다.
  부분/조기 종료 기록이 정상 완료 표를 막지 않으며 원본/기존 확정값/승인을 덮어쓰지 않는다.
- SessionWitnessValidator: 안전차만 있는 예선이 0명 본선으로 정규화되지 않는다.
  불안정/진행 중 표는 원본으로 보존. 새 관측의 범위·시간·키 검증.
- PdoStore: 누락 명단 차이는 PARTIAL, 겹치는 확정값 충돌만 CONFLICT.
- RacePublication/PdoPortalStore/PortalController: 승인 재검증에 근거 유지.
  실제 공개 replay가 없으면 결과표만으로 기존 수동 등록 가능.
- QualifyingWitnessRecovery: 예선 선도착/후도착 모두 기존 승인 경로에서 연결.
  이벤트 힌트/관측자·설정 지문·시간 경계·유일 후보로 판단하며 참가자 일치 불필요.
- PortalView: 종료 상태 미확인 표시, result-only 선수에 가짜 상세 링크 없음.
  Formula Hybrid Gen 3 코드명/중복 선택 수정, 기존 저장 선택 ID 유지.
- 새 tests/overlay_witness_contract.php: C# 산출물/실제 witness를 PHP 수신 계약으로 읽기 전용 검증.

## 검증 결과 / 남은 경계

- Release 격리 빌드: 경고 0 / 오류 0. Activity 116/116 (수정 전 114/114).
- Client 최종 재실행 202/202. 앞선 실행은 별도 Avante numeral pulse WPF 타이밍 1건이
  1.0 대신 1.0000052로 실패했다. 테스트/애니메이션을 바꾸지 않고 재실행했다. 간헐 실패를 숨기지 말 것.
- captureSessionId 없는 전송 경로까지 포함한 최종 통합 스크립트 실행도 exit 0.
  증거: work/league-integrity-audit-20261014/joint-final.log 및 joint-final 폴더.
  Release 경고/오류 0, Activity 116/116, Client 202/202, PHP 관련 26개 스위트와 실제 원본 계약 통과.
  이 로그의 게이트는 리그 수정 범위이며 아래 레거시 전체 게이트 실패를 제외·은폐한 GREEN이 아니다.
- PHP 관련 회귀: 최종 결과 31, witness 조정 19, 안전차/관측 22, 예선 복구 34,
  예선 선도착/결과표 단독 승인 3, 관리자 통합 104, 경기 설정 97, 재시도 25,
  gzip 계약 196, 포털 74 등 통과.
- app/tests PHP 구문 오류 0. 실제 본선/예선 원본과 C# 재생 결과가 PHP 수신 검증 통과.
  희소 재생은 stable=false여서 본선 정규화되지 않는 것이 정상이다. 원본과 신규 산출물 모두 전송 없음.
- 기본 verify.sh는 시스템 SDK 3.1 및 NuGet audit 네트워크 문제로 전체 GREEN 아님.
  휴대용 SDK 8.0.424/캐시 빌드에서 NuGetAudit=false 사용. 온라인 취약점 검사는 미실행.
- 레거시 tests/run.php는 fixtures/real-monza-31/session.json이 없어서 중단된다.
  tests/web03.php의 기존 lazy 이미지/국기 개수 및 검색 콤보 개수 단정 3건 실패.
  삭제/skip/완화하지 않았다. 관련 계약 통과를 전체 웹 GREEN이라고 보고하지 말 것.
- 시험 경기/실제 신규 네트워크 업로드/수정 서버의 운영 등록은 미검증.
- 로컬 브라우저 연결은 제목만 읽히고 DOM이 빈 document, 스크린샷은 CDP timeout이었다.
  실제 렌더/상호작용 QA를 통과했다고 보고하지 않는다. 운영 페이지를 변경하지 않았다.

## 이어받는 코덱스 실행 메모

1. AGENTS.md, PROJECT.md, docs/TASK.md, 이 문서를 읽고 최신 사용자 지시와 대조한다.
2. git status/diff부터 확인. **git add -A 금지**: UI/계기판/페널티/이전 R11·R12 변경도 존재한다.
   위 파일들에도 기존 수정이 섞여 있으므로 모두 이번에 만든 hunk라고 가정하지 않는다.
3. SDK: H:\CodexWorks\AMS2\Overlay\LeagueOverlay_2026-09-08\work\tools-0.8.4\dotnet\dotnet.exe
   PHP: C:\Users\User\Documents\Codex\2026-09-01\files-pasted-by-the-user-2026\.tools\php-8.4.25-nts\php.exe
   ext는 PHP 폴더의 ext. pdo_sqlite/openssl/sodium 사용.
4. 새 통합 스크립트에 SDK/PHP/ext/웹 루트/본선 gzip/예선 upload-payload.json 전달.
   OfflineRestore는 캐시 검증 전용이다. 기본 verify.sh를 대체하는 전체 릴리즈 게이트는 아니다.
5. 릴리즈 범위·서버 동반 반영 순서·레거시 게이트 실패·온라인 audit·실제 화면 QA를 확인한다.
6. 사용자가 맡긴 커밋·릴리즈만 수행. 운영 DB 복구/옛 큐 전송/삭제를 릴리즈의 부수 작업으로 하지 않는다.
7. R13 예선 복구는 대상 해시/서버 중복 대조 뒤 별도 선택 전송으로 처리한다.

전체 릴리즈 판정은 PARTIAL이다. 관련 소스/계약 회귀 통과와 운영 검증 완료를 구분한다.

이 인계는 다음 주 결과를 무조건 보장한다는 뜻이 아니다. 결과표와 상세 기록의 고장 경계를 분리하고,
실제 데이터로 재현한 결함의 수정 소스·회귀 증거를 전달한다. 없는 최종 순위는 미확정으로 유지한다.
