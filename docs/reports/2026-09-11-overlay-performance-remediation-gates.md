# AMS2 v0.7.1 Overlay 성능 최적화 — Gate 보고서

- 날짜: 2026-09-11 / Codex
- Base / 작업 후 HEAD: `85442a7c7b901907623aaecb55f4285d802e7692` (`v0.7.1`). 이번 변경은 미커밋.
- 기존 미커밋 아반떼 N/UI/설정 변경과 이미지·폰트 소스를 보존했다. 기존 작업 트리의 수정 파일을 되돌리지 않았다.
- 참조: [오버레이 리소스·애니메이션 코드 원인 분석](2026-09-11-overlay-code-resource-audit.md).
- **FINAL: YELLOW. 자원 수명/렌더 계산 개선과 기능 게이트는 통과했으나, Monitor 실제 표시 60fps 및 프레임 지연 개선은 통과하지 못했다.** VR 미검증 때문에 내린 판정이 아니다.

## BASELINE — GATE 0

코드 수정 전 한 번 측정한 값이다. CPU는 프로세스 CPU 시간 / 경과 시간 / 논리 코어 16개이며 PC 전체 CPU 사용률이 아니다. MiB는 1,048,576 bytes. RAM은 측정 종료 시점 값이며 peak가 아니다. GPU 엔진 카운터 인스턴스가 없어 **N/A**이다. SoftwareOnly도 DWM의 GPU 합성 비용까지 0이라는 뜻은 아니다.

| 구성 | CPU 전체 코어 기준 % | 종료 Working Set MiB | 종료 Private MiB | allocation MiB/s | callback Hz | callback p95 ms | callback p99 ms | 33ms 초과 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Dashboard | 0.292641 | 211.820312 | 152.835938 | 3.091483 | 64.029627 | 16.076700 | 16.121100 | 0 |
| Avante N | 1.884802 | 174.789062 | 118.285156 | 19.120973 | 65.105204 | 16.073700 | 16.129100 | 0 |
| 전체 조합 | 4.168661 | 199.167969 | 132.609375 | 39.771300 | 64.999666 | 16.069700 | 16.128000 | 0 |

- CPU / RAM: 위 표.
- GPU: N/A — `NO_COUNTER_INSTANCE`, 측정된 0%가 아님.
- FPS / FRAME P95 / FRAME P99: **실제 presentation NOT MEASURED**. 표의 callback은 대체 합격 지표가 아니다.
- DWM 표면 전달: Dashboard 64.030387Hz / p95 16.058594ms / p99 16.090088ms; Avante 63.980981Hz / p95 16.056152ms / p99 16.082764ms.
- 초기 verify: Client 148/148, Activity 111/111, errors=0 warnings=0, `GATE: PASS (59.0s)`.
- [초기 원시 결과](../../harness/reports/perf-gates-20260911/gate0/summary.json).

## 측정 범위와 비교 제약

실게임은 조작하거나 다시 실행하지 않았다. 별도 프로파일을 사용하는 WPF 데모 프로세스에서 3초 warm-up 후 약 30초씩 측정했다. 설치본·실제 SHM·실제 업로드를 사용하지 않았다. Dashboard 255×95, Avante 340×298, 그래프 340×90, 페달 120×127, 타워 385×520, 상대차 291×85, 세션 149×90이다. 전체 조합은 옵션 8개 중 내용이 있는 6개 창을 표시한다. Event/RaceControl의 모든 실제 상황을 재현한 것이 아니다.

신규 driving sample은 distinct Rendering callback마다 주입한다. 사전 history 1440개, nominal 144Hz, 동적 속도/RPM/페달을 사용한다. Shell fixture는 `next=t+.05`로 예약하므로 약 16Hz로 양자화된다. **이 fixture의 Shell cadence는 제품의 절대 deadline 기반 최대 20Hz와 동일하지 않다.** 수집/서버 Hz를 화면 FPS로 계산하지 않았다.

초기와 최종 사이 표시 환경/스케줄링이 동일하다고 확정할 수 없다. 최종 Gate 내 보존된 Test-8 바이너리 대조군도 p95 약 30ms를 보였다. 따라서 초기→최종 수치 변화 전부를 코드 효과라고 주장하지 않는다. 다음은 현재 환경 대조군의 실제 결과다.

| 구성 | CPU 전체 코어 기준 % | 종료 Working Set MiB | 종료 Private MiB | allocation MiB/s | callback Hz | callback p95 ms | callback p99 ms | 33ms 초과 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Dashboard | 0.107331 | 211.937500 | 152.714844 | 2.887036 | 59.981242 | 30.241500 | 31.082700 | 0 |
| Avante N | 1.645753 | 176.046875 | 118.859375 | 18.337576 | 59.645426 | 30.766000 | 31.163700 | 0 |
| 전체 조합 | 3.735541 | 199.292969 | 132.585938 | 37.579971 | 60.854528 | 30.184000 | 31.131300 | 1 |

원본 JSON은 반올림하지 않은 값과 UTC 측정 시각을 보존한다. 정적 타워·세션에 매초 60번의 표면 변경이 없는 것은 정적 내용의 특성이므로 animation stall로 합산하지 않는다.

## AVANTE N

- Before: 초기 CPU 1.884802%, Working Set 174.789062MiB, allocation 19.120973MiB/s.
- After: CPU 2.332576%, Working Set 165.238281MiB, allocation 17.947478MiB/s.
- 주 병목: 샘플마다 65ms Animation 생성, RPM마다 경고층/sector 명령 재작성, 소프트웨어 렌더에서 고정 이미지와 글자 도형의 반복 래스터화.
- 적용 수정: 재사용 target follower, 지속되는 바늘 transform/follower geometry, band/pair 전환 시에만 경고층 재작성. face와 scale을 서로 다른 정적 bitmap으로 실제 표시 크기/DPI에서 한 번 합성하며 동적 층의 앞뒤 순서를 유지한다. 크기/DPI/차량 최대 RPM 변경 시에만 정적 층을 갱신한다.
- 원본 PNG·폰트·일반/확장형·색·숫자 배치·좌우 점등·red flash 유지. 텍스트 Pen도 재사용한다. 기존 픽셀 검증은 아래 Gate에서 모두 실행했다.

## RENDER REMEDIATION

| 항목 | 반영 / 남은 한계 |
|---|---|
| SoftwareOnly | **미해결.** Default 하드웨어 및 혼합 HWND 경로를 Gate 1에서 비교했지만 확실한 개선을 입증하지 못해 채택하지 않았다. 최종은 기존 SoftwareOnly 유지. GPU→CPU readback이 남은 지연의 확정 원인이라고 단정하지 않는다. |
| Graph | 새 sample의 새 구간만 추가. 64-segment chunk, 오래된 path/Drawing Freeze, Pen/Drawing 재사용, 10초 eviction. 중복 GeometryGroup 보유 제거. resize/reset/reconnect 또는 cache가 중간 sample을 놓친 경우에만 history 복원. gap >150ms / null / ABS / stale 의미 유지. |
| Animation | RPM·핸들·Dashboard 입력은 기존 현재값에서 최신 target을 따라가는 재사용 follower. 샘플마다 새 Animation을 생성하지 않는다. 완료/숨김 시 loop 해제. 기어 변속 등 기존 일회성 연출 유지. |
| Static/Dynamic | Avante 정적 face/scale 표시 크기 raster cache, RPM follower/needle/readouts/warnings 분리. 정적 경고층은 band/pair/validity가 바뀔 때만 다시 그림. |
| Hidden Resources | 선택되지 않은 기본/개량 view의 sample 처리를 건너뛰며 gauge 전용 창에는 숨은 steering view를 생성하지 않는다. opacity=0의 콘텐츠/샘플/VR 합성 중지. 실제 OFF는 기존 창 종료/참조 해제 경로 유지. 공유 세션/이벤트 분석은 보존. |
| Image Resources | 원본은 변경하지 않음. 살아 있는 view끼리 decode 결과 공유, 정적 강한 참조를 weak cache로 교체. 경고 bitmap만 실제 사용 영역으로 crop하여 기본+3색 보유 pixel bytes 25,159,040 → 12,349,376. 원본 base decode는 확대 품질을 위해 native size 유지. 추가 정적 raster는 표시 해상도에 맞춤. 매 frame BitmapImage/Source/PNG sequence 교체 없음. |
| Gallery | 최초 표시 시 preview 작성. 숨김/최소화/닫힘 시 Image.Source 해제, 재표시 시 복원. 화면에 보이는 선택 카드의 이미지는 유지. |
| Rendering Subscription | Driving HUD 표시 수요가 있을 때만 coordinator 구독. target/scroll의 구독도 숨김/완료 시 해제. 모든 driving UI OFF이면 display history 해제. |
| Monitor VR timer | Monitor에서 15Hz VR timer 시작 안 함. 전환/중지 시 handler 및 bitmap/buffer 해제. 헤드셋 검증 아님. |

GC가 회수할 수 있도록 참조를 해제했다는 뜻이며 OFF 순간 프로세스 Working Set이 반드시 0으로 줄어든다는 뜻은 아니다. 그래프의 전체 history 재순회 제거만으로 전체 렌더 비용이 해결되었다고 보지 않는다.

## AFTER — GATE 3 최종 선택 소스

| 구성 | CPU 전체 코어 기준 % | 종료 Working Set MiB | 종료 Private MiB | allocation MiB/s | callback Hz | callback p95 ms | callback p99 ms | 33ms 초과 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| Dashboard | 0.123716 | 174.664062 | 114.160156 | 2.635061 | 60.010225 | 30.049600 | 31.072500 | 0 |
| Avante N | 2.332576 | 165.238281 | 107.332031 | 17.947478 | 60.076570 | 30.195800 | 31.100200 | 0 |
| 전체 조합 | 4.177891 | 187.621094 | 121.703125 | 41.631301 | 60.717628 | 30.082600 | 31.104800 | 4 |

현재 대조군 대비 전체 조합 CPU 3.735541% → 4.177891%, allocation 37.579971 → 41.631301MiB/s, Working Set 199.292969 → 187.621094MiB. 1회씩의 짧은 fixture 비교이므로 장시간 실제 게임 부하의 감소율로 일반화하지 않는다.

- CPU / RAM: 위 표.
- GPU: N/A — 해당 프로세스 엔진 인스턴스 없음; DWM 공용 GPU 비용의 프로세스별 분배는 미측정.
- FPS / FRAME P95 / FRAME P99: **actual presentation NOT VALIDATED**.
- 최종 전체 조합의 DWM 표면 전달: 그래프 57.644473Hz / p95 31.020996ms / p99 32.037842ms / max 47.097412ms / >33ms 8회. 페달 57.181730Hz, Avante 57.644472Hz. 이 역시 physical presentation FPS가 아니다.
- 실제 desktop 픽셀 검증: 창 `visible=true`, `cloaked=0`, session=1. DXGI `AcquireNextFrame`은 29초 동안 각 265~266회 timeout, frames=0으로 **계측 무효(exit=2)**. 0fps라고 판정하지 않았다. 계측 프로세스에만 display idle hold를 적용해도 같았다. 종료 시 해제했다. 모니터 절전이 원인이라고 확정하지 않는다.
- 설정 주사율 143Hz가 확인되어도 실제 frame 표시 성공을 뜻하지 않는다. DXGI가 프레임을 받지 못하는 원인은 미확정이다.
- [최종 원시 결과](../../harness/reports/perf-gates-20260911/gate3-final/summary.json).

## ARCHITECTURE — GATE 2

Gate 1의 지연이 남아 있어 별도 `work` harness에서 2-process 후보를 제한된 POC로 확인했다. **제품 코드에 분리를 넣지 않았다.**

| 구성 | 합산 CPU % | 합산 Working Set MiB | 합산 Private MiB | callback Hz | callback p95 / p99 ms | actual FPS |
|---|---:|---:|---:|---:|---:|---|
| Monolith | 2.986285 | 236.109375 | 159.929688 | 60.530515 | 16.342900 / 31.046600 | N/A |
| Collector + Monitor | 2.458591 | 311.472656 | 210.812500 | 60.440931 | 30.048900 / 31.115600 | N/A |

- Selected: **MONOLITH**.
- Reason: 표시 cadence 개선 없음. Private 합계 약 50.88MiB 증가. Working Set 합계는 공유 DLL 페이지를 중복 포함할 수 있으므로 물리 RAM 증가량과 동일하지 않다. CPU 계측 구간도 Collector 약 38초/Renderer 약 30초로 완전 일치하지 않아 작은 차이를 확정적인 절감으로 보지 않는다.
- GPU: 두 경로 모두 SoftwareOnly, 엔진 인스턴스 없음. 정밀 GPU 비교 미완료.
- IPC: shared memory에 QPC timestamp 8bytes만 발행하는 **최소 비용 screening**이다. 전체 Overlay model을 직렬화/전송한 POC가 아니다. source age p95/p99 = mono 7.3601/7.4177ms, split 0.4935/0.6802ms이며 publish/소비 위상도 포함한다. 순수 IPC latency로 부르지 않는다.
- Capture: 실제 Parser/ActivityCaptureRuntime/Archive 코드에 48명 raw fixture를 넣음. mono 29.998264Hz, p99 34.6642ms, samples=1025; split 29.990713Hz, p99 48.0816ms, samples=1147. 실제 SHM/네트워크 없음.
- Renderer 정상 종료 뒤 Collector sample 1087→1146(+59)로 지속 기록 확인. **강제 crash/프로세스 kill 복구 시험은 하지 않았다.** 독립 기록의 설계 가치는 있으나 이 결과만으로 분리 채택 사유로 삼지 않는다.
- VR을 별도 process로 나누는 것은 동일 원본/계약을 Collector가 소유하고 Renderer가 표시만 소비하는 설계로 검토했다. 실제 성능 PASS/FAIL 없음.

## REGRESSION

```text
=== VERIFY (20260911-231255) ===  branch=main head=85442a7c7b901907623aaecb55f4285d802e7692 dirty=True
dotnet: <SDK8.0.424> (8.0.424)
[PASS] versions — canonical version: 0.7.1  (source: Directory.Build.props)
[PASS] secrets 
[PASS] restore 
[PASS] build — errors=0 warnings=0 log=build-20260911-231255.log
[PASS] test:Client — exit=0 passed=149 total=149 failed=0 log=test-Client-20260911-231255.log
[PASS] test:Activity — exit=0 passed=111 total=111 failed=0 log=test-Activity-20260911-231255.log

=== GATE: PASS (62.7s) ===
report: harness\reports\verify-20260911-231255.md
```

| 항목 | 결과 |
|---|---|
| Build | Release errors=0 warnings=0 |
| Client | passed=149 total=149 failed=0 exit=0 |
| Activity | passed=111 total=111 failed=0 exit=0 |
| Compact/Wire | V1 byte 유지, V2 장거리, quantization, presence/null, timestamp, exact gzip HTTP 계약 회귀 PASS. 실제 서버 업로드 E2E는 NOT RUN. |
| Capture cadence | ReadTelemetry 본문 및 33.333ms full read 유지. Activity/Witness/Compact/FutureTelemetry 소스 변경 없음. |
| Upload cadence | 5초 poll 및 backoff/전송 계약 소스 변경 없음. live 전송 미실행. |
| Display cadence | 기존 distinct render frame의 fast read 본문 유지. 일반 projection 최대 20Hz 유지. OFF일 때 표시 구독만 해제. |
| 보호 파일 | assets/계약 90개 hash 비교 변경=0. 원본 이미지 제거/재생성 없음. |
| Lifetime | options=14 cycles=2 auxiliaryRemaining=0 avanteViewsAndImagesCollected=3 |

```text
PROOF incremental graph initial=2401 appended=2400 processed=4801 retained path; bounded chunks; stale/reset preserved
PROOF RPM target does not rasterize the static face again
PROOF target updates reuse target follower; inactive motion stops
PROOF distinct display frames=144 local reads=144 duplicate frames skipped; hidden HUD reads=0 recording reads=0 (fixture, not measured FPS)
```

샘플·수집·upload를 낮춰 성능을 맞추지 않았다. [parity 원문](../../harness/reports/perf-gates-20260911/parity.json).
위 출력은 SDK 로컬 경로만 `<SDK8.0.424>`로 표시했다. 숫자/결과는 그대로이며 전체 원문은 `harness/reports/perf-gates-20260911/final-verify.log`에 있다.

## 중간 실패 / 제외한 시도

논리 Gate는 0/1/2/3이다. 전체 remediation 구현 뒤 발견한 실패를 고치고 **같은 Gate를 재실행**한 기록도 보존했다. 네 번 모두 처음부터 통과했다는 의미가 아니다.

- Gate 1 최초: Client 147/149, Activity 111/111. 기존 StreamGeometry 구체 타입 가정과 opacity=0 capture 가정을 현재 자원 중지 요구에 맞게 바꾸었으며, 끝점 누락도 수정했다. 픽셀/크기/비영 투명도 기준은 유지했다. 이후 149/149,111/111.
- 최초 retained geometry aggregate는 전체 조합 allocation 111.163839MiB/s로 악화됐다. aggregate를 매번 serialize하는 경로 대신 chunk Drawing을 직접 재사용하도록 고친 뒤 재검증했다.
- GPU Default / 혼합은 명확한 개선 미입증 및 메모리 비용 때문에 제외. 최종 SoftwareOnly 해제 완료로 쓰지 않는다.
- Gate 3 native clock 실험은 p95 약 30ms가 그대로이고 CPU 이점도 없어 제외. 최초 호출은 SDK 경로 누락, 다음은 Clock 반환 타입 build error 1로 실패했다. 고친 뒤 기능 통과했지만 성능 효과 없어 제품에서 제거했다.
- 1ms timer 진단 `timeBeginPeriod` 성공(result=0)에도 전체 callback 61.111288Hz / p95 30.0448ms. 제품에 추가하지 않았고 진단 종료 시 요청 해제.
- 초기 약 16ms → 최종 약 30ms의 원인 전부를 이번 코드 변화 탓이라고 확정하지 않는다. 최종 Test-8 대조군에서도 같은 현상을 확인했다. 반대로 새 코드가 그 문제를 해결했다고도 하지 않는다.
- 최종 중복 GeometryGroup 제거 뒤 147/149가 한 번 실패했다. 내부 Curve API를 조회하던 검사와 Pen의 drawing bounds를 geometry bounds로 취급한 검사였다. 실제 화면의 retained Drawing/geometry 경계로 검사 대상을 바꾸고, 기존 .01px 허용 범위·3px 굵기·색상 조건을 유지했다. 동결된 이전 chunk의 색 변경 검사도 추가했다. 이후 최종 149/149 통과.

## VR

**CODE REVIEW ONLY / ARCHITECTURE ONLY / NOT VALIDATED**

REAL VR: **NOT AVAILABLE ON THIS PC**. Quest3, Virtual Desktop, 실제 SteamVR headset rendering을 실행하지 않았다. 기존 CPU compositor/D3D11 fixture 테스트 통과를 VR 실기기 PASS로 쓰지 않는다.

## FINAL

**YELLOW — 실제 Monitor 60fps와 frame p95/p99 목표 미달/미검증.** 기능·데이터 보호 게이트는 PASS지만 성능 목표 완료는 아니다. 꺼진 UI의 계산과 bitmap 수명, 반복 history/Animation 생성, Avante 고정 면의 CPU 비용을 줄였다. 전체 allocation과 표시 pacing은 남아 있다.

현재 채택한 구조는 Monolith다. 다음 해결 대상은 (1) 현재 desktop의 실제 표시 프레임을 측정할 수 없는 DXGI timeout 원인, (2) 동일 환경에서 남는 layered WPF 표면 전달의 30ms 간격과 GPU 경로의 실제 이익이다. 서버 Hz/Compact/원본 의미를 바꾸는 우회는 하지 않는다. 추가 게임 테스트는 사용자가 중단한 상태로 유지했다.

commit / tag / push / release / 설치본 교체: **수행하지 않음**.

최종 검증 DLL SHA256: `9DC03C38FA57CFE51FB4B9A11DC2B351F96E554765F4B8CCB01FD7446DC6AAF9`.

측정 상세는 `harness/reports/perf-gates-20260911/`(gitignored), 대형 ETL·POC 실행 도구·수정 전 파일은 기존 작업공간의 `work/perf-remediation/`에 보존했다. 계측용 POC/새 패키지는 제품 프로젝트에 추가하지 않았다.

참고한 플랫폼 동작: [Microsoft WPF MediaContext 소스](https://github.com/dotnet/wpf/blob/v8.0.0/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/MediaContext.cs), [Desktop Duplication API](https://learn.microsoft.com/en-us/windows/win32/direct3ddxgi/desktop-dup-api), [SetThreadExecutionState](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate). 플랫폼 설명은 이 PC의 병목을 자동으로 확정하는 근거가 아니다.

## 최종 기록 점검

- 문서 작성 후 secrets: `scanned=353 allowed=37 hits=0`, `SECRET CHECK: PASS`. 버전 검사 `VERSION CHECK: PASS`, 모두 0.7.1.
- 전체 Git diff 공백 검사에는 기존 AGENTS.md와 이전 docs/TASK.md 문단의 trailing whitespace가 남는다. 이번 변경의 LegacyPedalTelemetryView 공백 한 줄은 정리했다. 기존 사용자 문서는 되돌리지 않았다.
- 현재 대조군 대비 최종 전체 조합 CPU는 3.735541% → 4.177891%, allocation은 37.579971 → 41.631301MiB/s로 증가했다. 메모리는 199.292969 → 187.621094MiB로 감소했다. **이번 결과를 전체 CPU 최적화 성공이라고 쓰지 않는다.**
- 정적 면 캐시 중간 측정에서는 Avante CPU 1.425043%가 관찰됐지만 최종 측정은 2.332576%였다. 따라서 중간의 낮은 수치만 최종 절감 근거로 선택하지 않았다.
- 종료 시 OS 메모리 표본: TotalVisibleMemorySize=33477436KiB, FreePhysicalMemory=17618352KiB. 이 게임 미실행 개발 환경 표본은 실제 게임 부하의 RAM 부족 여부를 판정하지 못한다.