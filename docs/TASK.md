# REQ-RELEASE-072 — 0.7.2 공개 릴리즈 (2026-09-13)

사용자가0.7.2 릴리즈를 명시적으로 요청했다. 현재 누적된 제품·테스트·자산·검증 문서를 보존하고 버전/릴리즈 메타데이터를 갱신한다. 기준선 재검증, Release 빌드/158개 Client·111개 Activity 및 버전·비밀·공개 패키지 감사, 격리 패키지 실행, commit/tag/push/GitHub Latest 게시와 원격 자산/해시 확인까지 승인 범위다. 이전 작업의 commit/tag/push/release 금지는 이번 공개 요청에 한해 해제된다. 게임·기존 설치본·서버/Cafe24·DB·사용자 보정은 변경하지 않는다. 기존 Monitor RED와 실게임/성능/VR 미검증 경계를 유지한다. 백업·증거 work/release-0.7.2(작업 공간), repo work/validation-0.7.2.

# REQ-N-COMMON-RPM-10 — 최종 승인 90% / 97% 공통 표시 정책

이 항목이 아래 모든 이전 프로필/비율/fallback 제안보다 우선한다. mMaxRPM×.90 노랑, ×.97 빨강·200ms 점멸·외곽5쌍 완등, 최대=floor(red/1000+1)×1000. 차량명 내장 임계값 제거, 저장된 명시적 수동 보정 우선/해제 시 공통 복귀. 새 차량 무효 최대값은 경고 미생성, 같은 차량 일시 무효 최대값은 마지막 유효값 유지. 일반/확장 단일 resolver, 좌표/정적 자원/렌더 최적화 보존. 원본 데이터와 cadence 불변. 범위 AvanteRpmScale, AvanteClusterView, 기존 설정창 및 Overlay의 설정용 엔진 기준 cache, 관련 Avante 검사/문서. 기준선 work/rpm-common-policy. 실제 게임/성능 NOT TESTED, Monitor RED 유지. 별도 테스트 Client만 전환, 게임/설치/commit/tag/push/release 변경 없음. ponytail 미적용.

# REQ-N-VEHICLE-WARNINGS-09 — ARC Camaro 경고 프로필

실제 read-only SHM에서 ARC Camaro/mMaxRPM11500 확인. 사용자 게임 HUD 빨강 약11300 확인을 해당 정확한 차량 프로필에 적용, 자동 최대12000/빨강11300. 사용자가 게임에는 노랑이 없음을 확인했으므로 N 표시용 기존 명시적5/6 대체값(약9416.667) 유지, 게임 자동값으로 보고 금지. 기존 사용자 보정 우선, 다른 차량은 해당 프로필/미확인 fallback, mMaxRPM=red 추정 금지. 기존 차량 전환/정적 geometry/시각 회귀에 ARC 추가. 범위 위 AvanteRpmScale/AvanteRpmCalibrationTests 및 문서, 수집·전송 변화 없음.

# REQ-N-OUTER-GAUGE-08 — 바깥 분할 게이지 완등과 최대 눈금 분리

사용자16000 눈금/14800 레드존에서 마지막 블록이 켜지지 않는 문제. 알려진 경고값의 경우 redStart에서 좌우5쌍 완등, 4단계는 노랑~빨강 중간, 기존1~3단계 보존. 최대 눈금/바늘/색 경계/200ms 점멸은 유지. 미확인 경고 차량은 기존 흰 범위 진행 유지, 엔진 최대를 redStart로 추정하지 않음. 범위 AvanteRpmScale.LitPairs 표시 계산, 기존 Avante 테스트/Program/보고서. 후속 차량 전환의 경고값 누락도 현재 SHM/설정을 확인한다. 수집·기록·전송 계약/원본 자산/설치/commit/tag/push/release 변경 금지. 기준선 work/avante-outer-gauge.

# REQ-N-FLASH-07 — 실제 영상의 점멸 소실 / 밝기 전환

사용자191147242 영상의 거의 지속 점등을 확인하고 경계 왕복에서 초기화되는 점멸을 수정. 색상은 기존 바늘 보간을 유지하고 점멸은 표시/관측 모두 빨강일 때 적용(관측 RPM이 경계를 내려가면 즉시 종료), 경계 재진입 시 위상 보존, 숨김/중단/차량 설정 변경 시 해제. 기존 공유 표시 시계로 200ms 주기의 부드러운 밝기 전환(기존250ms보다 빠름), 매 tick opacity만 변경. 기존 정적 배경 gradient/숫자/폰트/경고 RPM/수집·전송 보존. Lola만 기존 수동 보정으로 최대16000(빨강14800) 적용, 다른 차량 자동 정책 유지. 범위 AvanteClusterView, 기존 Avante 테스트/Program, TASK/PROJECT/영상 및 통합 보고서. 별도 검증 실행본 사용, 설치/commit/tag/push/release 없음. 기준선 work/avante-blink.

# REQ-N-VIDEO-SPEED-06 — 속도 표시판 정렬 / 단위 크기

2026-09-12 사용자 좌측214·우측29 참조: 숫자는 표시판 내부 중심(1024,562)에 정렬, 기존 숫자 폰트108/고정 advance/기울기 유지. km/h는17로 축소하고(1168,589)로 이동하여 숫자 오른쪽 아래와 테두리 사이 여백 확보. 영상 FLNtQZ2MWi8의 follower/외곽/기어 링은 비교 보고만 작성하며 이번에는 변경하지 않음. 수정 전 소스·자산·별도 실행본 ZIP/SHA256 보존 완료. 변경 범위 AvanteClusterView, 기존 Avante 검사, TASK/PROJECT 및 영상 비교 보고서. 기록/전송/cadence/설정/원본 이미지 변경 금지.

# 최신 결과 — 참조 차량 경고 복구 / 숫자 통과 / 정렬

Lola·Iveco 참조 프로필로 노랑/빨강/점멸 복구(75% 미복원, 노랑은 명시적 대체값). 빠른 RPM 통과1~15 숫자 확대 보완, 원본 윤곽 발광선/흰 눈금 안쪽 붉은 띠/속도 광학 정렬. 최종 Release warning/error0, Client155/155, Activity111/111. 별도 테스트 실행, 설치/commit/tag/push/release 없음. 실제 시각 대조와 전체 차량 경고 자동 일치는 미완료. 기존 Monitor RED 유지. 통합 보고서 최신 절 참조.

# REQ-N-ALIGNMENT-05 — 추가 원본 비교 수정

기어 발광선의 원형 clip/색상 필터로 생긴 끊김과 이중선을 원본 chrome ridge를 따라 수정. 레드존 눈금은 흰 외곽선/흰 눈금 안쪽 붉은 띠 형태로 수정. 속도329 비교에서 광학 중심을 우측4/위8 design px 보정, 크기/기울기/고정 digit 유지. 변경 없는 프레임 자원 생성 금지. 범위 AvanteClusterView, 관련 검사 및 보고서.

# REQ-N-NUMERALS-04 — 중간 숫자 확대 누락

한 표시 프레임이 ±100RPM 영역을 건너뛰면 확대가 보이지 않는 경로를 수정. 빠른 통과 때 같은 공유 시계의 retained100ms 감쇠로 최고120%를 표시하고 느린 통과는 기존 RPM 비율 유지. 새 프레임별 Animation/geometry 생성 금지. 1~15 모든 숫자, 데이터 중단/숨김 원복 검사. 범위 AvanteClusterView와 기존 Avante 테스트.

# REQ-N-WARNINGS-RESTORE-03 — 참조 차량 경고 누락 복구

미확인 경고 차단으로 현재 Lola의 노랑/빨강/점멸이 사라진 회귀. 2026-09-12 사용자 게임 HUD와 실제 차량 식별값을 연결해 Lola B2K00 Ford-Cosworth - Superspeedway(빨강 약14800) 및 앞서 제공한 Iveco Stralis(약3800) 참조 프로필을 적용한다. 엔진 최대75% 규칙 복원 금지. 노랑은 기존 N 경고 방식의 명시적5/6 대체값, 실차량 실측으로 보고하지 않는다. 세 경고를 같은 resolver에서 복구하고 실제 표시/점멸 및 차량 전환 회귀. 원본 자산/렌더 최적화/수집·전송 보존. 변경: AvanteRpmScale, 기존 Avante 테스트/Program 및 보고서. 별도 테스트 Client만 갱신, 설치/commit/tag/push/release 없음.

# 최신 결과 — 75% 제거 / N 원본 동작

미확인 레드존75% 추정·중복 판정 제거. 숫자±100RPM에서최대120% retained 확대, 원본 기어 윤곽 발광선, 어두워지는 붉은 배경/레드존 눈금 적용. Client154/154·Activity111/111·Release warning/error0. 별도 테스트 실행, 설치/commit/tag/push/release 없음. 실게임 대조 NOT TESTED; 기존 Monitor RED 유지. 통합 보고서 최신 절 참조.

# REQ-N-REFERENCE-02 — 원본 참조 수정

- 75% 레드존 추정 제거. 미확인 경고 표시 중지 및 보정 출처 명시. 기존 보정 우선 유지.
- 천 단위 숫자 ±100RPM에서 1→1.2→1 retained transform. 바늘과 같은 표시 RPM.
- 원본 안쪽 윤곽의 경고색, 어두워지는 빨간 배경, 레드존 눈금 색상. 원본 자산/폰트/r349 보존.
- 범위: AvanteRpmScale, AvanteClusterView, DrivingHudSettingsWindow, 관련 Avante 테스트/문서. 수집/전송/설치 변경 없음. 기준선 work/n-reference-20260912.

# 최신 결과 — RPM 자동 범위와 N 표시 수정

레드존보다 큰 다음1000 RPM 단위 자동 최대눈금 적용(7400→8000,8000→9000). 기존 수동 최대값/설정 키 보존. 눈금 띠 r349 정렬, 속도 중심·고정 자릿수 및 원본5단계 진행 띠 복원. 최종 통합 gate153/153+111/111, 경고/오류0. 기존 Monitor RED, 실게임/VR NOT TESTED 유지. 별도 테스트 실행, 설치/commit/tag/push/release 없음. 통합 보고서 최신 절 참조.

# REQ-RPM-RANGE-01 / REQ-N-READOUT-01 — 최신 사용자 수정

- 확인/보정된 레드존 기준 자동 최대눈금은 (floor(red/1000)+1)*1000. 7400→8000,8400→9000,10500→11000,8000→9000. 노랑/빨강 자체는 눈금 변경으로 재비율화하지 않는다. 미확인 차량 기존 fallback 유지, mMaxRPM을 레드존으로 단정하지 않는다.
- 기존 보정창에서 레드존 기준 자동/최대눈금 수동을 구분. 기존 JSON은 수동으로 보존, 수동최대>red 엄격검증. 차량/설정 변경시에만 정적 자원 갱신.
- 속도 숫자 세로 중심을 표시창 가운데로 보정, 폰트108 고정 및 동일 digit advance/height 유지. 1의 비대칭 side bearing을 같은 자릿수 중앙에 보정. 폰트/이미지 교체 없음.
- 바늘 뒤 띠는 원본 HTML의 5단계 radial gradient 질감(투명→중간색→밝은 정점→외곽 음영)을 복원하고 흰/노랑/빨강 모든 stop을 갱신. retained path/brush 유지. 새 Bitmap/Animation/전체 레이어 매프레임 재생성 금지.
- 바로 앞 REQ-TICK-RIM-01(r349)은 유지. 기준선 gate verify-20260912-165404 PASS152/111, 경고/오류0. 기존 dirty 보존. 범위 AvanteRpmScale, AvanteClusterView, 기존 설정창 및 관련 테스트/문서. 설치/commit/tag/push/release 금지.

# REQ-TICK-RIM-01 — 원형 이미지 외곽에 눈금 띠 정렬

사용자 최신 첨부 기준: 기능성 눈금과 연결 원호만 바깥으로 이동해 원형 계기판 이미지/색상 배경 외곽에 맞춘다. 바깥 분할 게이지, 숫자·바늘·폰트·RPM 변환·색상 영역은 유지한다. 제품 변경 범위 AvanteClusterView.cs의 눈금 반경 상수만. 원본 자산/기존 dirty 보존. 정적 위치 변경은 전후 및 기존 8k/10k/12k 화면·회귀로 검증하며 새 테스트 러너/기능은 추가하지 않는다. 별도 테스트 실행본 갱신, 설치/commit/tag/push/release 금지.

# 2026-09-12 16:47 RPM 실제 HUD 참조 적용 수정

동일 차량의 기본 HUD임을 사용자 확인. 자동 프로필 누락을 수정해 Aston Martin Vantage GT3 Evo - Low Downforce에만 12000눈금/약7400빨강을 적용한다. 노랑6166.667은 명시적 N 비율 대체값. 기존 사용자 보정 우선. 최종 gate PASS61.5초, warning/error0, Client152/152, Activity111/111. 기존151건 유지. 별도 테스트 Client 실행(PID9852), 설치/commit/tag/push/release 없음. 수정 후 재실게임 대조 NOT TESTED, Monitor RED 유지. 보고서 상단 추가 기록 참조.

# REQ-RPM-LIVE-01 — 실제 HUD 참조 적용 누락 수정 (2026-09-12)

- 사용자 확인: 첨부 0~12 눈금은 같은 Aston Martin Vantage GT3 Evo - Low Downforce의 AMS2 기본 HUD. 최대 표시12000, 빨강 시작 약7400은 이 참조에 한정한다. 엔진 리미터/권장 변속값으로 해석하지 않는다.
- 원인: 자동 차량 프로필0개, 저장 보정0개여서 실사용은 기존 fallback만 적용. 실제 참가자 VehicleName으로 프로필을 찾고 기존 RootCarName 사용자 설정 키와 우선순위를 보존한다.
- 우선순위: 사용자 보정 > 해당 차량 HUD 참조 > 기존 명시적 fallback. 노랑은 실측 정보가 없어 참조 빨강의5/6인 N 비율 대체값임을 기존 보정 UI에서 명시한다.
- 범위: AvanteRpmScale, AvanteClusterView, DrivingHudSettingsWindow, OverlayWindow/App의 설정 전달, 회귀 테스트 및 이 보고서. 원본 이미지/폰트, 수집/기록/전송/설치/버전은 변경하지 않는다.
- 기준선: work/monitor-rpm/live-profile-baseline, 기존 테스트151/111. 사용자 스크린샷의 현재RPM1821 및 12000 눈금/7400 경계를 기준으로 자동 프로필 적용을 검증한다. 화면 참조 재현과 재실게임 대조를 구분한다.
- commit/tag/push/release/설치 교체 금지. 미커밋 변경 보존.

# 현재 결과 — 2026-09-12

최종 통합 게이트 PASS (Client151/151, Activity111/111, warning/error0). REQ-RPM/TICK 코드·WPF 화면·동일 조건 하네스 검증 완료. 실차량 대조, 30분 주행, 실제 게임 동일 조건3쌍은 미완료. Monitor 실게임 끊김 목표 FAIL/전체 RED. 사용자 게임 종료 후 재시작하지 않음. 다음 작업은 지연 프레임의 UiTick/스레드 stack과 capture gap 동시 귀속. 상세 상태와 증거는 `docs/reports/2026-09-12-monitor-live-integration-validation.md`.

# Additional active requirements — Avante RPM and ticks (2026-09-12)

- REQ-RPM-01: inspect installed SHM meanings; display-only vehicle calibration (maximum scale/yellow/red), one position mapping, explicit unverified fallback, stable invalid/stale handling and vehicle/session reset. Core Presentation settings additions authorized; raw telemetry and capture/upload untouched.
- REQ-RPM-02: cached static scale/sectors, dynamic needle/glow/red-region flash; boundary, transition, serialization and allocation regression; 8k/10k/12k screenshots.
- REQ-TICK-01: remove baked functional ticks using a cleaned derivative of the original PNG, preserve all pixels outside the tick ring through clipping; one inward tick band, reference proportions, independent number/needle radii. No opaque cover. Original assets/fonts retained.
- REQ-TICK-02: before/after magnified and scaled captures; report exact-reference limits, real vehicle NOT TESTED after user closed AMS2. No game restart, install replacement, commit/tag/push/release.

# Active task — Monitor live integration validation (2026-09-12)

Latest user scope: actual Client/game/Collector verification, confirmed product defects fixed; no Ponytail. Default WPF hardware/Monolith. Existing dirty work and v0.7.1 HEAD preserved.
- REQ-01: actual SHM/model/View/Dispatcher/composition, isolated optional startup-hook diagnostics; no forced UpdateLayout/GC. Resource/GPU/event metrics, physical FPS boundary.
- REQ-02: Monitor OFF/four HUD ON/local Collector, lifecycle and 30-minute run; preserve settings using a validation-only store. No operating-server test uploads.
- REQ-03: only reproduced defects. Interrupted reusable motion followed by the same target remains at the old value while inactive; fix in HudTargetMotion.Set. Regression added, no data/cadence/contract/design changes.
- REQ-04: baseline gate, integrated final gate, 3 alternating baseline/final real Client runs if product changes, per-run and long-run limits reported.
Baseline: development workspace work/monitor-live/baseline; 205 source/test files and product DLL SHA256 9A125B9D57ABEF2F2DF1A4B3942E6F335FE5E4AD1E1069F821CCBFCFAB560593. Existing script first failed one test; harness retry passed149/149+111/111,0 warnings/errors; failure retained.
Planned product scope: HudTargetMotion.cs only for the reproduced REQ-03 issue; targeted tests, diagnostic harness, report/PROJECT/TASK. No install/commit/tag/push/release, GPU port or process split. VR NOT TESTED.
Evidence/report target: docs/reports/2026-09-12-monitor-live-integration-validation.md.

# Active task — Monitor bottleneck implementation (2026-09-12)

User scope: analyze AND implement confirmed render/multicore bottlenecks; preserve current dirty Default WPF hardware / Monolith baseline.
- REQ-01: trace actual SHM/model/Dispatcher/View/render/composition flow, identified thread roles, lock/wait/backlog and scheduling evidence; no assumed core saturation.
- REQ-02: isolated harness/idle HUD/models without View/full animation/omit text-stroke/graph/needle-effects; common time-based presentation 60/120/144Hz; sufficient allocation trace separate from final unprofiled comparisons.
- REQ-03: one confirmed structural remediation batch: independent retained values/resources, cached geometry/stroke/bounds, dispatch coalescing, graph reuse, pedal motion and inactive lifetime. No blanket workers/Task.Run.
- REQ-04: conditional minimal direct GPU scene only if WPF native path remains dominant; no WPF adapter result as native ceiling. No process/IPC/product-wide migration without evidence.
- Gate: baseline → isolation → batch implementation → final integrated gate and at least 3 alternating Before/After runs with per-run/median/variation. Actual physical FPS remains unverified if unavailable.
Protected: SHM/Session/Activity/Witness/Compact/Archive/Upload meaning/order/precision/cadence; all assets/UI/settings; VR behavior. No game modifications/hooks/injection, install, commit/tag/push/release.
Baseline files/hashes and binaries: work/monitor-structure/source-before, baseline.json, product-before (development workspace). Planned product scope: AvanteClusterView, PedalCurveCache, DrivingHudViews, target/clock helpers only if supported; targeted regression additions; harness and reports.

Recorded outcome: structural remediation implemented, final gate PASS149/149+111/111, 3 alternating Before/After pairs completed, physical FPS unverified / YELLOW. See `docs/reports/2026-09-12-monitor-structural-remediation.md`. Native graph POC remains harness-only. No release/install.

# Active task — Monitor renderer matrix (2026-09-12)

Latest user scope supersedes previous tasks below. Base v0.7.1 / 85442a7c7b901907623aaecb55f4285d802e7692. Preserve all dirty source/assets/settings.
- REQ-01: isolated identical-scene A SoftwareOnly / B default WPF / C opaque diagnostic / D DirectComposition + D2D + D3D11 harness.
- REQ-02: same-duration CPU, WS/private, allocation, GC, GPU when available, DWM delivery Hz/p95/p99/>33ms. Never call DWM or callback Hz physical FPS. No repeated Desktop Duplication debugging.
- REQ-03: adopt only clear superiority: mean DWM >=60Hz, target p95<=20ms, CPU not increased, design/transparency/readability retained. Otherwise preserve product structure.
- REQ-04/05: compare identical Avante layers. A superior D is initially limited to Avante and graph surfaces; settings stay WPF.
- REQ-06: Monolith default. No new IPC/process or VR implementation without measured justification.
- Gate A: compile complete harness and run one matrix. Gate B: selected final structure only, Release/Client/Activity/verify/Compact-Wire/final performance once. No per-patch full tests.
Protected: SHM/Activity/Witness/Compact semantics; collection/recording/upload cadence; features/design/layout persistence/original assets. No game controls or VR hardware tests. No commit/tag/push/release/install.
Planned changes: isolated harness, task/report records; product render surface/policy only if matrix demonstrates clear winner.


Recorded outcome 2026-09-12: Gate A A/B/C/D compared; B Default WPF hardware + shared visible-only native presentation clock selected, Monolith maintained. Gate B PASS: Release 0 errors/0 warnings, Client 149/149, Activity 111/111. Performance YELLOW: CPU lower but allocation/private memory higher; pedal 59.908Hz/p95 27.775ms/>33ms 6. Physical FPS not measured. Additional native profile identifies WPF geometry/path CPU costs (render thread 60.287% of app CPU samples). No game/install/release. See `docs/reports/2026-09-12-monitor-renderer-matrix.md` for partial requirements and remaining work.

# Active task — v0.7.1 Overlay performance remediation (2026-09-11)

The latest user request supersedes the older task/release instructions below.
Base: 85442a7c7b901907623aaecb55f4285d802e7692. Preserve existing dirty Avante N/UI/assets.
No commit/tag/push/release. No game controls or new game tests.

- REQ-PERF-01: one render remediation batch: hardware policy, incremental retained graph chunks/pens, reusable RPM/steering/input target motion, static warning layers, hidden views, subscription/timer and image lifetime.
- REQ-PERF-02: preserve SHM/full-read/fast-read/capture/upload cadence and meaning. Presentation interpolation only; no fact synthesis or recording quality reduction.
- REQ-PERF-03: stop OFF/transparent view processing, release exclusive references, retain shared event/session analysis.
- REQ-ARCH-01/02: only after Gate 1 establishes a remaining structural bottleneck, compare Monolith vs Collector+Monitor total CPU/RAM/GPU/FPS/frame p95/p99/IPC/capture. No unconditional process split.
- REQ-VERIFY-01: Gate 0 once before edits; Gate 1 after all remediation; conditional Gate 2; final Gate 3. Build/Client/Activity/performance at integration gates; final verify and Compact/Wire parity. Never mark callback Hz as actual presentation FPS.
- VR: code/architecture review only; real headset unavailable. Its absence alone does not downgrade Monitor verdict.

Protected: SHM raw meaning; Activity/Witness; Compact V1/V2; source and server cadence/contracts; every existing overlay feature, saved setting and visual design.
Changed file scope: Presentation renderers/helpers/gallery, Overlay windows/interop/VR lifetime, coordinator display subscription/projection guard, presentation-only history revision, regression tests, this task, performance report.
Baseline: work/perf-remediation/gate0 (independent desktop fixture); harness/reports/verify-20260911-215230.md PASS, Client 148/148, Activity 111/111, zero warnings/errors.

Final recorded status: functional Gate 3 PASS (Release 0 errors/0 warnings, Client 149/149, Activity 111/111); performance YELLOW, actual presentation >=60fps not established. SoftwareOnly/Monolith retained. Current-control CPU/allocation did not improve; memory/lifetime changes verified. See `docs/reports/2026-09-11-overlay-performance-remediation-gates.md`. No game/release/installed-app changes.

# docs/TASK.md — v0.7.0 안정화 감사 및 핵심 회귀 방지

## 0. 작업 성격

이 작업은 새 기능 개발이 아니다.

현재 공개 `v0.7.0`을 기준으로,
실제 레이스에서 데이터 손실·업로드 보류·HUD 끊김으로 이어질 가능성이 높은 부분을
좁은 범위에서 조사하고 필요한 최소 수정만 수행한다.

현재 기준:

- Release: `v0.7.0`
- Commit: `48b2881f1f6f4b6f758763ef9556e383db086e70`

반드시 `AGENTS.md`와 `PROJECT.md`를 먼저 읽는다.

## 1. 최우선 규칙

- 요청하지 않은 UI/기능/설정 추가 금지.
- 디자인 변경 금지.
- 현재 0.7.0의 정상 HUD/레이아웃/Compact 계약을 최대한 보존.
- 원인을 확인하지 않고 threshold만 확대하는 식의 수정 금지.
- Client/Protocol 변경은 실제 필요성이 증명된 경우에만 수행.
- 서버/Cafe24 운영 배포는 이번 작업에서 수행하지 않는다.
- commit/tag/push/release는 사용자 별도 승인 없이는 수행하지 않는다.
- 자동 테스트만으로 GREEN 판정 금지.
- 실제 AMS2 검증이 필요한 항목은 `NOT RUN` 또는 `YELLOW`로 남긴다.

## 2. 범위 밖

다음은 이번 작업에서 하지 않는다.

- 새로운 Overlay 디자인
- 새로운 Dashboard/Telemetry UI 추가
- Web Portal 기능 개발
- Race Coach 개발
- 새로운 통계 기능
- 새로운 Race Story 기능
- Compact 전체 재설계
- Replay cadence 임의 증가
- Private Driver Telemetry 서버 업로드 활성화
- Steam/OpenXR 지원 범위 확대
- 기존 V1 schema ordinal 변경
- Cafe24 DB migration
- 사용자 요청 없는 폰트/색상/배치 변경

# 요구사항

## REQ-HUD-HZ-01 — 로컬 HUD 60Hz 읽기 제한 제거 (2026-09-11 사용자 지시)

- 사용자 지시: 로컬 텔레메트리는 더 높은 Hz로 수집할 수 있으며 서버 데이터의 주기만 기존대로 제한한다.
- 확인: DrivingFrame에 60Hz 게이트가 있어 프레임이 100Hz 이상이어도 표시 읽기는 약 58Hz다. 서버 Observe는 독립 ReadTelemetry에서만 호출된다.
- 수정: 로컬 HUD 60Hz 게이트 제거, 같은 렌더 프레임 중복 방지 유지. 표시 이력 4,096개로 확장해 240Hz의 10초 범위 보존.
- 보호: 전체 스냅샷 30Hz와 서버용 샘플 cadence·전송·원본·일관성 검사·꺼진 HUD 읽기 중지는 유지.
- 예정 파일: PlayerOverlayCoordinator.cs, DrivingTelemetry.cs, FastDrivingReadTests.cs, DrivingHudTests.cs, PROJECT.md, 이 항목, 결과 보고서.
- 수용: 144개 고유 프레임에 로컬 읽기 144회, 중복/숨김 읽기 없음, recorder counter 증가 없음; 240Hz 표시 이력 10초; 기존 회귀 통과. 실제 프레임률과 구분하여 보고.
- 예상 서버 용량 증가: 0 (고속 읽기에서 recorder/업로드를 호출하지 않음). 실제 전송 E2E는 별도 표기.


## REQ-DOC-01 — 저장소 문서 정합성 확정

### 목표

현재 `PROJECT.md`, `docs/TASK.md`, `README.md`, `VERSIONING.md`, `AGENTS.md`가
0.7.0의 실제 상태와 충돌하지 않는지 확인한다.

### 수용 조건

- `PROJECT.md` 기준선이 v0.7.0 / 현재 commit과 일치.
- `docs/TASK.md`가 과거 0.4.x/0.5.x 현재 작업으로 남아 있지 않음.
- AGENTS의 과거 handoff 문서가 현재 정책보다 우선하지 않도록 확인.
- README/VERSIONING의 Current version이 0.7.0과 일치.
- 충돌 발견 시 현재 사실로 수정하되 제품 동작은 변경하지 않음.

## REQ-SPEED-01 — 690~705 m/s 비정상 속도 원인 추적

### 배경

과거 장거리 기록 실패 조사 중 다른 attempt에서
약 690~705 m/s 범위의 speed 값이 관측되었다.

이 값은 정상 차량 속도로 간주하지 않는다.

### 조사 대상

- SHM 원본 speed
- viewed participant 전환
- slot/generation 변경
- session transition
- race restart
- teleport/reset
- snapshot sequence consistency
- parser offset/field 의미
- fast display read와 full snapshot read의 차이
- Compact quantization 전 입력

### 금지

원인 확인 없이:

- speed upper bound만 1000m/s 등으로 확대
- 이상값 clamp 후 정상값처럼 저장
- 해당 sample을 조용히 0으로 변경

금지.

### 수용 조건

다음 중 하나를 근거와 함께 확정:

A. parser/read 오류  
B. valid SHM transient but invalid vehicle telemetry  
C. participant/session transition artifact  
D. 기존 저장자료만으로 원인 확정 불가

A~C이면 최소 수정 + 회귀 테스트.

D이면 진단 로그/validation hook만 추가하고 실제 AMS2 재현을 다음 단계로 남긴다.

## REQ-SPEED-02 — Display/Archive validation policy 정합성

현재 HUD와 Compact Archive에서 속도 유효 범위 정책이 다를 수 있다.

### 수용 조건

- Display path와 Archive path의 현재 validation을 표로 작성.
- 서로 다른 이유가 명확하면 유지.
- 이유 없이 다른 경우 공통 semantic validator 또는 명시적 정책으로 정리.
- 정상 고속 차량 데이터를 임의 차단하지 않음.
- 이상값 때문에 전체 chunk가 실패하는 구조가 필요한지 재검토.
- raw evidence 보존 원칙 유지.

## REQ-UPD-01 — 경기 중 자동 업데이트 설치 차단

### 목표

새 버전 다운로드 자체는 가능하더라도,
실제 Race/Capture attempt가 진행 중이거나 종료 durable finalize가 끝나지 않은 상태에서는
Updater가 Overlay를 종료하고 설치하지 않게 한다.

### 필수 조사

현재:

```text
GitHubAutoUpdater
→ PrepareInstaller
→ exit callback
→ ExitClient
→ Coordinator Dispose
→ ActivityCapture Dispose/finalize
```

순서와 capture 상태를 추적한다.

### 요구 동작

- Capture/active race가 없는 상태: 기존 자동 설치 가능.
- Capture가 진행 중: 다운로드/검증은 가능, 설치는 보류.
- stable race result + durable finalize 완료 후: 설치 가능.
- 프로그램 종료을 강제하거나 race capture를 중간 분할하지 않음.
- 업데이트 보류 상태는 상태창에 이해 가능한 문구로 표시.

### 수용 조건

자동 테스트에서:

1. Idle update → install handoff 가능
2. Active Practice/Qualify/Race capture → install handoff 금지
3. Race result 관측됐지만 finalize ACK 전 → 금지
4. Finalize ACK 후 → 허용
5. 기존 installer SHA/size/URL 검증 유지

## REQ-ARCH-01 — 5분 local durable loss window 분석

### 목표

현재 product 구성의 `ChunkDurationMs = 300_000`이
비정상 종료 시 최대 얼마의 미커밋 데이터를 잃을 수 있는지 실제 코드로 증명한다.

### 우선 조사

- LocalDurableTelemetryArchive commit 시점
- Chunk flush 시점
- Process crash / power loss / taskkill 시 보존 범위
- Finalize 이전 memory-only data
- Retry source preservation과 정상 chunk의 차이

### 필수 산출

다음 profile을 동일 60분/32대 fixture 또는 기존 production-equivalent fixture에서 비교:

- 300s
- 60s
- 30s

각각:

- 로컬 파일 개수
- 총 gzip bytes
- 평균 chunk bytes
- encoding CPU
- peak working set
- 정상 종료 write 횟수
- crash 시 이론상 최대 미커밋 시간

### 변경 조건

측정 결과 30~60초 durable commit이 실용적이면
**로컬 commit 단위만** 축소하는 최소안을 적용할 수 있다.

단 네트워크 HTTP 요청 수를 함께 증가시키지 않는다.
필요하면 local chunk와 upload batch를 분리한다.

### 수용 조건

측정 없이 기본값 변경 금지.

## REQ-MP-01 — Multiplayer 15초 UNKNOWN 전환 감사

### 목표

실제 Multiplayer 세션에서 `online.log`에 새 이벤트가 15초 이상 없다는 이유만으로
MULTIPLAYER가 UNKNOWN이 되어 최종 업로드가 보류될 가능성을 검증한다.

### 필수 시나리오

1. Join evidence 발생
2. 60초 이상 네트워크 관련 새 로그 없음
3. Race 계속 진행
4. Leave evidence 없음

현재 구현 결과와 원하는 보안 의미를 비교한다.

### 변경 원칙

- participant count만으로 multiplayer 판정 금지.
- SHM에 없는 online 여부를 추정 금지.
- 명시적 Join evidence를 무한정 신뢰하는 것도 금지.
- 실제 Leave/process restart/session boundary를 고려.

### 후보

필요하면:

```text
Join evidence
→ bounded multiplayer lease/latch
→ explicit leave/process/session boundary
→ clear
```

형태를 검토한다.

lease 길이는 임의 결정하지 말고 실제 online.log 패턴/기존 실제 로그를 측정한다.

### 수용 조건

- 정상 멀티가 단순 log silence 때문에 업로드 불가 상태가 되는지 명확히 판정.
- 수정할 경우 false-positive multiplayer upload를 늘리지 않는 테스트 포함.

## REQ-HUD-01 — 60Hz Driving HUD transient gap 복원력

### 배경

현재 fast display read에서 약 150ms 이상 sample을 얻지 못하면 `null`이 전달될 수 있고,
DrivingTelemetryHistory는 `null` 입력 시 전체 history를 Clear한다.

### 목표

짧은 display-only read 실패가 10초 그래프 전체 초기화로 이어지는지 확인한다.

### 수용 조건

- 16~150ms 일시 누락
- 150~500ms 누락
- participant 실제 변경
- session generation 변경
- game detach

를 각각 fixture로 검증.

정상 요구:

- 일시 누락: 기존 history 유지 또는 시각적 gap
- 실제 participant/session 변경: history 초기화
- stale 데이터를 새 telemetry fact로 합성하지 않음

현재 동작이 이미 적절하면 수정하지 않는다.

## REQ-FAULT-01 — UI Tick 예외 fault boundary 조사

### 목표

Dashboard/Telemetry/Event 등 한 패널 오류가 전체 Overlay hide로 이어지는 현재 경로를 검증한다.

### 수용 조건

- `UiTick` exception → `_overlay.HideOverlay()` 경로를 재현 fixture로 확인.
- 개별 패널 오류를 안전하게 격리 가능한지 검토.
- 최소한 last-good-frame 유지가 가능한지 판단.
- 광범위 try/catch 추가로 실제 오류를 숨기지 않는다.

수정은 실제 재현과 최소 안전 경계가 명확한 경우에만 한다.

## REQ-LOG-01 — Unbounded telemetry log channel 위험 평가

### 목표

`Channel.CreateUnbounded<TelemetryLogEntry>`가
장시간 disk stall/failure에서 메모리를 무제한 늘릴 가능성을 측정한다.

### 수용 조건

- 정상 log rate
- synthetic burst
- logger I/O delay
- logger IOException

조건에서 queue/memory behavior 측정.

필요할 경우:

- bounded queue
- low-priority log drop counter
- WARN/ERROR 우선 보존

을 적용한다.

Telemetry/Capture fact 자체를 log queue와 함께 drop하지 않는다.

## REQ-V2-01 — Long-track V1/V2 Client 호환 회귀

### 목표

0.7.0에서 추가된 long-track V2가 기존 V1을 깨지 않았는지 Client 측 회귀 고정.

### 수용 조건

- 20,000m 이하 → V1
- 20,000m 경계 → 정의된 계약대로 V1
- 20,815.41m → 필요한 block만 V2
- 100km 이하 → 지원
- 지원범위 초과 → 명확한 실패
- V1/V2 mixed attempt decode/metadata 정상
- V2 `COMPACT_SCHEMA_UNKNOWN` → retryable
- 다른 invalid 400 → 기존 정책 유지

Cafe24 운영 수신 확인은 이번 작업의 배포 범위 밖이며
실제 서버 검증이 없으면 `NOT RUN`으로 보고한다.

## REQ-VERIFY-01 — 전체 회귀 및 실제 검증 분리

### 자동 검증

최소:

```powershell
.\scripts\verify.ps1
```

또는 이에 해당하는:

- Release build
- Client tests
- Activity tests

전부 수행.

### 실제 검증 상태를 별도 보고

다음은 자동 테스트와 섞지 않는다.

- 실제 AMS2 주행 60Hz HUD
- 실제 Multiplayer upload
- 실제 Nordschleife 20km+ upload
- 실제 Quest 3 / Virtual Desktop
- 실제 game-load CPU/GPU

실행하지 않았으면 `NOT RUN`.

### GREEN 조건

GREEN은 다음을 모두 만족할 때만 가능:

- 자동 회귀 전부 PASS
- 발견한 P0/P1 코드 결함 해결 또는 명확히 무해함 증명
- 데이터 손실 위험 증가 없음
- 기존 Compact V1 호환 유지
- 요청 외 UI 변경 0
- 문서 정합성 정상

실게임이 필요한 핵심 문제가 남으면 최종은 YELLOW.

# 3. 변경 전 반드시 작성할 조사표

코드 변경 전에 아래 표를 먼저 채운다.

| ID | 현재 동작 | 재현 여부 | 위험 | 수정 필요 | 수정 파일 후보 |
|---|---|---|---|---|---|
| SPEED | | | | | |
| UPDATER | | | | | |
| DURABLE | | | | | |
| MULTIPLAYER | | | | | |
| HUD GAP | | | | | |
| UI FAULT | | | | | |
| LOG QUEUE | | | | | |
| V2 | | | | | |

원인을 모르는 항목을 추측으로 수정하지 않는다.

# 4. 보호 대상

이번 작업 중 반드시 그대로 유지:

- 0.7.0 Overlay 선택 화면
- 기본/개량 Timing Tower
- 기본/개량 Telemetry
- Pedal Gauge
- Racing Dashboard
- ABS 표시
- Steering 표시
- Behind Battle event
- Waiting overlay
- Position animation
- Relative gap 색상 규칙
- False LAP gap 수정
- Safety Car 제외
- 60Hz display-only read와 recorder 분리
- Existing Activity/Witness upload idempotency
- DPAPI credential
- A2CT V1 schema
- Long-track additive V2
- gzip upload
- installer verification
- click-through
- layout persistence

# 5. 최종 보고 형식

```text
AMS2 0.7.0 STABILITY AUDIT

BASE:
v0.7.0
48b2881f1f6f4b6f758763ef9556e383db086e70

FINAL:
GREEN / YELLOW / RED

DOCUMENT CONSISTENCY:
PASS / FAIL

ANOMALOUS SPEED:
ROOT CAUSE:
FIX:
TEST:

AUTO UPDATE DURING CAPTURE:
CURRENT:
FIX:
ACTIVE CAPTURE BLOCK:
PASS / FAIL

LOCAL DURABILITY:
300s:
60s:
30s:
SELECTED:
REASON:

MULTIPLAYER LOG SILENCE:
CURRENT:
FIX/NO FIX:
FALSE POSITIVE RISK:

DRIVING HUD GAP:
PASS / FAIL

UI FAULT ISOLATION:
PASS / FAIL / NOT CHANGED

LOG QUEUE:
PASS / FAIL / NOT CHANGED

LONG TRACK V2:
PASS / FAIL

RELEASE BUILD:
warnings <n>
errors <n>

CLIENT TESTS:
<n>/<n>

ACTIVITY TESTS:
<n>/<n>

REAL AMS2:
PASS / FAIL / NOT RUN

REAL MULTIPLAYER:
PASS / FAIL / NOT RUN

REAL NORDSCHLEIFE SERVER E2E:
PASS / FAIL / NOT RUN

QUEST3/VD:
PASS / FAIL / NOT RUN

REQUEST-OUTSIDE CHANGES:
0 / <count>

COMMIT:
NOT PERFORMED unless explicitly approved

TAG:
NOT PERFORMED unless explicitly approved

PUSH:
NOT PERFORMED unless explicitly approved

RELEASE:
NOT PERFORMED unless explicitly approved
```

# 0.7.1 릴리스 지시 — 2026-09-10

사용자가 안정화 개선 사항을 포함해 0.7.1 커밋·태그·푸시·GitHub Latest 릴리스를 명시적으로 승인했다. 공개 안내는 적용된 개선 사항 중심으로 작성하고, 검증 안내는 VR 실기기 미검증만 표시한다. 기존 상세 감사 보고서는 당시 조사 기록으로 보존한다. 아래 승인 전 금지 규칙은 이번 릴리스에 대한 사용자 승인으로 충족됐다.

# 6. 완료 후

필요한 코드 수정과 자동 검증까지 완료하면 멈춘다.

사용자의 별도 요청 없이:

- 버전 증가
- commit
- tag
- push
- GitHub Release
- Cafe24 배포

를 진행하지 않는다.
