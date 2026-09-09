# 오버레이 gzip JSON 업로드 구현 결과

2026-09-09 · 작업 위치 `E:\AMS2 KRLEAGUE\AMS2KRLeague` · 0.5.1 소스 기준 미커밋 변경

## 구현 완료

- `POST v1/player/activities`, `POST v1/session/witness`의 기존 JSON 본문을 gzip으로 전송할 수 있도록 구현했습니다.
- 서버 bootstrap의 아래 광고를 읽고 해당 경로만 사용합니다.

```json
{
  "capabilities": {
    "gzipRequestRoutes": [
      "v1/player/activities",
      "v1/session/witness"
    ]
  }
}
```

기존 bootstrap 응답의 `event` 등은 그대로 필요합니다. 위는 추가 필드의 발췌입니다.

지원 정보는 실행 중에만 보관하고 실제 API 주소에 묶습니다. 서버/경로가 달라지면 재사용하지 않습니다. 지원 정보 없음, 잘못된 형태, 미지원 경로, bootstrap 실패는 기존 일반 JSON으로 처리합니다. 앱은 시작 시 bootstrap을 받으므로 서버 작업이 끝난 뒤 기존 실행에 반영하려면 앱을 다시 시작하여 확인해야 합니다.

압축 후 본문이 작아지는 경우에만 `Content-Encoding: gzip`을 붙입니다. `Content-Type`은 기존 `application/json`입니다. 압축해도 작아지지 않으면 일반 JSON을 보냅니다. 이미 gzip을 사용하는 텔레메트리 경로는 변경하지 않았습니다.

## 서버와 맞출 핵심

- 압축 해제 결과는 기존 `payload.json` 바이트와 같습니다. JSON을 재직렬화하지 않습니다.
- `Idempotency-Key`, 원래 본문의 SHA-256, 로컬 대기 파일과 메타데이터를 유지합니다.
- 서버도 압축 해제한 원본 바이트로 해시 계산·검증·저장을 해야 일반/gzip 재전송 간 충돌이 없습니다.
- 서버 오류에 대해 임의로 일반 JSON을 즉시 재전송하는 새 동작은 넣지 않았습니다. 기존 재시도·충돌·격리 처리를 유지합니다.
- 수집/전송 시점, 모드 판정, 개인 상세 업로드 차단은 그대로입니다.

## 검증 결과

- Release 빌드: 경고 0개, 오류 0개
- Client 테스트: **120/120 통과** (신규 gzip 관련 테스트 그룹 3개 포함)
- Activity 테스트: **107/107 통과**
- `git diff --check`: 통과

신규 검증에는 두 경로의 gzip 해제 후 바이트 일치, 대기열 재열기, 로컬 본문/메타데이터 보존, 동일 제출 번호 유지, 작은 본문의 일반 JSON 선택, 기존 서버 응답·worker 상태 처리, 지원 정보의 경로별 적용/실패/서버 변경/새 실행 초기화가 포함됩니다. 기존 텔레메트리와 레이스 종료 후 업로드 검증도 통과했습니다.

테스트의 HTTP는 모의 서버이며 실제 경기 자료를 재전송하지 않았습니다. 서버 담당의 실제 PHP 구현과 맞대는 연동 시험, 운영 배포, 운영 서버 수신 확인은 아직 하지 않았습니다. 현재 서버 담당이 작업 중이라는 사용자 안내를 반영해 서버 파일은 수정하지 않았습니다.

검증 로그: `work/validation-gzip-json-20260909/{build,client,activity}.log`

## 변경 파일

- `src/AMS2LeagueClient/Runtime/ActivityConnectionOptions.cs`: 실행 중 서버별 지원 상태
- `src/AMS2LeagueClient/Runtime/Cafe24ActivityUploadTransport.cs`: bootstrap 파싱과 선택적 gzip 전송
- `tests/AMS2LeagueClient.Tests/Program.cs`: 테스트 등록
- `tests/AMS2LeagueClient.Tests/Program.GzipUpload.cs`: 신규 계약·호환성 테스트

기존 미커밋 문서 두 개는 보존했습니다. 실행 중인 설치 프로그램을 교체하거나 재시작하지 않았으며 새 버전 지정·커밋·태그·릴리즈는 하지 않았습니다.

서버 작업 완료 후 전달받을 항목은 실제 bootstrap 응답, 두 경로 지원 여부, 일반/gzip 교차 중복 처리 시험 결과, 크기 제한/실패 처리 결과, 운영 배포 여부입니다. 그 결과를 기준으로 연동을 확인하면 됩니다.
