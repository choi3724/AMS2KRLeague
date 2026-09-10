# AMS2 0.7.0 STABILITY AUDIT

작성일: 2026-09-10. **최종 판정: YELLOW / PARTIAL.** 허용된 코드 수정과 로컬 자동 검증은 수행했다. 실게임·운영 서버·VR 실장비 검증이 남아 있으므로 제품 전체를 GREEN으로 판정하지 않는다.

## 기준선과 변경 범위

- BASE: v0.7.0 / `48b2881f1f6f4b6f758763ef9556e383db086e70`.
- 작업 경로: `E:\AMS2 KRLEAGUE\AMS2KRLeague`. HEAD 및 버전은 변경하지 않았다. 문서의 공개 Latest 기준은 v0.7.0이며 이 보고서는 새 운영 릴리스 조회 결과가 아니다.
- 최초 미커밋 변경: `.gitignore`, `docs/reports/`, `global.json`, `harness/`. 선행 하네스 설치 작업을 보존했다. 작업 도중 사용자가 변경한 `AGENTS.md`, `PROJECT.md`, 루트 `TASK.md`도 보존했다.
- 사용자 제공 루트 TASK는 0.7.0 안정화인데 `docs/TASK.md`는 과거 작업이었다. REQ-DOC-01에 따라 후자를 전자와 일치시켰다. 두 파일 SHA256: `AAE7C027C027AD4B6FA0825D5FB5789FF6B135440195D984F8187023C9B49099`.
- `README.md`, `VERSIONING.md`, `Directory.Build.props`의 현재 버전은 0.7.0. SDK는 기존 하네스의 `global.json` 8.0.424 / latestPatch를 유지했다.
- 보호 대상: 기존 선택 화면·기본/개량 타워와 그래프·독립 페달·계기판·ABS·핸들·전후방 접전·레이아웃·click-through·대기 UI·순위 애니메이션·Safety Car 제외·SHM 읽기 전용·기록/표시 분리·DPAPI·idempotency·공개/비공개·V1/V2·gzip·설치 파일 검증.
- 기존 `scripts/*.ps1`, `scripts/verify.sh`, CI는 수정하지 않았다. 서버 파일도 수정하지 않았다. 이번 범위는 최초 첨부 P0~P3 명세와 이후 사용자가 제공한 REQ 항목이다.

하네스 설치 당시 기준 테스트는 Client 135/135, Activity 110/110이었다. 수정 전 추가한 P0 회귀 두 개는 실제 FAIL했다. 새 AGENTS/TASK는 작업 중 수신했으므로, 이후 작성한 요구 대조표를 최초 코드 변경 전 작성했다고 주장하지 않는다. 원래 `verify.sh`는 후반 검증에서 실제 실행했다.

## 감사 발견별 판정

`FIXED`는 해당 코드 경로와 로컬 회귀 기준이다. 운영 발생 문제가 전부 해결됐다는 뜻이 아니다.

| 항목 | 판정 | 변경 및 근거 |
|---|---|---|
| D1 UNKNOWN이 불변 payload에 고정 | FIXED | provisional capture를 보존하고 확인된 멀티 근거가 생긴 뒤 전송 envelope를 한 번만 확정. HTTP와 모드 관측 worker 분리. 재시작 후 바이트·hash·key 유지, SINGLE_PLAYER 거부 |
| D2 활동 HTTP 200/201만으로 SENT | FIXED | 명시적인 stored/duplicate ACK와 오류 유무 검사. 응답에 있는 ID/hash/key 대조. 계약에 없는 필드를 새 필수 항목으로 요구하지 않음 |
| D3 모호한 telemetry 2xx 영구 격리 | FIXED | 빈 본문/HTML/잘못된 JSON/알 수 없는 status/semantic error는 미확정 전달로 재시도. exact bytes 보존 |
| D4 401 처리 및 인증 복구 불일치 | FIXED | 동일 installation identity로 계약에 있는 재등록 수행, 동시 복구 중복 억제와 backoff. 403/진짜 conflict 유지. 지연된 옛 401이 새 credential을 무효화하지 않음 |
| D5 Compact 종료와 JSON mirror gate 불일치 | FIXED | 검증된 Compact finalize와 loss ledger를 권위로 사용. ID/hash/sequence/time/privacy/accepted-durable 수 검사. 파일 존재만으로 통과하지 않음 |
| D6 gzip/metadata 사이 crash window | FIXED / 제한 있음 | 새 write-ahead commit manifest로 증명 가능한 누락 metadata만 복구. dry-run/apply/repeat 검사. 구버전의 증거 없는 고아 파일은 BLOCKED 상태로 남김 |
| D7 이상값이 chunk/finalize를 막음 | OPEN / 일부 FIXED | schema bound 사전 검증, 변하지 않은 poison 재인코딩 억제, 실패 raw 보존은 수정. 잘못된 값을 누락/정상화해서 COMPLETE 처리하는 정책은 적용하지 않았으므로 해당 attempt의 완료 보류는 남음 |
| D8 완전한 종료만 업로드 허용 | OPEN | 기존 gate 정책 유지. partial-session ingestion은 서버 취합·승인 의미를 바꾸므로 별도 결정 필요 |
| F1 VR bitmap capture 병목 | OPEN | 단계별 측정 추가. RenderTargetBitmap이 주 비용. BitmapCache는 악화되어 제거. 15Hz 캡처 설정을 올리지 않음 |
| F2 동기 로그 I/O 및 공용 대기 | FIXED | bounded background writer로 이동. 중복 unbounded 채널 제거. overflow/IOException/drain 회귀 |
| F3 전체 그래프/계기판 반복 재그리기 | FIXED / 실게임 미검증 | 활성 variant만 갱신, 픽셀 폭 기준 min/max 보존, ABS/누락 분할, 계기판 입력 arc를 별도 visual로 분리 |
| F4 짧은 read 누락에 history 삭제 | FIXED | STALE과 RESET 분리. 16/150/300/500ms 누락에서 history 유지, 참가자·세대·detach에서는 초기화 |
| F5 Dispatcher 종료 drain 대기 | FIXED / 비상 종료 제한 | 정상 종료는 timer callback → capture/finalize → logger 순서의 비동기 drain. Dispatcher 진행 검사. 비상 OnExit의 동기 fallback은 보존 |
| 추가 추정: 60Hz deadline 누적 방식 결함 | NOT_REPRODUCED | HEAD가 이미 절대 위상 방식으로 deadline을 전진시킴. 해당 cadence 코드는 수정하지 않음 |
| 사용자 제공 dotnet.exe 0xe0434352 화면 | NOT_REPRODUCED | 조사 중 별도 RenderBench 초기 실행 실패를 확인했으나 스크린샷 프로세스와 일치하는 사건 로그 증거 없음. 제품 오류 원인으로 단정하지 않음 |

## 요구사항별 수용 결과

| 요구 | 상태 | 실제 수용 결과 / 관련 파일 |
|---|---|---|
| REQ-DOC-01 | DONE | 사용자 TASK와 docs/TASK 일치, 0.7.0 문서·버전 검사 PASS |
| REQ-SPEED-01 | DONE (D) | 실제 실패 아카이브 복사본에서 이상값 및 이웃 표본 확인. 과거 SHM 원본 bytes가 없어 A~C 확정 불가. `PlayerOverlayCoordinator.DescribeSpeedAnomaly` 진단만 추가 |
| REQ-SPEED-02 | DONE | 아래 validation 정책 표. 범위 확장·clamp·0 치환 없음. `CompactTelemetryCodec`, `LocalDurableTelemetryArchive`에서 조기 검증/poison 반복 방지 |
| REQ-UPD-01 | DONE (자동 검증) | idle 허용, Practice/Qualify/Race 차단, 개인 완주만으로 허용 안 함, stable 결과 후 finalize ACK=false면 차단, true면 허용. SHA/size/URL 회귀 PASS. 실제 설치 중 주행은 NOT RUN |
| REQ-ARCH-01 | PARTIAL | 300/60/30초 동일 fixture 측정 완료. HTTP 증가 없는 독립 checkpoint는 별도 persistence 정책이 필요해 미적용. 기본 300초 유지 |
| REQ-MP-01 | PARTIAL | 실제 log silence >15초 확인, 60초 이상은 현재 로컬 자료에 없음. 합성 2분 silence→UNKNOWN 재현. late evidence 회복은 수정, lease 연장은 미적용 |
| REQ-HUD-01 | DONE (자동 검증) | gap/history/reset/geometry/hidden→visible 회귀 PASS. 실제 게임에서 프레임 확인은 NOT RUN |
| REQ-FAULT-01 | DONE (조사), NOT CHANGED | UiTick 의존성 고장 주입으로 전체 hide 확인. 자연 발생 패널 오류 미재현. 안전한 개별 경계가 확정되지 않아 제품 catch 동작 유지 |
| REQ-LOG-01 | DONE / 제한 명시 | 512줄 순서 보존, 20,000건 burst bounded/drop, exclusive file lock IOException 후 복구, 종료 drain 검사 PASS. WARN/ERROR 별도 우선 큐는 없음 |
| REQ-V2-01 | DONE (로컬), 운영 NOT RUN | V1 및 long-track 경계 회귀, 미지원 V2 exact-byte retry, C#/PHP 6개 V2 vector 일치. 운영 Cafe24 업로드는 실행 안 함 |
| REQ-VERIFY-01 | PARTIAL | Release build/145+111/하네스 및 기존 verify PASS. 실게임과 운영 E2E, Quest3/VD 미실행으로 YELLOW |

업데이트 수정 파일: `ActivityCaptureRuntime.cs`, `GitHubAutoUpdater.cs`, `App.xaml.cs`. 다운로드/검증 뒤 설치 시점과 종료 callback에서 재검사하고, capture와 경쟁하지 않도록 종료 인계를 예약한다. 상태창에는 `다운로드 완료 · 경기 기록 저장이 끝나면 자동 설치합니다`를 표시한다. finalize ACK 전 차단 검사는 내부 ledger ACK를 false/true로 전환하는 fixture이며, 실제 디스크를 강제로 멈춘 주행 검증은 아니다.

## 수집·기록·전송·표시 주기 구분

이번 작업에서 아래 제품 기본값과 서버 전송 시점은 변경하지 않았다. Hz는 빈도이며 설정상 상한/목표와 실제 달성값을 구분한다.

| 단계 | 현재 설정/의미 | 아닌 것 |
|---|---|---|
| 전체 SHM 읽기 | 약 30Hz 목표 full snapshot | 모든 필드가 30Hz로 파일에 기록되거나 서버에 전송되는 것이 아님 |
| 표시 전용 빠른 SHM 읽기 | 최대 60Hz, 경합하면 기다리지 않고 skip | recorder 입력을 늘리는 경로가 아님 |
| capture 표본 선택 | replay source gate 5Hz, driver/incident ring 20Hz | HTTP 요청 빈도가 아님 |
| Compact 기록 밀도 | replay progress 2,000ms, world XYZ 500ms, extension 20,000ms, close-battle 500ms | 파일 전송 주기가 아님 |
| 로컬 chunk | product 300,000ms | 5분마다 반드시 서버에 올린다는 뜻이 아님 |
| 서버 전달 | 기존 전체 경기 종료/자격/완료 gate 후 queue 전송. worker poll 5초, batch 활동 8개·telemetry 4개 및 기존 retry backoff | 0.2Hz 데이터 수집 또는 일정한 초당 HTTP 요청 수가 아님 |
| HUD 값 반영/애니메이션 | 전체 snapshot 계산 최대 20Hz, 주행 HUD 목표 최대 60Hz, WPF 렌더 이벤트 | 실제 표시 FPS나 모니터 주사율 보장 아님 |
| VR bitmap 생성 | 기존 최대 15Hz 캡처 설정 | 헤드셋 compositor/패널 주사율 아님 |
| 모니터/헤드셋 주사율 | 이 작업에서 측정·변경하지 않음 | 데이터 읽기·기록·전송 주기와 독립 |

빠른 표시의 중간 값을 생략하거나 픽셀 기준으로 그래프를 줄이는 것은 화면 처리다. 저장 원본의 표본 수, A2CT 필드, 서버 전송 바이트를 이 경로에서 변경하지 않는다.

## 비정상 속도: ROOT CAUSE D

실제 사용자 자료는 읽기 전용으로 복사했다. `%LOCALAPPDATA%\AMS2KRLeague\activity\future-telemetry\sessions`의 실패 gzip 45개, 총 19,027,336 bytes. 기존 파일 수정·재전송 없음. 복사본 분석 스크립트/결과는 작업공간 `work/speed-archive-audit.php`, `speed-archive-audit.json`, `speed-target-neighbours.json`에 남겼다. 사용자 식별자·차량 이름·토큰은 이 보고서에 싣지 않는다.

동일 participantRef 65 / slot 1 / generation 1의 이웃 관측 예:

| elapsed ms | speed m/s | world X | world Y | world Z |
|---:|---:|---:|---:|---:|
| 8929 | 614.1126708984375 | 4218.760742 | -75.324081 | 4036.796631 |
| 8976 | 704.5807495117188 | 4226.395996 | -75.807396 | 4044.356445 |
| 9008 | 58.00637435913086 | 4242.676270 | -76.054787 | 4059.463135 |

이 구간은 lap=1, distance=0, raceState=2, active=1이었다. 저장된 slot/generation이 같다는 사실만으로 원본 SHM의 일시 오류·parser·텔레포트·온라인 보간을 구별할 수 없다. 과거 read sequence와 원본 SHM 전체 bytes가 없어 D로 분류했다. 재현용 704.58075f를 parser에 넣은 회귀는 값 보존과 진단 offset/slot/generation을 확인한다.

| 경로 | 현재 validation / 의미 |
|---|---|
| full parser | root speed offset 6848, 참가자 speed 배열 10800+slot×4에서 읽음 |
| display-only read | 동일 root speed + sequence/state/viewed/active 일관성 검사 |
| HUD sample | finite/0~1000m/s 표시 수용 범위. 물리적으로 정상이라는 판정 아님 |
| capture adapter | finite/nonnegative 값 보존. Compact에 맞춰 clamp하지 않음 |
| DRIVER_FAST Compact | 0.01m/s 양자화, 정수 0..65535, 즉 655.35m/s 저장 범위 |
| PARTICIPANT_REPLAY Compact | 0.1m/s 양자화, 정수 0..6554, 즉 655.4m/s 저장 범위 |

차이는 표시 방어 범위와 불변 wire 표현 범위의 목적 차이다. 상한을 늘리지 않았다. 새 진단은 이상값에서만 최대 30초에 한 번 sequence/세대/상태/slot/raw speed bits/offset을 기록한다. 실패 raw는 보존하고, 변하지 않은 poison 청크는 한 번 실패 후 반복 인코딩하지 않는다. 원인불명 표본을 버리고 정상 COMPLETE를 만드는 처리는 하지 않았다.

## 로컬 내구성 비교와 결정

실제 archive/Compact commit을 사용하는 동일 60분·32대 합성 fixture. 72,000개 source frame, 기존 replay gate와 500ms world, private driver 포함. 사고·전체 세션 metadata·SHM·HTTP는 제외했다. 실제 R4 전송량 추정이 아니다.

| 측정 | 300s | 60s | 30s |
|---|---:|---:|---:|
| gzip 파일 수 | 61 | 302 | 603 |
| gzip bytes | 207044 | 357668 | 483165 |
| 평균 gzip bytes | 3394.1639344262294 | 1184.3311258278145 | 801.2686567164179 |
| 전체 로컬 파일 수 | 183 | 906 | 1809 |
| Commit 호출 수 | 24 | 120 | 240 |
| encoding+commit thread CPU ms | 2765.625 | 3234.375 | 4140.625 |
| 관측 최대 working set bytes | 289816576 | 140857344 | 115154944 |
| 종료 drain ms | 282.8665 | 142.8144 | 121.2387 |
| input drop / commit failure | 0 / 0 | 0 / 0 | 0 / 0 |
| 정상 처리 시 nominal memory-only 구간 s | 300 | 60 | 30 |

파일 수/Commit 호출 수는 OS WriteFile 호출 수가 아니다. CPU는 인코딩과 commit 합계이며 순수 인코더만 측정한 것이 아니다. working set은 1,000프레임마다 샘플링했다. I/O 지연/재시도/백로그는 nominal 시간을 초과할 수 있으므로 crash 손실의 절대 상한으로 표현하지 않는다. 실제 taskkill·전원 차단은 NOT RUN이다.

기준 v0.7.0의 300s fixture는 gzip 61개 / 207044 bytes / 로컬 파일 122개 / commit 24회였다. 수정본은 동일한 gzip **61/61개 SHA256 일치**, commit manifest 61개만 추가됐다. 바이트 비교 결과는 `harness/reports/compact-byte-parity.json`에 보존했다. 실패 원본 보존과 새 journal은 로컬 내구성용이며 서버 payload는 아니다.

**SELECTED: 300s 유지.** 단순 60/30초 변경은 memory window를 줄이지만 gzip chunk 및 대응 HTTP 요청이 약 5배/10배로 늘어난다. HTTP를 유지하면서 줄이려면 별도 로컬 checkpoint가 필요하다.

| 결정 후보 | 효과 | 비용/전제 |
|---|---|---|
| 현행 300s 유지 | wire/HTTP 변화 없음 | 정상 memory-only 구간 최대 약 5분, 장애 시 더 길 수 있음 |
| 로컬 30~60s append checkpoint + 최종 300s A2CT 유지 | HTTP/최종 chunk identity 유지 가능 | 원시 checkpoint 포맷·replay 중복 방지·checksum·보존/삭제 시점·재시작 순서 정책 필요. 추가 디스크 비용은 이 표에서 측정하지 않음 |
| A2CT 자체를 60/30s로 변경 | 현재 측정 그대로 구현 가능 | HTTP 증가 조건을 위반하므로 미선택 |

checkpoint나 기존 사용자 큐의 migration/resend는 적용하지 않았다. 새 manifest 복구는 원본 identity/privacy/hash가 입증되는 새 crash window에만 적용한다.

## 멀티플레이 로그 무응답

실제 `E:\Users\choi3\Documents\Automobilista 2\log\online.log`: 405009 bytes, SHA256 `00DF79089A9CF13D952EA58DA02F494D74892EEFF8347EFCACE948F4D43BA48C`. 이름/ID 원문은 제외하고 간격만 집계했다.

```text
multiplayer-marked event intervals=1288
join markers=4 leave markers=7
gaps >15s=2
gaps >60s=0
maximum gap=25.765s
p95 gap=6.859s
```

현재 detector는 마지막 관측 근거가 15초를 넘으면 UNKNOWN으로 내려간다. 합성 join 후 2분 silence에서도 UNKNOWN/업로드 금지를 확인했고, 이후 유효 log가 범위를 덮으면 늦은 근거로 회복되는 것을 검사했다. 새 mode worker는 HTTP 대기와 무관하게 관측하지만, 게임 로그 자체가 조용한 문제를 없애지는 않는다.

현행 15초+provisional 대기는 false-positive 권한 확대가 없다. bounded lease 30/60초 후보는 보류를 줄일 수 있지만 놓친 leave 후 업로드를 더 오래 허용할 위험이 있다. 한 파일의 최대 25.765초로 안전한 lease를 결정할 수 없어 연장하지 않았다. participant 수로 멀티를 추정하지 않았다.

## 렌더 성능과 리소스

Windows 10.0.19045 / .NET 8.0.30 / 16 logical CPUs / WPF tier 2. 동일 32대·개량 타워/그래프/페달/계기판·1920×1080·96dpi offscreen. 5초 준비+30초 측정. 합성 생산 목표 100Hz, 소비 Timer 목표 60Hz. 이 Timer는 production DrivingFrame 스케줄러 그 자체가 아니다. 실게임 SHM와 모니터 주사율은 측정하지 않았다.

| 측정 | desktop 기준 | desktop 최종 | VR capture 기준 | VR capture 최종 |
|---|---:|---:|---:|---:|
| 처리 간격 수 | 1200 | 1200 | 945 | 959 |
| 처리 간격 p95 ms | 35.0488 | 34.7846 | 53.2031 | 52.7927 |
| 처리 간격 p99 ms | 35.5381 | 35.2038 | 56.8608 | 57.7476 |
| 처리 간격 max ms | 37.2842 | 44.4406 | 60.2413 | 62.3052 |
| >16.7ms 간격 수 | 726 | 896 | 770 | 803 |
| >33.3ms 간격 수 | 372 | 193 | 375 | 348 |
| UI 처리 p95 ms | 0.3922 | 0.1378 | 25.1432 | 24.5077 |
| UI 처리 p99 ms | 0.4839 | 0.2493 | 27.0834 | 26.2779 |
| UI 처리 max ms | 2.3293 | 0.3428 | 33.0943 | 32.9858 |
| allocated bytes | 536220072 | 361365376 | 401417608 | 310644808 |
| GC pause ms | 39.549 | 24.79 | 24.414 | 18.879 |
| WPF Rendering callbacks | 5498 | 5284 | 3589 | 3590 |

할당량과 UI handler 비용은 줄었다. 간격 p99/max는 일부 악화되어 전반적인 frame pacing 개선이나 60fps 달성을 주장하지 않는다. 1회씩의 짧은 합성 측정이며 게임 부하에서 재검증해야 한다. callback 수는 presentation FPS가 아니다.

VR 단계별 p95(ms):

| 단계 | 기준 | 최종 |
|---|---:|---:|
| 전체 bitmap capture | 25.9567 | 25.4174 |
| compose | 0.0303 | 0.0274 |
| RenderTargetBitmap | 25.0617 | 24.6339 |
| CopyPixels | 0.3587 | 0.3197 |
| RGBA 변환 | 0.9295 | 0.6411 |
| 실제 SteamVR Submit | NOT RUN | NOT RUN |

최종 capture 360회, p99=27.1559ms, max=32.8614ms. BitmapCache 실험은 p95=43.5486ms로 악화되어 철회했다. 캡처 rate/resolution은 유지했다. `perf-final-vr-wrongflag.json`은 VR 인자를 누락한 데스크톱 재실행 기록이므로 VR 결과에서 제외했다. 최종 파일의 mode는 `OFFSCREEN_VR_CAPTURE_NO_HEADSET`이다.

실제 SHM skip/age, GPU 사용률, presentation FPS, Quest3/VD 제출 지연은 NOT RUN. 합성 desktop은 produced=2999/consumed=1198/coalesced=1801, source age p95=9.5005ms였다. 이는 화면이 최신 값만 소비한 합성 계측이며 저장 표본 누락 수가 아니다.

## UI와 로그 검증의 의미

- STALE 시 history를 남기되 새 사실로 추가하지 않는다. 350ms gap은 별도 geometry figure로 분리한다. 600개 원본 표본을 유지하면서 작은 폭의 min/max를 보존하는 회귀가 통과했다.
- 숨긴 speed panel의 값을 갱신하지 않다가 다시 켰을 때 최신 180km/h로 반영되는 것을 검사했다. 보이지 않는 기본/개량 중복 작업을 줄였다.
- 정상 종료에서 진행 중 timer callback을 막아도 Dispatcher 작업이 실행되고, 해제 후 drain이 완료된다. 비상 OnExit fallback과 실제 OS 종료/디스크 영구 정지는 별도 제한이다.
- 로그 queue는 기본 2048줄, 넘치면 새 로그를 버리고 counter에 기록한다. 수락한 줄 순서는 유지한다. ERROR의 임의 exception message 대신 type을 남겨 credential 노출을 막는다. WARN/ERROR도 overflow될 수 있으며 무손실 진단 로그를 보장하지 않는다. capture 원본 채널과는 별개다.
- 정상 512줄, 20,000건 burst, exclusive file lock 오류 후 복구를 검사했다. 중간 실행 `test-Client-20260910-145919.log`에서는 20,000건 중 written=3976, dropped=16024, producerMs=73이었다. 최종 실행별 숫자는 Client 로그의 `PROOF logger`에 남겼다. 비동기 소비량이므로 실행마다 written/drop 비율은 달라진다. 디스크 write가 영구 정지한 경우 drain 완료 시간은 보장하지 않는다.
- UiTick 오류 주입 fixture는 `_windowTracker` 의존성 고장으로 전체 hide를 확인한다. 실제 Dashboard/Telemetry 패널의 자연 발생 오류를 재현한 것은 아니다. 오래된 화면을 계속 보여 주는 것도 위험해 광범위 catch/last-good 우회는 추가하지 않았다.

화면 캡처: `harness/reports/baseline-hud-layout/`와 `harness/reports/final-layout-check/after/`. 후자는 기존 `--capture-layout` 전체 검사에서 155개 PNG를 생성했다. 기준 캡처 13개 중 10개는 PNG hash가 동일했다. 나머지 3개는 dashboard shift 애니메이션 캡처이며 픽셀 차이가 있다. 계기판·ABS 그래프·shift-0을 직접 대조해 배치/정렬을 확인했다. 정적 캡처와 합성 시간 계측을 실게임 애니메이션 검증으로 대체하지 않는다.

## 서버 계약, 호환성, 가시성

별도 서버 소스 `C:\Users\User\Documents\Codex\2026-09-01\files-pasted-by-the-user-2026\outputs\AMS2League\server\cafe24`를 읽고 로컬 PHP 검사만 실행했다. 운영에 같은 코드가 배포됐는지는 확인하지 않았다.

| 검사 | 실제 결과 | 범위 |
|---|---|---|
| tests/compact.php | 124 passed, 0 failed | C# V1/V2 decode, ingest, exact bytes, public/private, query schema coverage |
| tests/telemetry.php | 34 passed, 0 failed | InMemory API transport/idempotency/filter |
| tests/witness_reconciliation.php | PASS 17 checks | PDO reconciliation 순수 로직, hash/legacy/raw/stage/conflict |
| tests/public_privacy.php | PASS 17 checks | SQLite `:memory:` public/private SQL 검사 |
| tests/run.php | 45 passed, 0 failed | InMemory 수신→canonical 미승인 자료→standings API→서버 HTML. 미승인 자료가 공식 순위에 노출되지 않음 |
| 현재 C# V2 vector vs PHP fixture | 6/6 bytes 일치 | 0101/0110/0120/0121/0130/0140. 20,815.41m/100km/wrap 등의 기존 계약 |

공개 query schema는 `[1,16,32,33,64,80,81,257,272,288,289,320]`; private driver 304는 제외한다. 거리 외 범위·ordinal·V1 byte 계약은 유지한다. V2 미지원 오류는 exact-byte retry, 다른 invalid 400은 기존 분류다.

`PdoStore` 읽기 확인: witness와 telemetry 모두 transaction+identity/hash 비교를 사용한다. witness는 raw evidence와 normalized 결과/link를 같은 transaction에서 처리하고, 중복 재시도 시 finishSessionWitness를 재실행한다. telemetry 먼저 도착하면 이후 `linkTelemetryWitness`가 installation+witness 기준으로 연결하고, witness 먼저 도착하면 `resolveTelemetryJoins`로 연결한다. 같은 installation의 다른 source_session_uid는 같은 그룹으로 무조건 합치지 않는다. Compact 파일 기록 lock과 실패 transaction 파일 보상 삭제가 있다.

이는 코드 경로 확인과 로컬 fixture 근거다. 실제 MariaDB 동시 요청/중단 후 재시작/순서 역전과 production result API→실제 브라우저 상세·리플레이까지의 연속 E2E는 **BLOCKED / NOT RUN**이다. InMemory standings HTML 검사를 운영 결과 페이지 검증으로 부르지 않는다. 서버에 임시 endpoint나 승인 우회는 만들지 않았다.

로컬 관측성은 새 사용자 화면 대신 기존 로그/sidecar에 추가했다. session/attempt/chunk/activity ID, capture completeness, closeRequested, finalizeAck, durableAck, knownLoss, eligibility block, HTTP/code, receiverAck, retryAt를 남긴다. telemetry metadata의 LastHttpStatus/LastResultCode는 로컬 큐 상태이며 Compact wire 필드가 아니다. 알 수 없는 서버 문자열은 `SERVER_ERROR_UNRECOGNIZED`로 축약하고 credential 원문을 기록하지 않는다.

**SENT = 수신 서버의 명시적인 저장 ACK.** canonical 취합·관리자 승인·공개 게시 성공을 뜻하지 않는다.

## 수정 파일 묶음

- 전달/인증: `ActivityUploadModels`, `ActivityUploadWorker`, `Cafe24ActivityUploadTransport`, `ActivityConnectionOptions`, `TelemetryChunkUpload` 및 Delivery/Auth 회귀.
- 확정/복구: 새 `ProvisionalActivityStore`, 새 `CompactArchiveEvidence`, `CompactTelemetryChunkStore`, `LocalDurableTelemetryArchive`, accumulator revision, `RaceUploadCompletionGate`, `ActivityCaptureRuntime` 및 Provisional/RaceBatch/Archive 회귀.
- 표시/종료: `FileLogger`, `DrivingTelemetry`, 새 `PedalCurveBuilder`, 두 그래프 view, `DrivingDashboardView`, `OverlayWindow`, coordinator, App 및 Render/UiFault 회귀.
- VR: `OverlayWindow.Vr`, `VrOverlayController` 단계 시간 계측. 캐시 실험은 최종 diff에 없음.
- 하네스: `harness/render-perf/`, `harness/archive-perf/`, `harness/PERFORMANCE.md`. 측정 명령과 한계는 PERFORMANCE 문서 참조.

## 실제 실행 출력과 최종 게이트

최종 하네스 `harness/reports/remediation-final-gate.log`, run `20260910-151517`:

```text
[PASS] versions — canonical version: 0.7.0  (source: Directory.Build.props)
[PASS] secrets
[PASS] restore
[PASS] build — errors=0 warnings=0 log=build-20260910-151517.log
[PASS] test:Client — exit=0 passed=145 total=145 failed=0 log=test-Client-20260910-151517.log
[PASS] test:Activity — exit=0 passed=111 total=111 failed=0 log=test-Activity-20260910-151517.log
=== GATE: PASS (55.1s) ===
```

기존 verify.ps1의 build+145/145+111/111+레이아웃 캡처 및 verify.sh 검증도 PASS했다. 최종 보완 후 verify.sh 재실행도 exit=0, Client 145/145, Activity 111/111, 빌드 warnings=0/errors=0이었다. 결과는 `harness/reports/final-sh-verify.log`와 `final-sh-check/`에 기록했다. 두 검증 스크립트를 합치거나 CI를 변경하지 않았다.

실패도 남겼다: 130143 P0 fail-first 135/137, 134613 UI 137/140, 140207 geometry 검사 142/143, 143504와 145812는 새 진단/회귀 참조 누락으로 build errors=1. 이후 수정해 전체 게이트를 다시 통과했다. 150927에서는 새 문서의 개발 경로 4건이 시크릿 HIT로 검출됐고, Client는 metadata 동시 읽기 충돌로 144/145였다. 문서 경로는 값까지 검토한 exact allowlist 4건으로 처리했다. 임시 로컬 추적으로 integrity `0050.upload.json` 읽기의 IOException 공유 충돌을 확인했다. atomic 파일 교체와 함께 읽을 수 있도록 `FileShare.ReadWrite | FileShare.Delete` 및 열린 handle의 1MiB 상한을 적용했다. 원본 journal이 있어도 HTTP integrity CONFLICT는 차단하고 401 retry는 허용하는 독립 복사본 회귀, 교체 handle을 연 상태의 읽기 회귀가 통과했다. null loss 값을 0으로 취급하지 않도록 종료 증거 검사도 보강했다. 임시 추적 코드는 제거했다. 실패 로그를 PASS로 덮어 쓰지 않았으며 타임스탬프별 로그는 보존했다.

`git diff --check`의 남은 네 trailing-space 줄은 사용자 AGENTS 및 동기화한 TASK의 Markdown 강제 줄바꿈이다. 사용자 문서의 공백은 보존했다. 제품 source/tests의 whitespace 검사는 별도로 수행했다.

| 실제 검증 | 상태 |
|---|---|
| 실제 AMS2 주행/HUD/game-load CPU·GPU | NOT RUN |
| 실제 멀티 업로드 | NOT RUN |
| 실제 Nordschleife 운영 서버 E2E | NOT RUN |
| Quest3 / Virtual Desktop 깜빡임·지연 | NOT RUN |
| 운영 결과 웹페이지 브라우저 QA | NOT RUN |
| 사용자 기존 archive 수정/재전송 | NOT PERFORMED |

## 승인 대기 / 발견했지만 고치지 않은 문제

운영 실행 승인 요청은 하지 않는다. 이번에 운영 배포할 패키지를 준비한 작업이 아니다. 다음 결정은 구현 전에 별도로 확정해야 한다: 로컬 checkpoint 보존/복구 정책, multiplayer lease와 종료 gate/partial ingestion 정책. 각각의 영향은 위 표에 적었다.

남은 핵심 위험은 VR의 UI 스레드 bitmap 비용, 무로그 멀티 UNKNOWN, 원인 불명 이상속도/poison attempt 완료 보류, nominal 5분 memory window, 자연 발생 UiTick 오류 경계, 운영 DB/실게임 검증이다. 테스트 통과가 이 문제를 없애지는 않는다.

요청하지 않은 제품 UI/기능/디자인 변경: 0건. 공개 wire 확장·수집 cadence 변경·서버 전송 cadence 변경·private driver 활성화: 0건.

COMMIT / TAG / PUSH / RELEASE / CAFE24 DEPLOY: **NOT PERFORMED**.
