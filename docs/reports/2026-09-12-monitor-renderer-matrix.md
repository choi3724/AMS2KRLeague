# AMS2 v0.7.1 Monitor renderer 비교 — 2026-09-12

## 판정

**YELLOW / 성능 목표 미완료.** B(Default WPF hardware)를 현재 작업 트리에 반영했다. 전체 4개 장면의 CPU는 5.975%→4.215%, Avante 단독은 3.762%→1.559%로 감소했다. 그러나 전체 조합 allocation은 65.466→69.336MiB/s, Private Memory는 122.910→223.660MiB로 증가했다. 페달은 DWM 59.908Hz, p95 27.775ms, >33ms 6회이므로 모든 Overlay의 안정적 60fps 달성을 선언하지 않는다. 실제 물리 화면 FPS는 측정하지 않았다.

CPU 4.215%는 16논리코어 기준 약 0.674개의 논리코어 CPU 시간을 지속 소비하는 양이다. 특정 코어 하나가 67.4%로 고정됐다는 의미나 물리코어 성능 환산은 아니다. 이 측정에는 제품 View 4개와 합성 입력/계측 하네스가 포함되고 Collector/리그 수집·전송은 실행하지 않았다. 따라서 이 수치를 리그 수집 비용으로 설명할 수 없고, 오버레이 전용 비용과 하네스 비용의 정확한 차이는 별도 분리하지 않았다.

## 기준선과 보호

- HEAD `85442a7c7b901907623aaecb55f4285d802e7692`, v0.7.1, main, 기존 dirty 상태 보존. v0.7.1 태그의 순정 UI와 비교한 것이 아니라 이번 backend 작업 직전의 미커밋 remediation/Avante를 대조군으로 고정했다.
- 이전 분석: `2026-09-11-overlay-code-resource-audit.md`, `2026-09-11-overlay-performance-remediation-gates.md`.
- 기존 dirty 변경과 이번 변경은 별도 baseline 해시/파일 백업으로 구분했다. 기준 203파일 중 보호 대상 Core/Runtime/Assets 110파일의 이번 작업 전후 해시 변경 0개. 원본 이미지/폰트/설정 및 SHM/Activity/Witness/Compact/수집·기록·전송 cadence 수정 없음.
- 게임 테스트, 설치 교체, commit/tag/push/release 하지 않았다. 운영 공개본은 변경하지 않았다.

## 측정 조건

별도 `harness/renderer-matrix`에서 같은 제품 AvanteClusterView, Telemetry graph, Pedal gauge, Timing을 사용했다. Avante 454×375, graph 540×120, pedal 120×120, timing 390×150, 같은 위치/입력, 10초 graph prefill, 3초 warmup 후 각 30초 측정이다. 입력은 명목 60Hz의 절대 시간 기반 동일 합성 데이터, Timing은 기존 분리된 20Hz 갱신이다. 각 측정 구간 입력 표본 1799개. 144Hz 공통 presentation scheduler는 표시/보간용이며 SHM 또는 서버 cadence를 바꾸지 않는다.

Gate A는 네 방식에 같은 65ms RPM/35ms 핸들 보간을 적용한다. Gate B는 같은 입력 하네스 아래 이전/선택 제품 DLL의 native animation 경로를 사용한다. Gate A의 140Hz 결과를 Gate B 제품 결과로 재사용하지 않는다. 이 하네스에는 모든 종류의 Overlay와 실제 Collector 실행이 포함되지 않는다.

CPU는 프로세스 누적 CPU 시간을 wall time×16으로 나눈 machine %. WS/Private는 측정 종료 시점 값이며 peak가 아니다. GPU는 해당 PID의 관측 가능한 엔진 카운터 합계 평균으로, Task Manager의 가장 바쁜 엔진 사용률과 동일하지 않다. N/A는 카운터 미수집이며 0%가 아니다. allocation은 관리 힙 할당량/시간이고 생존 메모리 증가량이 아니다.

DWM 값은 해당 HWND의 ETW UpdateWindow 전달 간격이다. 물리 모니터 FPS나 실제 픽셀 변화 횟수로 부르지 않는다. Timing의 약 50ms 간격은 20Hz 상태 갱신에 따른 값이므로 연속 애니메이션 stall로 합산하지 않는다. ETW lost events=0. 단일 순차 실행의 30초 비교이며 장시간/실게임 부하 안정성 보증은 아니다.

## RENDER MATRIX — GATE A

A=SoftwareOnly transparent/layered, B=Default hardware transparent/layered, C=opaque Default 원인 분리용, D=D3D11+D2D+DirectComposition transparent surface POC.

| Renderer / scene | CPU % | WS MiB | Private MiB | Allocation MiB/s | GPU engine sum % | DWM Hz (Avante) | p95 ms | p99 ms | >33ms | GC 0/1/2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| all-A | 7.176 | 186.191 | 123.277 | 63.199 | N/A | 105.891 | 13.894 | 13.914 | 0.000 | 41/8/2 |
| all-B | 4.813 | 209.395 | 225.488 | 63.200 | 17.000 | 140.300 | 6.962 | 13.888 | 0.000 | 41/7/2 |
| all-C | 4.540 | 203.105 | 152.988 | 63.200 | 12.077 | 141.400 | 6.966 | 13.882 | 0.000 | 41/7/2 |
| all-D | 4.474 | 249.691 | 232.582 | 111.142 | 20.769 | N/A | N/A | N/A | N/A | 79/46/13 |

표의 DWM/p95/p99/>33ms는 Avante surface 기준이며 CPU/메모리는 장면 전체 합계다. Surface별 결과:

| Run | Surface | DWM Hz | p95 ms | p99 ms | >33ms |
|---|---|---:|---:|---:|---:|
| all-A | avante | 105.891 | 13.894 | 13.914 | 0 |
| all-A | graph | 105.924 | 13.894 | 13.924 | 0 |
| all-A | pedal | 59.928 | 27.773 | 27.793 | 9 |
| all-A | timing | 20.018 | 62.482 | 62.501 | 599 |
| all-B | avante | 140.300 | 6.962 | 13.888 | 0 |
| all-B | graph | 139.566 | 6.966 | 13.888 | 0 |
| all-B | pedal | 60.008 | 20.850 | 27.775 | 1 |
| all-B | timing | 20.004 | 55.565 | 62.497 | 599 |
| all-C | avante | 141.400 | 6.966 | 13.882 | 0 |
| all-C | graph | 142.166 | 6.967 | 13.876 | 0 |
| all-C | pedal | 60.008 | 20.846 | 20.970 | 0 |
| all-C | timing | 20.004 | 55.559 | 55.575 | 599 |

D는 WPF retained Drawing을 D2D에 번역하는 adapter를 사용하며 native 전용 renderer의 이론적 성능 상한을 평가한 것이 아니다. DirectComposition surface에서 WPF와 같은 DWM UpdateWindow 관측값은 얻지 못했다. DXGI GetFrameStatistics 변경 poll 기준 Avante 52.982Hz/p95 27.783ms/p99 41.663ms/>33ms 63회가 관측됐으나, unique polls 1589와 PresentCount delta 2146이 다르므로 실제 전달 FPS로 사용할 수 없다. Desktop Duplication frames=0 디버깅은 반복하지 않았다.

D 전체 allocation 111.142MiB/s, WS 249.691MiB, Avante 단독 CPU 2.093%로 B 단독 1.396%보다 높다. 공통 장면 번역 및 geometry 재생성 비용이 남았다. 명확한 제품 우위가 입증되지 않아 D를 제품으로 이식하지 않았다. WPF 설정창은 유지되며 새 제품 dependency/프로세스/IPC 추가 없음. Topmost/transparent/click-through native style과 hit-test 반환은 구현했지만 실제 다른 프로세스로 클릭이 전달되는 사용자 입력 E2E는 미검증이다.

## AVANTE N

Current는 이번 선택 전 native 대조군 N이며 아래 A/B/C/D는 동일 공통 보간을 사용한 별도 Gate A 결과다.

| Renderer / scene | CPU % | WS MiB | Private MiB | Allocation MiB/s | GPU engine sum % | DWM Hz (Avante) | p95 ms | p99 ms | >33ms | GC 0/1/2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| avante-A | 4.485 | 166.219 | 109.816 | 22.437 | N/A | 118.660 | 13.894 | 13.903 | 0.000 | 14/7/1 |
| avante-B | 1.396 | 191.285 | 211.812 | 22.438 | 9.769 | 143.233 | 6.959 | 6.971 | 0.000 | 14/7/1 |
| avante-C | 0.722 | 191.910 | 147.734 | 22.438 | 12.692 | 134.428 | 13.882 | 13.893 | 0.000 | 14/7/1 |
| avante-D | 2.093 | 195.699 | 189.961 | 74.519 | 19.154 | N/A | N/A | N/A | N/A | 47/27/1 |

단독 native Before/After는 아래 Gate B를 사용한다. 정적 face/scale/이미지, 바늘, RPM, 속도/기어, red-zone/LED를 보존했다. D의 정적 이미지 upload는 Avante 5회(face/scale/LED tint 포함), 핸들 1회이며 매 프레임 bitmap 교체가 아니다. White/yellow/red warmup screenshot을 남겼다. 초기 D readback을 Present 뒤에서 실행했을 때 정적 층이 빠진 캡처가 나와 Present 전 readback으로 수정했고, 수정된 source/GPU 이미지를 검토했다. 제품 디자인/원본 자산 변경은 없다. 이 결과는 실제 게임 화면 검증을 대체하지 않는다.

## SELECTED

Renderer: **B — Default WPF hardware, MONOLITH 유지.**

B가 A보다 CPU와 Avante/graph DWM 전달에서 명확하게 우세했고, C는 투명도 요구를 만족하는 제품 후보가 아니다. D는 비교 가능한 전달 관측과 allocation/단독 CPU에서 우위를 입증하지 못했다. B 채택은 모든 성능 목표 PASS 선언과 별개다.

제품 변경은 Monitor HwndSource Default 정책, 보이는 Monitor motion subscriber만 공유하는 native waitable timer 기반 presentation clock, 기존 target follower/history scroll 연결이다. 마지막 subscriber가 사라지면 timer/구독을 해제하고 stale queued dispatch를 무효화한다. source data, session, upload 계산은 clock에서 수행하지 않는다. Hardware clock을 사용할 수 없는 경로는 기존 CompositionTarget 경로를 유지한다. VR-only invisible surface는 기존 SoftwareOnly 경로를 유지한다.

기존 graph retained chunk, target motion, 숨은 View 처리 차단, VR timer 분리, 이미지 lifetime 개선을 보존했다. 이 변경은 기존 10파일 수정과 `MonitorPresentationClock.cs` 추가이며 기존 dirty 변경을 이번 변경으로 소급 분류하지 않는다. 모든 OFF/lifetime 조합의 메모리 사용량을 이번에 별도 전수 계측한 것은 아니다.

## FINAL — GATE B

N=이전 product DLL+SoftwareOnly/native animation. P=선택 product DLL+Default/native animation. 이전 DLL은 실행 전에 별도 보존했다.

| Renderer / scene | CPU % | WS MiB | Private MiB | Allocation MiB/s | GPU engine sum % | DWM Hz (Avante) | p95 ms | p99 ms | >33ms | GC 0/1/2 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---|
| all-N | 5.975 | 185.895 | 122.910 | 65.466 | N/A | 90.221 | 13.901 | 13.916 | 0.000 | 42/7/2 |
| all-P | 4.215 | 208.305 | 223.660 | 69.336 | 14.538 | 104.666 | 13.901 | 13.911 | 0.000 | 45/8/1 |
| avante-N | 3.762 | 170.957 | 114.531 | 22.589 | N/A | 113.200 | 13.897 | 13.906 | 0.000 | 14/7/1 |
| avante-P | 1.559 | 195.512 | 216.863 | 23.074 | 11.385 | 142.166 | 6.960 | 13.881 | 0.000 | 14/7/1 |

| Run | Surface | DWM Hz | p95 ms | p99 ms | >33ms |
|---|---|---:|---:|---:|---:|
| all-N | avante | 90.221 | 13.901 | 13.916 | 0 |
| all-N | graph | 90.221 | 13.902 | 13.919 | 0 |
| all-N | pedal | 60.028 | 27.778 | 27.796 | 0 |
| all-N | timing | 20.004 | 55.573 | 62.496 | 599 |
| all-P | avante | 104.666 | 13.901 | 13.911 | 0 |
| all-P | graph | 104.700 | 13.902 | 13.914 | 0 |
| all-P | pedal | 59.908 | 27.775 | 27.791 | 6 |
| all-P | timing | 20.004 | 62.487 | 62.512 | 599 |

전체 CPU 상대 감소 약 29.5%, Avante 단독 약 58.6%. 전체 WS +22.410MiB, Private +100.750MiB, allocation +3.870MiB/s다. 단독 Avante DWM은 113.200→142.166Hz, 전체 조합은 90.221→104.666Hz다. 페달의 6회 >33ms와 p95 목표 미달을 숨기지 않는다. CPU 감소만으로 자원 최적화가 완료됐다고 판정하지 않는다.

최종 비게임 환경 메모리 snapshot은 물리 메모리 33477436KiB, free 20280876KiB였다. 해당 순간 시스템 RAM 부족은 관측되지 않았으나 게임 중 RAM 여유를 증명하는 값은 아니다. Hardware 전환 후 Private 증가분을 GPU 전용 VRAM 증가량으로 동일시하지 않는다.

## 추가 원인 분석 — 전체 CPU 4% 질의

제품 수정 없이 P 전체 장면에 별도 ETW CPU profile을 수행했다. 45초 fixture 결과 CPU 4.016%, WS 214.145MiB, Private 215.805MiB, allocation 66.584MiB/s지만 profiler가 붙은 실행이므로 Gate B 수치를 대체하지 않는다. 약 30초 kernel profile, PID 19868, CPU samples=22786, EventsLost=0.

| CPU 경로 | 표본 | 전체 앱 CPU 표본 비중 |
|---|---:|---:|
| WPF render thread TID 1404 | 13737 | 60.287% |
| TID 10988 (managed 호출/geometry bounds 경로) | 8097 | 35.535% |
| CHwDisplayRenderTarget::DrawPath 포함 stack | 8114 | 35.610% |
| CShapeBase::WidenToSink 포함 stack | 5014 | 22.005% |
| TID 10988의 MilUtility_PathGeometryBounds 포함 stack | 2457 | 10.783% |

함수 행은 inclusive이며 서로 중첩된다. 합산해서 CPU를 계산하면 안 된다. 이 값은 대기 시간을 포함하는 EventPipe wall-time 비중이 아니라 PerfInfo/Sample CPU 표본이다. 따라서 **하드웨어 경로에서도 WPF 선/도형 확장, 경계 계산과 렌더 준비가 CPU 병목으로 남는 것**은 확인됐다. 모든 비용이 GPU로 이전된 상태가 아니다.

소스에서 `AvanteClusterView.Text`는 캐시된 문자 geometry를 `DrawGeometry`로 그리고, 스타일 문자에는 shadow/stroke를 적용한다. `DrawValues`는 표시값 묶음이 달라지면 gear/speed/상태 라벨을 포함한 `_values` 전체를 다시 연다. `geometry.Bounds`도 이 경로에서 조회한다. 캐시가 있어도 native WPF draw/path 계산이 사라지는 것은 아니다. 다음 최적화 후보는 해당 native 경로를 실제로 줄이는 retained value layer/문자 렌더 자원 단위의 변경이다. 단 native 표본만으로 graph/문자/개별 도형별 비용을 정확하게 배분하지 않았으므로 특정 UI 하나가 전부 원인이라고 단정하지 않는다.

관리 trace 첫 시도는 이 dotnet-trace 버전에서 지원하지 않는 `cpu-sampling` profile로 실패했다. `dotnet-sampled-thread-time`으로 수정 후 붙였지만 fixture 종료로 짧게 끝나 전체 30초 관리 CPU/할당 분석으로 사용하지 않는다. 짧은 관리 stack에 Avante SetSample→DrawValues→Text/Status가 관측됐다는 보조 증거만 남긴다. Native parser는 symbol progress stderr 때문에 shell exit=1을 반환했으나 JSONL 및 summary가 완성되고 EventsLost=0, symbol 4850/4852가 확인됐다. exit=0 검증으로 꾸미지 않았다.

이 분석은 다음 병목 경로를 좁혔지만 저자원/전체 60fps 목표를 해결한 최종 결과는 아니다. Collector가 이 fixture에서 실행되지 않았으므로 프로세스 분리가 이 CPU를 줄인다고 주장할 근거도 없다.

## REGRESSION

Gate B에서 전체 검증 1회: `harness/reports/verify-20260912-110137.md`, duration 65.5s.

```text
GATE: PASS
versions: PASS (0.7.1)
secrets: PASS
restore: PASS
build: errors=0 warnings=0
test:Client exit=0 passed=149 total=149 failed=0
test:Activity exit=0 passed=111 total=111 failed=0
PROOF shared Monitor clock delivers callbacks, stops when idle, and cancels stale dispatch; callback count is not FPS
```

Compact/Wire: 기존 exact gzip HTTP/V1 bytes/V2 precision/null presence/timestamps 회귀 PASS. Capture cadence: UNCHANGED. Upload cadence: UNCHANGED. 보호 110파일 해시 변화 0. 실서버/실게임 capture E2E 재실행은 하지 않았다.

Portable repo harness 자체 build도 errors=0 warnings=0, 2.32s, analyzer self-check PASS. 초기 하네스 compile/초기화 오류는 수정 후 실행했다. 첫 Gate A는 Timing에 같은 mutable VM을 다시 전달해 갱신되지 않는 문제가 확인돼 중단 보존하고, 새 VM 갱신으로 고친 뒤 전체 corrected matrix를 다시 실행했다. 최종 수치는 corrected 실행만 사용한다. 제품 미세 수정별 전체 Client/Activity 검증은 반복하지 않았다.

## 요구사항 수용과 남은 제한

| 요구사항 | 결과 |
|---|---|
| REQ-01 | A/B/C/D harness 구현/실행 완료. D의 native scene adapter 한계 명시 |
| REQ-02 | PARTIAL: CPU/RAM/alloc/GC/A-C DWM 측정. D 동등 DWM 관측 및 A GPU 카운터 없음 |
| REQ-03 | PARTIAL: B의 비교 우위 확인/반영. 최종 모든 UI의 60Hz/p95/자원 목표 미달 |
| REQ-04 | Avante 동일 장면 비교 및 native Before/After 완료 |
| REQ-05 | D 우위 미입증으로 제품 GPU 이식 조건 불충족; 미이식 |
| REQ-06 | MONOLITH 유지, 새 Collector/VR 프로세스 구현 없음 |

요청하지 않은 UI/기능/설정 추가 0건. 운영 승인 대기 없음. 남은 문제는 페달 전달 간격, 약 69MiB/s allocation, WPF native geometry CPU 비용과 hardware Private Memory 증가다. 제품에 반영된 선택은 테스트를 통과한 작업 트리 상태이며 배포 완료 상태가 아니다.

## VR

CODE REVIEW / ARCHITECTURE ONLY / NOT VALIDATED.
REAL VR: NOT AVAILABLE ON THIS PC. Quest3 / Virtual Desktop / 실제 SteamVR headset rendering NOT TESTED / OUT OF SCOPE. Monitor YELLOW의 이유는 VR 미검증이 아니라 위 Monitor 성능 미달이다.

## 증거와 재현

- 실행법: `harness/renderer-matrix/README.md` (Gate A/B, 이전 DLL 분리 포함).
- repo 요약/스크린샷/해시: `harness/reports/renderer-matrix-20260912/` (gitignored).
- 원본 ETL/JSONL: `C:/Users/User/Documents/Codex/2026-09-08/plugin-computer-use-openai-bundled-play-2/work/renderer-matrix/` 아래 `gate-a-corrected`, `gate-b`, `profile`.
- 이전 DLL SHA256: `9DC03C38FA57CFE51FB4B9A11DC2B351F96E554765F4B8CCB01FD7446DC6AAF9`.
- 선택 DLL SHA256: `51072116B626F62F99F9B12E8301183AEF20FC04469ACDE887F0F8797123C524`.
- 표는 가독성을 위해 소수점 3자리 반올림; 원본 `summary.json`에 전체 정밀도 보존.

DXGI 통계 해석 근거: [DXGI_FRAME_STATISTICS](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/ns-dxgi-dxgi_frame_statistics), [GetFrameStatistics 제한](https://learn.microsoft.com/en-us/windows/win32/api/dxgi/nf-dxgi-idxgiswapchain-getframestatistics). WPF drawing 추출 근거: [WPF DrawingServices source](https://raw.githubusercontent.com/dotnet/wpf/v8.0.0/src/Microsoft.DotNet.Wpf/src/PresentationCore/System/Windows/Media/DrawingServices.cs). Harness native binding: [Vortice.Windows](https://github.com/amerkoleci/Vortice.Windows).
