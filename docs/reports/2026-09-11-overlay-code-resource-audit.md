# 오버레이 리소스·애니메이션 코드 원인 분석 — 2026-09-11

## 판정과 범위

현재 병목은 하나가 아니다. **GPU readback을 우회한 소프트웨어 렌더 경로의 CPU 비용**, **고속 샘플마다 반복하는 전체 그래프 재생성**, **일부 비활성 자원의 작업/참조 유지**가 겹친다. 아래는 현행 코드에서 확인한 사실과, 이미 확보한 추적이 뒷받침하는 범위를 구분한 결과다. 특정 코드 줄이 모든 끊김의 유일한 원인이라고 단정하지 않는다.

사용자의 “게임 테스트는 그만하고 / 코드를 분석해서 원인을 찾아내” 이후 게임 조작·게임 계측·새 진단 실행을 중단하고 소스와 기존 출력만 분석했다. 마지막 게임 추적은 21:17:03에 `STOP_EXIT=0`으로 종료됐다. 게임 프로세스는 종료하지 않았다.

- 기준 HEAD: `85442a7c7b901907623aaecb55f4285d802e7692` / v0.7.1.
- 미커밋 변경 다수 보존. 아반떼 계기판과 관련 자산은 HEAD에 없는 미추적 추가 파일이다.
- 이번 후속 단계의 저장소 수정: 승인된 정확한 로컬 경로 allowlist 5개, 고주사율 보고서의 게이트 결과 정정, 이 분석 보고서. 추가 제품 코드 수정 없음.
- 보호: 기존 디자인·옵션·레이아웃, 로컬 고속 읽기, 서버 기록/전송 주기와 원본 계약.

## 코드에서 확인한 원인

| 우선순위 | 코드 위치 | 확인한 사실과 영향 |
|---|---|---|
| 높음 | `src/AMS2LeagueClient/Overlay/OverlayWindowInterop.cs:84` | `Configure`가 모든 HUD HWND에 `SoftwareOnly`를 적용한다. 투명 WPF의 GPU→CPU readback 대기를 우회했지만, 이미지 보간·도형 채우기 비용을 CPU 렌더 스레드로 옮겼다. GPU 사용 능력을 뜻하는 `RenderCapability.Tier=2`와 실제 이 창의 렌더 모드는 다르다. |
| 높음 | `Presentation/DrivingHudViews.cs:87,132`, `Presentation/LegacyPedalTelemetryView.cs:53,89`, `Presentation/PedalCurveBuilder.cs:63` | 새 샘플 참조마다 dirty가 되고, 개량 그래프는 5번, 기본 그래프는 4번 전체 이력을 순회한다. 픽셀 열 축약은 출력 도형을 줄이지만 입력 이력의 순회는 생략하지 않는다. 매 프레임 새 Pen과 Geometry도 만든다. |
| 높음 | `Presentation/AvanteClusterView.cs:197,250` | 연속 샘플마다 새 65ms DoubleAnimation을 생성하여 기존 바늘 애니메이션을 교체한다. 애니메이션의 RPM 값 변경마다 `DrawMotion`에서 gradient/sector와 경고등 그리기 명령을 다시 만든다. 경고 색·점등 칸 수가 같아도 정확한 RPM이 key에 포함돼 바늘과 경고층을 함께 갱신한다. |
| 중간 | `Presentation/SteeringWheelView.cs:30,45`, `Presentation/DrivingHudViews.cs:18,53` | 핸들은 매 샘플 애니메이션을 취소/재설정하고 `InvalidateVisual`로 숫자 FormattedText까지 다시 만든다. 페달 게이지만 사용하는 경우에도 표시하지 않는 SteeringWheelView를 생성하고 매 샘플 SetSample을 호출한다. 이 경우 회전 애니메이션은 IsVisible 검사로 실행되지 않지만 객체·샘플 처리 자체는 남는다. |
| 중간 | `Runtime/PlayerOverlayCoordinator.cs:125,300,795` | `CompositionTarget.Rendering`을 Start에서 구독하고 Dispose에서만 해제한다. 전부 꺼졌을 때 DrivingFrame은 일찍 반환하므로 SHM fast read는 멈추지만 전역 프레임 구독은 남는다. 숨김 상태의 callback 로그가 0인 이유도 측정 코드 전에 반환하기 때문이므로 실제 callback 0회의 증거가 아니다. |
| 중간 | `Runtime/PlayerOverlayCoordinator.cs:477,513,557,566` | 레이스 컨트롤·이벤트 분석과 전체 표시 모델 생성에 컴포넌트 활성 여부 검사 없음. `towerSnapshot=SequenceNumber`가 표시 키에 포함되어, 값이 같아도 새 snapshot이면 모델을 다시 만들 수 있다. 창/View만 꺼도 이 CPU 작업은 자동 중지되지 않는다. 다만 상태를 누적하는 분석은 재활성화/다른 UI와 의존성이 있으므로 단순 삭제하면 안 된다. |
| 중간 | `Overlay/OverlayWindow.Vr.cs:30,34,57`, `Vr/VrOverlayController.cs:73` | Monitor 모드도 `StartVr`에서 15Hz 타이머를 시작한다. Tick 본문은 즉시 반환해 캡처·GPU 제출은 하지 않지만 Dispatcher 호출은 계속된다. VR→Monitor 전환은 controller/runtime을 Disconnect하지만 `_vrBitmap`/`_vrPixels`는 StopVr까지 보존한다. StartVr→StopVr→StartVr 시 익명 Tick 구독이 누적되는 경로도 있다. 반복 시작이 현행 UI에서 발생했다고 입증한 것은 아니다. |
| 중간 | `Presentation/DrivingDashboardView.cs:17`, `Presentation/SteeringWheelView.cs:15`, `Presentation/ClientStatusWindow.Gallery.cs:149` | Housing/ImageBrush와 Wheel이 static 강한 참조라 View를 끈 뒤에도 디코드 이미지가 프로세스 수명 동안 남는다. 갤러리 생성은 꺼진 옵션까지 실제 View로 미리보기를 렌더하여 이 정적 자원을 로드한다. `_galleryImages`의 정적 미리보기 BitmapSource도 상태 창이 유지되는 동안 남는다. 미리보기 이미지와 실행 중 HUD 메모리는 구분해야 한다. |
| 중간 | `Overlay/OverlayWindow.xaml.cs:226,315` | 투명도 100%는 Content.Opacity=0만 설정한다. IsVisible과 Enabled는 그대로여서 고속 읽기/그림 갱신의 대상에서 제외되지 않는다. 옵션 비활성화와 완전 투명은 현재 다른 처리다. VR 전용 출력은 HWND opacity=0이 의도된 경우이므로 그것과도 구분해야 한다. |
| 진단 결함 | `Runtime/PlayerOverlayCoordinator.cs:679` | `animationWindows`는 이벤트 큐 현재 항목과 레이스 컨트롤 이력 유무 두 개만 센다. 계기판·핸들·그래프의 애니메이션 창 수가 아니며 값 0을 전체 애니메이션 정지로 해석할 수 없다. |

`Presentation/`, `Runtime/`, `Overlay/`, `Vr/`의 상대 위치는 모두 `src/AMS2LeagueClient/` 아래다.

### 수집을 높였을 때 비용이 커지는 이유

표시 샘플 주기를 S, 그래프 재생성 빈도를 R이라고 하면, 개량 그래프의 순회 횟수는 정상 10초 이력에서 대략 `5 × 10 × S × R`이다. 현재처럼 새 렌더 프레임에서 읽고 즉시 전체 그래프를 갱신하면 S와 R이 함께 증가한다. 단순 계산상 60/60Hz는 초당 약 180,000회, 144/144Hz는 약 1,036,800회의 샘플 방문이다. 이는 실제 CPU 시간 측정값이 아니라 코드 구조의 작업량이다. 샘플을 줄이는 대신 필터/새 구간을 누적 계산하고 이전 구간의 도형을 재사용해야 한다.

65ms 애니메이션을 60Hz이면 약 17ms마다, 144Hz이면 약 7ms마다 다시 시작하는 것도 같은 문제다. 수집 빈도 증가 자체가 잘못된 것이 아니라, 각 입력마다 애니메이션 객체와 과거 전체를 재구성하는 구현이 늘어난 빈도를 제대로 수용하지 못한다.

## 이미 정상적으로 처리하는 부분과 검증 누락

- `OverlayWindow.SynchronizeSurface`는 옵션 OFF에서 Hide → Content=null → Close → 참조=null로 처리한다. 비활성 보조 창을 처음부터 전부 만드는 구조는 아니다.
- `RefreshDrivingViews`는 보이는 주행 패널만 갱신한다. 아반떼/핸들/그래프는 숨김·Unloaded에서 해당 애니메이션을 중지한다.
- 아반떼 두 디자인은 이미지 배열을 공유하고 static WeakReference를 사용한다. 기존 lifetime 검사는 두 View와 이미지 배열의 GC 회수, 14개 옵션의 켜기/끄기 2회와 HWND 개수 복귀를 확인한다.
- 이 검사는 **coordinator 전역 Rendering 구독**, **Monitor 모드 VR timer**, **static Housing/Wheel**, **갤러리 bitmap 유지**, **옵션 OFF 후 표시 모델 계산 중지**를 검사하지 않는다. 앞선 자원 검증은 이 범위까지 다루지 못했다.
- 작은 정적 brush/pen/font와 선택 목록의 미리보기까지 모두 0바이트일 수는 없다. 실행 HUD의 자원 해제와 필요한 설정 화면 자원을 각각 계수해야 한다. RAM이 즉시 줄지 않는 것만으로 누수라고 판단하지 않는다.

## 기존 측정과 연결되는 범위

동시 커널/DWM/GC 추적 `20260911-205232-kernel.etl`, EventsLost=0. 게임 포커스 상실 전 trace-relative 1,000–17,000ms만 분석했다.

- WPF renderer TID 2800: CPU 15,392.8952ms / 16초 = 한 코어 약 96.2%.
- UI TID 23892: CPU 4,339.7273ms.
- renderer 표본은 이미지 bilinear interpolation, software path filling, alpha blending에 집중했다. 이 증거가 단일 UI의 비중이나 개별 함수 수정 효과까지 정량적으로 증명하지는 않는다.
- GC 정지 21회, 합 113.8659ms, 최대 15.0306ms. 그래프의 16.667ms 초과 전달 간격 354개 중 GC와 겹친 것은 12개였다. GC를 모든 지연의 주원인으로 볼 수 없다.
- 함수/스케줄링 상세 추적 자체의 부하가 있을 수 있으므로 아래 가벼운 추적과 분리했다.

가벼운 DWM/DXGI 추적 `20260911-211633-frames.etl`, EventsLost=0. 분석 21:16:34.914–21:17:02.914, 28초. 이때 호스트는 RaceFinishing 상태로 넘어간 뒤였으며 10분 정상 주행 수용 시험이 아니다.

| 신호 | 이벤트 수 | 평균 Hz | p99 간격 ms | 최대 간격 ms |
|---|---:|---:|---:|---:|
| 게임 DXGI Present | 4895 | 174.8473 | 7.6264 | 13.8668 |
| 그래프 DWM UpdateWindow | 1798 | 64.2146 | 16.1568 | 30.9111 |
| 페달 게이지 DWM UpdateWindow | 1768 | 63.1426 | 31.1809 | 47.0035 |
| 아반떼 DWM UpdateWindow | 1798 | 64.2146 | 16.1614 | 30.9061 |

페달 값/숫자가 동일하면 표시 전달이 생략될 수 있으므로 게이지의 모든 긴 간격을 프레임 드롭으로 분류하지 않는다. DWM UpdateWindow와 게임 Present는 모니터 스캔아웃/눈에 보인 고유 프레임 수와도 다르다. 이 결과는 지속 60fps PASS가 아니다.

21:14:51 별도 자원 표본: 총 RAM 33,477,436KiB, 가용 5,764,476KiB, 게임 GPU 3D 88%, DWM 4%. 이 한 표본만으로 페이징이 모든 시점에 없었다고 결론 내리지 않는다. 기존 게임+오버레이 부하와 다른 시각이므로 직접 비율 비교하지 않는다.

## 비교해 보류한 방법

게임 시험 중단 지시 전에 독립 진단 앱에서 같은 크기의 세 패널로 후보를 원본과 교대로 비교했다. 계측값은 WPF callback Hz이며 GPU Present FPS가 아니다.

| 후보 | 원본 Hz 두 구간 | 후보 Hz 두 구간 | 판정 |
|---|---|---|---|
| 정적 WPF BitmapCache | 74.00 / 74.05 | 74.26 / 71.69 | 일관된 개선 없음 |
| 원형 클립을 배경층으로 이동 | 71.07 / 72.09 | 70.38 / 71.89 | 개선 없음 |
| 그래프의 연속 수평 명령 합치기 | 73.05 / 66.37 | 71.08 / 65.09 | 할당 감소, FPS 개선 없음 |
| 배경을 한 번 실제 비트맵으로 렌더 | 67.19 / 65.52 | 63.64 / 63.59 | 개선 없음 |
| 이미지 최근접 보간 — 품질 진단용 | 67.46 / 66.99 | 62.74 / 62.84 | 채택하지 않음 |
| 1ms 타이머 요청 | 66.08 / 66.61 | 65.73 / 66.26 | 개선 없음, 요청 종료 |

비교 중 장면/시스템 부하는 시간에 따라 변하므로 행 사이 개선율을 계산하지 않는다. 모두 제품 미반영이다. 무효과인 캐시, 품질 저하, 타이머 설정을 제품에 누적하지 않았다. WPF 진단 상태에서도 `_animationRenderRate=143`, `MIL_PRESENTATION_DWM`이 나와 단순한 내부 60Hz 설정만으로 설명되지 않았다.

## 수정 우선순위

1. 활성 HUD가 없으면 프레임 구독 해제, Monitor 모드 VR 타이머 중지/프레임 버퍼 반환, 페달 게이지의 숨은 핸들 제거. 표시 분석/기록 분석의 호출자를 확인해 꺼진 항목의 불필요한 projection만 생략한다.
2. 입력 수집은 고속 유지. 그래프는 새 샘플의 필터 계산과 새 구간만 추가하고 이전 구간을 재사용한다. 바늘/핸들은 최신 목표를 갱신하는 방식으로 반복 Animation 생성과 정적 경고층 재생성을 분리한다. 경계·누락·재연결 의미를 보존한다.
3. GPU 직접 합성은 별도 경로로 검토한다. 기존 WPF GPU 경로만 복구하면 이미 확인한 readback 대기를 다시 만들 수 있다. 공유 장치/이미지를 사용하고 비활성 view의 버퍼·구독을 반환하는 구조가 필요하다. 아직 직접 합성의 제품 구현/게임 측정은 하지 않았다.
4. 지표도 실제 구독/활성 애니메이션 수, 단계별 CPU 시간, CPU 메모리와 GPU 자원을 구분하도록 바로잡아야 한다. 현재 수치는 이를 모두 보여주지 못한다.

DirectComposition 검토 근거: [Microsoft의 GPU 합성 설명](https://learn.microsoft.com/en-us/archive/msdn-magazine/2014/june/windows-with-c-high-performance-window-layering-using-the-windows-composition-engine). 임시 GPU 진단 프로젝트에 공개 Vortice 3.8.3 패키지를 복원한 단계이며 제품 csproj에는 추가하지 않았다. 사용자가 코드 원인 분석으로 지시한 뒤 GPU 시험 구현/실행도 진행하지 않았다.

## 최종 상태

- 앞서 로컬 HUD 60Hz 제한 제거는 구현·회귀·실제 60Hz 초과 읽기를 확인했다. 서버 전체 snapshot 약 30Hz와 기존 전송 정책 보존.
- 승인된 allowlist 반영 후 전체 게이트: `GATE: PASS (70.4s)`, build 오류 0/경고 0, Client 148/148, Activity 111/111. 보고서 `harness/reports/verify-20260911-205017.md`.
- 이후 제품 코드 변경 없음. 이번 코드 감사에서 찾은 항목은 **확인된 미수정 항목**이다. 실제 60fps 지속, GPU 직접 합성, VR 실장비는 미검증/미완료로 남긴다.
- 기존 보고서의 0.4초 게임/HUD 동시 정지 원인은 이 코드 감사만으로 소급 확정하지 않는다.
- commit/tag/push/release/운영 서버 변경 없음.
