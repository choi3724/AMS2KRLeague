# 로컬 HUD 60Hz 제한 제거 — 2026-09-11

## 기준선

- main / HEAD `85442a7c7b901907623aaecb55f4285d802e7692` / v0.7.1. 기존 미커밋 변경 보존, 버전·커밋·릴리스 변경 없음.
- `scripts/verify.sh` 첫 실행은 기본 dotnet SDK를 찾지 못해 실패. 설치된 8.0.424 경로를 명시한 재실행은 exit=0, Client 148/148, Activity 111/111(3894ms).
- 보호 대상: Raw/Compact, 서버용 샘플 cadence와 HTTP 전송, 읽기 일관성·참가자 검증, 꺼진 HUD의 읽기 중지, 기존 UI와 디자인.
- 증거는 현재 Codex 작업 폴더의 `work/live-performance/`에 보존. 변경 전 파일은 `before-high-hz/`.

## REQ-HUD-HZ-01 — 구현 및 검사 완료 / 장시간 프레임 수용 PARTIAL

사용자는 로컬 텔레메트리의 고속 수집을 허용하고 서버로 보내는 데이터만 기존 주기로 유지하도록 지시했다. 이미 서버 기록은 별도 `ReadTelemetry → ActivityCaptureRuntime.Observe` 경로를 사용하므로, `DrivingFrame` 앞의 60Hz 게이트가 서버 보호에 필요한 것은 아니었다. 로컬 경로에 불필요한 제한을 남긴 구현 판단을 수정했다.

### 변경

1. `PlayerOverlayCoordinator.cs`: `_nextDrivingTicks` 및 `Stopwatch.Frequency / 60` 표시 읽기 게이트 제거. 각 고유 WPF 렌더 프레임에서 최신 SHM의 주행 필드를 읽는다. 같은 RenderingTime 중복 방지, 숨긴 HUD 읽기 중지, nonblocking reader lock, 버전·참가자·세션·sequence 검사 유지. 시작 로그도 고정 60Hz/144Hz 목표 표현 대신 `drivingReadCadence=EACH_RENDER_FRAME recordingReadRate=30Hz`로 수정했다.
2. `DrivingTelemetry.cs`: 로컬 표시 이력 상한 1024→4096. 240Hz의 10초 표시가 약 4.27초로 잘리는 회귀를 방지한다. 약 400회/초까지 10초가 들어가는 유한 버퍼이며, 그보다 빠르면 기존처럼 개수 상한으로 제한된다. 수집 주기를 제한하는 타이머가 아니다. 서버 기록 버퍼와 무관하다. 기존 PedalCurveBuilder의 화면 픽셀 열별 극값 보존을 유지했다.
3. `FastDrivingReadTests.cs`: 실제 coordinator 메서드와 별도 이름의 SHM fixture를 사용해 144개 고유 프레임의 읽기, 중복 방지, 숨김 중지, recorder counter 미증가를 검사한다. 게임 SHM를 테스트에서 수정하지 않는다.
4. `DrivingHudTests.cs`: 240Hz/10초의 2401개 관측값 유지와 4096개 메모리 상한 검사. 기존 상한 검사를 삭제·완화하지 않았다.
5. `PROJECT.md`, `docs/TASK.md`: 로컬 상한 제거 및 보호할 서버 계약을 명시.

이는 **로컬 빠른 읽기를 프레임당 수행하도록 고친 변경**이다. 독립 수집 스레드에서 고정 144/240Hz를 보장하는 구현이나 모니터 주사율 변경이 아니다. 실제 읽기 성공률과 게임 원본의 갱신률, 렌더링 FPS는 서로 다르다. 같은 원본 상태를 다시 읽을 수 있으므로 읽기 횟수를 모두 새로운 게임 사실의 횟수라고 표현하지 않는다.

### 서버 경로 보존

- 전체 스냅샷 타이머 33.333ms 유지.
- `ReadTelemetry` 메서드 본문은 변경 전 백업과 동일함을 문자열 비교로 확인했다. 정규화한 본문 SHA256: `76dff7d85bc86078b9aceff874af8184c54094a3a3586c1b9acae0db2a1b9fe7`.
- ActivityCaptureRuntime, TelemetryArchiveOptions, codec, upload queue, HTTP transport 파일을 이번 변경에서 수정하지 않았다.
- 로컬 fast read는 `_activityCapture.Observe`를 호출하지 않는다. 서버용 샘플링·내용 계약·전송 cadence를 바꾸지 않았다. 실제 관측값이 시간마다 달라지는 라이브 payload의 byte 동일성이나 서버 E2E를 시험했다는 뜻은 아니다.

## 회귀와 실행 결과

```text
기존 코드 + 신규 회귀 검사:
Expected 144, got 1.
RESULT: 147 passed, 1 failed, 148 total
TEST_EXIT=1

수정 후:
PROOF distinct display frames=144 local reads=144 duplicate frames skipped; hidden HUD reads=0 recording reads=0 (fixture, not measured FPS)
PROOF local history at 240Hz retains 2401 observed samples over 10 seconds
Release build: errors=0 warnings=0
RESULT: 148 passed, 0 failed, 148 total
Activity: passed=111 total=111 failed=0
PACKAGE_EXIT=0
PACKAGE_SMOKE_EXIT=0
CAPTURE_FILES=19
```

- 최초 검사 작성 시 내부 RenderingEventArgs 생성자를 public처럼 호출해 빌드 실패했다. 테스트 전용 reflection 생성으로 수정했다. 이 컴파일 실패를 제품 문제 재현으로 세지 않았다.
- `high-hz-repro2.log`는 잘못 선택해 실행한 기존 resource probe 결과다. 신규 제한 검사의 통과 증거로 사용하지 않는다. 실제 제한 재현은 `high-hz-red-test.log`.
- 최종 빌드/테스트: `harness/reports/verify-20260911-204037.md`, `test-Client-20260911-204037.log`, `test-Activity-20260911-204037.log`.
- 실행본: Codex 작업 폴더 `work/Avante-Cluster-Test-8/AMS2LeagueClient.exe --updates-disabled`, PID 25924. 이전 Test-7 폴더는 보존. Test-7 프로세스가 이미 종료된 것을 확인한 뒤 Test-8을 시작했다.
- DLL SHA256: `C22F12F36E470559BDD523D39E6363380CE4F67F1B33DF5233270FC4FF74E8F7`.

## 실제 싱글 레이스의 읽기 빈도

20:45:10.91 게임 재개 후 20:45:47.24 다시 일시정지됨. 해당 짧은 실제 게임 구간에서 다음 표본을 확보했다. 최초 부분 구간 20:45:12는 제외했다.

| 로그 시각 | 전체 snapshot 성공 Hz | 로컬 driving 성공 Hz | WPF callback Hz | 최대 callback 간격 ms | Client 전체 CPU % |
|---|---:|---:|---:|---:|---:|
| 20:45:22 | 30.9 | 80.2 | 97.5 | 17.1 | 4.726 |
| 20:45:32 | 28.8 | 74.4 | 86.0 | 19.6 | 4.946 |
| 20:45:42 | 29.9 | 91.7 | 93.6 | 26.4 | 4.980 |

Hz는 기존 로그의 약 1초 최근 성공률 표본이며 CPU/callback은 대략 10초 구간 통계다. 앞선 Test-7의 10분 구간과 장면·조건이 다르므로 엄밀한 같은 장면 A/B FPS 증가율을 계산하지 않는다. 높은 로컬 읽기와 기존 전체 snapshot 약 30Hz가 동시에 유지됨을 확인한 것이다. CPU 수치는 16개 논리 CPU 전체 기준이다. 위 세 구간 전체 working set은 370.0 / 371.7 / 375.5MiB였다.

로그: `client-20260911-204400-456-b4cfea.log`, 화면: `game-20260911-204511-475.png`. 자원 표본 `20260911-204443-resources.jsonl`에는 재개 전 일시정지 구간도 포함되어 있으므로 파일 전체 평균을 주행 부하로 사용하지 않는다.

게임 포커스가 없는 첫 입력은 안전 검사에서 거절됐으며 아무 키도 전달하지 않았다. 이후 게임을 활성화하고 정상 재개했다. 20:45:47 이후에는 다시 포커스를 가져오거나 게임을 강제로 재개하지 않았다. 서버/게임 설정은 변경하지 않았다. 현재 레이아웃 해시는 `E8AF3EB83A65C038FF2D31FFEE6C4609B4A742B1E6314D8BC210CE493E32805C`이며 앞선 20:17 해시와 다르다. 이번 실행 직전 백업이 없어 이번 실게임의 전후 동일성을 주장하지 않는다. 오래된 레이아웃을 덮어써 복원하지 않았다.

## 전체 게이트 — 최초 실패와 승인 후 재실행

`harness/scripts/verify.ps1`: **GATE: FAIL**, exit=1, 72.3초. 버전·restore·build·두 테스트는 PASS지만, 기존 보고서 3개의 로컬 경로 5건 때문에 secrets 검사가 FAIL이다. 값은 경로이며 비밀번호나 토큰이 아닌 것을 확인했다.

정확한 파일·검사 유형·경로 일치값만 허용 목록에 추가하려 했으나 자동 승인 검토가 거부했다. 사유: 성능 수정 승인에 검사 설정 변경까지 포함되지 않았다는 판단. 거절된 명령은 실행되지 않았고 검사 설정은 그대로다. 당시 사용자에게 해당 5건만의 허용 여부를 별도로 요청했다. 게이트를 건너뛰거나 거절을 우회하지 않았다.

사용자의 후속 “계속 진행 해”를 해당 요청에 대한 진행 승인으로 명시해 자동 승인 검토를 다시 요청했고, 이번에는 승인되어 정확한 5개 항목만 추가했다. 기존 보고서 경로 외의 비밀 검사는 유지했다. 변경 전 allowlist는 작업 폴더 `work/live-performance/before-high-hz/secret-allowlist.txt`에 보존했다.

```text
scanned=348 allowed=37 hits=0
SECRET CHECK: PASS
[PASS] versions
[PASS] secrets
[PASS] restore
[PASS] build — errors=0 warnings=0
[PASS] test:Client — exit=0 passed=148 total=148 failed=0
[PASS] test:Activity — exit=0 passed=111 total=111 failed=0
=== GATE: PASS (70.4s) ===
GATE_EXIT=0
```

재실행 보고서: `harness/reports/verify-20260911-205017.md`, 전체 출력: 작업 폴더 `work/live-performance/high-hz-gate2.log`. 이 PASS는 빌드·회귀 게이트의 판정이며 실제 지속 60fps 판정이 아니다.

## 남은 제한

- 60Hz 고정 로컬 읽기 제한은 제거했고 실제 74.4~91.7Hz 성공을 확인했다.
- 계속 부드러운 60fps, 전체 게임 스캔아웃, Test-8 장시간 10분 주행은 이번 결과로 PASS 처리하지 않는다. 17~26ms의 callback 간격과 sequence 거절이 남는다.
- 이전 게임/DWM 공통 0.4초 정지의 원인 함수와 CPU 래스터 비용은 별도 남은 문제다. 수집 상한 제거가 이를 모두 해결한다고 주장하지 않는다.
- 서버 E2E 및 VR 실장비 검증 NOT RUN.
- 요청 외 제품 UI/기능 변경 0건. commit/tag/push/release 없음.
