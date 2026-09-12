# 2026-09-11 렌더 성능 수정 및 실게임 비교 — PARTIAL

## 16:35 추가 결과 — 관리자 추적 후 Test-7 적용

이 절이 아래 Test-6 시점 기록보다 최신이다. 현재 실행: **Test-7 PID 14044**, 게임 PID 25708. 지속적 60fps의 모든 프레임 보장은 **PARTIAL**이며, 주된 GPU 읽기 대기 원인은 확인·수정했다. 이전 절의 `관리자 승인 대기`는 15:26 당시 상태이며 현재는 승인 후 추적을 완료했다.

### 확인된 주된 지연 경로

사용자가 승인한 관리자 추적을 실행했다. Windows 10 기본 WPR는 시작에 성공했지만 저장에서 `0x80010106` COM 오류가 났다. `-skipPdbGen`도 실패했다. [Microsoft의 WPR 설명](https://devblogs.microsoft.com/performance-diagnostics/wpr-start-and-stop-commands/)에 같은 오류와 도구 버전 관련 안내가 있다. OS 재시작/드라이버 변경 대신 Windows ETW 직접 저장으로 GPU와 네이티브 CPU를 확보했다. 실패 로그도 보존했다.

- GPU: `20260911-160559-direct-gpu.etl`, 30.055718초, **EventsLost=0**.
- CPU/스케줄링: `20260911-160937-kernel.etl`, 약 31초, **EventsLost=0**. Microsoft 공개 PDB를 로컬 `work/symbols`에 받아 WPF/Direct3D 함수명을 해석했다.
- Test-6 렌더 스레드 23792의 핵심 스택:

```text
CComposition::Present
→ CRenderTargetManager::Present
→ CHwHWNDRenderTarget::Present
→ CD3DDeviceLevel1::PresentWithGDI
→ CD3DSwapChainWithSwDC::GetDC
→ CD3DSurface::ReadIntoSysMemBuffer
→ CBaseDevice::GetRenderTargetData
→ NVIDIA nvd3dumx
→ WaitForSingleObjectEx
```

이 스택에서 17,124회의 비실행 구간 합계 **23,487.260ms**를 관측했다. 이는 렌더 스레드의 실행 재개까지 걸린 시간이며 GPU 연산 시간/CPU 사용 시간과 다르다. 개별 대기는 주로 1~2ms였지만 투명 창의 GPU→시스템 메모리 복사에서 반복됐다. 별도로 WPF `WaitForDwm` 경로 4,856.358ms도 있었다.

따라서 이 구간의 주요 병목은 SHM를 읽는 작업이나 HTTP 전송이 아니라 **투명 WPF 창의 화면 전송 경로**다. `AllowsTransparency=true` 경로 자체는 기존 코드에도 있었다. 어느 과거 커밋에서 처음 실게임 임계점을 넘었는지는 같은 조건의 과거 버전 대조를 하지 않아 미확정이다. Avante 추가만을 단독 원인으로 단정하지 않는다. 이 자료는 NVIDIA 드라이버 자체의 결함을 증명하지 않는다.

### 최소 수정과 비교 실험

`OverlayWindowInterop.Configure`는 메인 타워 창과 보조 HUD 창 두 곳에서만 호출된다. 여기서 각 `HwndTarget.RenderMode`를 `SoftwareOnly`로 지정했다. 일반 설정창이나 게임의 GPU 사용을 바꾸지 않았다. 전체 렌더러/프로세스 분리, 해상도 축소, 수집/업로드 cadence 변경은 하지 않았다.

별도 진단 프로그램에서 같은 그래프·페달·Avante, 같은 크기/위치/투명도, 같은 합성 데이터로 hardware→software→hardware→software를 비교했다. 실제 게임과 Test-6는 유지했다. 각 구간 12초 중 초기 2초를 제외했다.

| 반복 | 경로 | 렌더 콜백 Hz | 값 갱신 Hz | 간격 p95 ms | 최대 ms | 전체 PC 기준 CPU % |
|---|---|---:|---:|---:|---:|---:|
| 1 | GPU 래스터 | 17.6497108483 | 15.4654097552 | 102.3907 | 176.6585 | 1.3543926134 |
| 1 | CPU 래스터 | 67.4990097895 | 59.7863551069 | 16.4652 | 25.1732 | 5.4291331572 |
| 2 | GPU 래스터 | 17.6391401836 | 15.6931529204 | 102.1212 | 137.7191 | 0.7223410854 |
| 2 | CPU 래스터 | 66.7900035342 | 59.7471500609 | 16.5812 | 21.4128 | 3.9288817663 |

증거: `composition-software.out`, `composition-software-build.log`, 현재 `work/composition-probe/Program.cs`. 콜백 빈도를 GPU 실제 표시 FPS와 동일시하지 않는다. **CPU 비용을 늘려 GPU 읽기 대기를 없앤 절충**이다. 낮은 FPS일 때의 낮은 CPU와 높은 FPS일 때의 CPU를 동일 작업량으로 비교하지 않는다.

### 실제 Test-7 실행 및 후속 추적

Test-6를 16:23:45 정상 종료했다. 중도 종료 구간은 CLIENT_STOP/Aborted·MidSession으로 보존했다. 종료 출력: `attempts=2 batches=99879 dropped=0 chunks=68 archiveDropped=0 failures=0`. archiveDropped=0은 SHM 누락이 없다는 뜻이 아니다.

Test-7을 `--updates-disabled`로 실행했다. 첫 실제 게임 구간(16:24:20~16:25:00)은 렌더 콜백 **64.8~87.4Hz**, 값 갱신 **57.3~59.2Hz**였다. 최대 간격은 각 10초 구간에서 **33.7 / 30.1 / 213.9 / 20.7ms**였다. 213.9ms 단일 긴 간격의 원인은 이 로그만으로 확정하지 않았다.

16:25:00에 게임이 INGAMEMENUTIMETICKING으로 전환됐다. 메뉴 이후 표본은 주행 비교에서 제외했다. 게임 캡처 도구는 `SetIsBorderRequired 0x80004002`로 실패했고, 사용자가 주행 복귀를 알려준 뒤 후속 추적을 실행했다.

- GPU: `20260911-162721-direct-gpu.etl`, 약 30초, **EventsLost=0**.
- CPU: `20260911-162818-kernel.etl`, 약 30초, **EventsLost=0**.
- Test-7 렌더 스레드의 **GetRenderTargetData/ReadIntoSysMemBuffer 대기 스택은 관측되지 않았다**.
- DWM이 받은 창 갱신 이벤트: 두 HWND 각각 1,986회/30.068초 ≈ **66.05Hz**. 다른 창은 값 변화/표시 정책에 따라 더 낮다. HWND별 이벤트는 화면에 새 내용이 전달된 기록이며, 모니터의 실제 스캔아웃 전체 FPS가 아니다. `gpu-comparison.jsonl`에 전체 원시 집계를 남겼다.
- 과거 GPU 추적에서는 게임 Present 호출 5,510회/약30초, 후속에는 3,445회였다. 주행 상황·게임 상태가 달라 이 차이를 Test-7 변경의 게임 FPS 영향으로 단정하지 않는다. 동일 상황의 반복 주행 대조는 남아 있다.
- 후속 CPU 실행 시간 합계: **18,398.4598ms**, UI+렌더 스레드: **15,855.1018ms (86.176245%)**. 나머지 모든 스레드: **2,543.3580ms**. 나머지는 수집·저장·업로드·런타임 등이 섞인 값이며 순수 HTTP CPU로 표기하지 않는다.
- 렌더 스레드 실행 시간 10,351.4598ms의 주요 샘플은 `CSoftwareRasterizer::FillPath`, `DrawPath`, 도형 안티앨리어싱이었다. 주 스레드 5,503.6420ms에는 도형 stroke/bounds 계산, 글자 처리 등이 있었다. 남은 큰 비용은 CPU 도형 처리다.
- 렌더 스레드의 `WaitForDwm` 비실행 시간 합계 18,152.083ms, 2,037회, 최대 14.652ms. 이는 CPU 점유가 아니라 화면 동기화 대기다.

### CPU / GPU / RAM, 수집 분리 판단

30초 단위가 완전히 동일한 장면은 아니므로 아래는 순차 실측이며 완벽한 같은 장면 A/B가 아니다. CPU %는 16개 논리 프로세서로 나눈 전체 PC 기준이다. RAM은 private working set이다.

| 구간 | 표본 | Client 평균 CPU % | private WS MiB | Client GPU 전용 MiB(마지막) | 시스템 가용 RAM 최저 MiB |
|---|---:|---:|---:|---:|---:|
| Test-6 16:22:43~16:23:11 | 10 | 2.55625 | 231.336~251.078 | 125.5117 | 5152 |
| Test-7 16:24:23~16:24:58 | 16 | 3.8671875 | 217.781~238.945 | 18.0977 | 5324 |

Test-7 해당 구간에서는 Client의 양수 GPU 엔진 사용 표본이 없었다. 이것은 GPU를 전혀 사용하지 않는다는 뜻이 아니다. 설정창의 GPU 자원과 Windows DWM 합성은 남는다. DWM 평균 CPU는 같은 순차 구간에서 4.11875%→2.19921875%였다. 두 구간 모두 시스템 page-in이 0이 아닌 표본 3개가 있어 모든 I/O 지연을 배제하지는 않는다. 다만 5GiB 이상의 가용 RAM이 있어 이때 RAM 고갈은 관측되지 않았다.

이전 모든 HUD OFF 표본의 약 0.24%는 수집만의 비용이 아니라 창 관리자·수집·전송·런타임이 섞인 잔여 프로세스 비용이다. 현재 스택상 대부분의 CPU는 UI/렌더다. **리그 수집을 다른 프로세스로 옮기는 것만으로 GPU 읽기 대기나 도형 래스터 비용은 사라지지 않는다.** 프로세스 분리는 이번 수정에 도입하지 않았다. 정확한 순수 수집/인코딩/HTTP 별 CPU 분할은 별도 함수 계측이 더 필요하다.

### 검증 / 보존 / 남은 제한

```text
VERIFY_EXIT=0
Release build: warnings 0 / errors 0
RESULT: 148 passed, 0 failed, 148 total
RESULT: 111 passed, 0 failed, 111 total (3523 ms)
PACKAGE_EXIT=0
PACKAGE_SMOKE_EXIT=0
CAPTURE_FILES=19
```

- 최종 게이트: `harness/reports/software-overlay-gate4.log`. 첫 세 번의 실패 로그도 보존했다. 새 렌더 모드 검사가 HWND 생성 전/시각 트리 연결 전을 조회했고, 이후에는 검사 자체가 보관한 source 참조가 GC 검사에 남았다. HWND 조회와 별도의 NoInlining 검사 범위로 바로잡았다. 수명 검사의 14개 옵션·2회 반복·자원 해제 조건을 완화하지 않았다.
- 변경: `src/AMS2LeagueClient/Overlay/OverlayWindowInterop.cs`, `tests/AMS2LeagueClient.Tests/OverlayLifetimeTests.cs`, 이 보고서. 이전 작업의 미커밋 변경도 보존했다.
- Test-7: `work/Avante-Cluster-Test-7/AMS2LeagueClient.exe --updates-disabled`.
- EXE SHA256: `5E19FDC8692C3811801D00A11E584B672D311BA6D987E9A89C07BD5F1349E3CA`.
- DLL SHA256: `DD55D6F328BDC9C82DB7ABDF8FE8FAECAB0BC11F0A999A4426249354E1478794`.
- 설정 파일 SHA256은 전후 동일: `D2865650FBC246AE8AA5944F28CD4CCACD7462E477AB299D88536EC493EB7401`. 9개 ON / 5개 OFF 및 모든 위치·크기·투명도 유지.
- 수집 Hz, 서버 전송 cadence/내용, SHM 일관성 검사, 차량 거리 계산을 이 추가 수정에서 변경하지 않았다.
- 16:32:04/14의 최근 표본: shmHz=29.7/30.1, renderCallbackHz=64.7/64.5, drivingHz=60.3/59.3, cpu=3.097/2.991%, 최대 콜백 간격=21.3/18.7ms. 이전 주행 복귀 직후에는 53~65Hz·50ms대 간격도 있었으므로 전 구간 60fps 보장으로 기록하지 않는다.
- `wpfRenderTier=2`는 하드웨어 기능 등급이며, 이번 투명 창의 CPU 래스터 설정을 나타내지 않는다.
- VR 실장비, 긴 연속 주행의 모든 프레임, 과거 11:27 SHM sequence 붕괴의 정확한 원인, 213.9ms 단일 간격, 동일 장면에서의 게임 FPS 영향은 미확정/미검증이다.
- 공개 commit/tag/push/release, 게임/드라이버/원격 서비스 변경은 하지 않았다.

결론: **주된 투명 HUD GPU 읽기 대기를 실측으로 확인하고 Test-7에서 제거했다. 실게임 표시 빈도는 크게 개선됐지만 모든 프레임 60fps와 이전 데이터 누락 전체 해결은 아직 PARTIAL이다.**

### 마지막 안정 구간 자원 표본 (16:34:19~16:34:48)

추적 도구 종료 후 14개 표본: Client(수집·전송 포함) 평균 CPU **4.0848214286%**, 최대 단일 표본 11.3125%, private working set **292.26171875~301.296875MiB**, 시스템 가용 RAM 최저 **5951MiB**. Client GPU 엔진의 양수 표본은 없었고, GPU 전용 할당은 마지막 표본 **18.09375MiB**, 공유 할당 **1.94140625MiB**였다. 출처: `20260911-163419-resources.jsonl`.

16:37에 Test-7/게임 모두 Responding=True. 최근 두 구간 renderCallbackHz=64.7/64.4, drivingHz=59.4/56.4, shmHz=30.6/29.7, 최대 콜백 간격=22.0/24.4ms. 일시적 fast-read 거절과 콜백 간격 때문에 표시 값 갱신이 매 구간 정확히 60Hz인 것은 아니다. 사용자에게 실제 체감 확인을 요청했으며, 아직 응답하지 않은 상태는 시각 검증 PASS로 처리하지 않았다.
---

## 아래는 관리자 추적 전 Test-6 시점 기록


## 기준선

- 저장소: `E:\AMS2 KRLEAGUE\AMS2KRLeague`, HEAD `85442a7c7b901907623aaecb55f4285d802e7692` (main / v0.7.1).
- Test-4 → Test-5 → Test-6를 별도 폴더로 만들었다. 현재 Test-6 PID 7792, AMS2AVX PID 25708.
- 수정 전 verify: Client 147 passed / 0 failed / 147 total, Activity 111 passed / 0 failed / 111 total. `harness/reports/avante-performance-gate.log`.
- 기존 AGENTS.md, PROJECT.md, docs/TASK.md, TASK.md 및 앞선 Avante/투명도/업데이트 안내 변경을 보존했다. 커밋·태그·push·공개 릴리스는 하지 않았다.
- 보호 대상: 기존 디자인·좌표·폰트·투명도, 실제 값과 누락 상태, 수집/전송 주기, 원본·Compact 형식, 차량 거리 계산, 기존 저장 파일.

## 이번에 수정한 내용

### 반복 렌더링 비용 — DONE (코드 및 로컬 회귀), 실게임 60fps는 미달

`AvanteClusterView.cs`: 고정 눈금·단위를 별도 유지, 바늘 형상과 회전 Transform 재사용, 글자별 형상 및 고정 문구 폭 캐시, 표시 내용이 같으면 숫자 DrawingVisual 재생성 생략. 회전/경고색/깜빡임 정책과 원본 데이터는 유지했다.

`DrivingHudViews.cs`: 페달 게이지의 숫자/채널 글자를 반복 FormattedText 대신 재사용하는 고정 Geometry로 그린다. 실제 막대 값과 색은 기존대로 갱신한다.

수정 전 30초 함수 추적에서 DrawValues 경로 약 2,462ms, PedalGraph.OnRender 약 1,658ms가 관측됐다. Test-5의 30초 추적에서는 PedalGraph.OnRender 약 194ms였고 DrawValues는 주요 비용 목록에서 빠졌다. **이는 샘플링된 경과 시간이며 CPU 시간/FPS가 아니다.** PedalGraph는 그래프와 게이지가 공유하는 클래스다. 원래 글자 렌더링 비용은 게이지 분기에서 발생했다.

같은 표시값 500회 갱신의 할당량은 160,000 bytes였다. 누락 표시 및 값 변경 시 픽셀 변화 검사도 유지했다.

### 꺼진 HUD 자원 해제 — DONE (수명 검사)

`OverlayWindow.xaml.cs/.xaml/.Vr.cs`: 켜진 구성만 생성하고 꺼질 때 보조 창을 닫고 뷰 참조를 비운다. 순위 타워의 콘텐츠와 선택하지 않은 텔레메트리 디자인도 해제한다. 재활성화 시 현재 값·스타일·레이아웃을 새 뷰에 적용한다. 닫힌 뷰의 갱신을 중단한다.

Avante의 큰 이미지 묶음은 활성 뷰끼리 공유하되 정적 강한 참조로 영구 보유하지 않는다. 원본 PNG는 스트림에서 OnLoad로 읽는다. VR 종료 시 마지막 합성 버퍼도 비운다.

실제 검사 출력:

```text
PROOF lifetime options=14 cycles=2 auxiliaryRemaining=0 avanteViewsAndImagesCollected=3
PASS Disabled overlays release windows views and image assets
```

14개 옵션의 켬/끔을 두 번씩 검사했다. 마지막 숫자 3은 일반형 뷰·확장형 뷰·공유 이미지 배열의 약한 참조가 회수됐다는 뜻이다. OS/WPF 메모리 캐시·메인 선택 화면의 썸네일·설정까지 즉시 0 byte가 된다는 주장은 아니다.

### 렌더 지연 계측 — DONE

`PlayerOverlayCoordinator.cs`: WPF RenderingTime이 같은 중복 콜백을 제외하고 콜백 빈도/최대 간격, 표시 처리 최대 시간, 읽기 잠금 경합/거절 횟수, WPF render tier를 별도 로그에 기록한다.

- `shmHz`: 전체 읽기 성공률, 목표 30Hz.
- `uiHz`: 세션/타워 모델 갱신 횟수, 상한 20Hz.
- `drivingHz`: 빠른 주행 표시값 갱신 횟수, 상한 60Hz.
- `renderCallbackHz`: WPF가 UI 스레드의 렌더 콜백을 호출한 빈도. **GPU present FPS나 모니터 주사율이 아니다.**
- `wpfRenderTier=2`: WPF 하드웨어 렌더링 지원 수준. 모든 패스의 GPU 실행이나 실제 프레임률을 증명하지 않는다.

## CPU / GPU / 메모리 실측

Windows 성능 카운터의 프로세스 CPU를 16개 논리 CPU 전체 기준으로 환산했다. 약 2~3초 간격 표본이다. 아래 구간은 Test-6의 같은 싱글 레이스에서 UI 구성만 바꿨다. 포커스 변경으로 게임이 잠깐 일시정지하는 구간은 별도 전환이며 자연 발생 끊김으로 세지 않았다.

| 구성 | CPU 평균 | CPU 표본 범위 | 클라이언트 GPU 양수 표본 | 최소 가용 RAM |
|---|---:|---:|---:|---:|
| 원래 구성, 14:59:43~15:00:22 | 1.4102% | 0~4.8125% | 1% | 4,177 MB |
| 모두 끔, 15:02:58~15:03:27 | 0.2411% | 0~2.1250% | 없음 | 4,037 MB |
| 페달 게이지만, 15:04:29~15:04:59 | 0.5402% | 0~1.7500% | 1% | 4,258 MB |
| Avante만, 15:05:28~15:05:57 | 1.3929% | 0.3125~5.6250% | 1% | 4,297 MB |
| 그래프만, 15:07:16~15:07:46 | 1.6116% | 0~5.2500% | 1% | 4,281 MB |
| 주행 패널 세 개, 15:09:17~15:09:44 | 1.0288% | 0~3.0000% | 1% | 4,584 MB |

GPU 카운터는 양수만 기록하며 낮은 사용량이 반올림/미보고될 수 있다. 양수 표본 없음은 GPU 사용 0의 증명이 아니다. 레이스 진행/백그라운드 부하와 GC 상태가 동일하지 않아 이 평균을 단순 뺄셈한 값을 정확한 모듈별 CPU 비용으로 사용하지 않는다. 모두 끔에도 수집·전송 외 프로세스/창 확인과 모델 처리 등이 남는다.

원래 구성 구간의 게임 CPU 평균은 26.3633%, 게임 3D 엔진은 78~81%, DWM은 CPU 평균 3.1133%/3D 엔진 9~10%였다. 클라이언트 전용 GPU 메모리 마지막 표본은 72.293 MiB, 공유 GPU 메모리 12.770 MiB였다. 클라이언트 private working set은 203.004~231.590 MiB였다. 시스템 페이지 유입은 16개 표본 모두 0이었다. **이 구간에서 RAM 고갈/페이지 인이 끊김의 주원인이라는 근거는 없다.**

과거 24~36% 부하의 Chrome 원격 호스트와 이번 클라이언트 수치를 혼동하지 않았다. 원격 서비스는 종료하지 않았다. 15:01 이후 별도 단일 표본에서 remoting_host PID 6220은 전체 CPU 환산 3.125%였다. 프로세스 존재만으로 실제 원격 연결 여부를 단정하지 않는다.

## GC 정지

함수 샘플링의 SuspendOther를 제외하고, GC 전용 추적의 SuspendForGC/GCPrep부터 재개까지를 측정했다.

| 실행본 / 구간 길이 | GC Start | 정지 횟수 | 정지 합계 | 최대 정지 | 16.667ms 초과 |
|---|---:|---:|---:|---:|---:|
| Test-4 / 20초 | 19 | 20 | 216.7665ms | 48.5409ms | 3 |
| Test-5 / 30초 | 20 | 25 | 127.5383ms | 19.8848ms | 2 |
| Test-6 / 30초 | 19 | 24 | 123.6469ms | 17.0491ms | 1 |

GC가 프레임 예산을 넘는 관리 코드 정지를 일으킨 것은 확인됐다. 변경 후 줄어든 관측은 있으나 이것만으로 모든 지연이나 60fps 달성을 설명하지 않는다.

## 남은 끊김: 확인된 범위와 반증

Test-6 원래 구성에서 shmHz는 약 29~30, renderCallbackHz는 약 24~27, 최대 콜백 간격은 약 140~188ms였다. 표시 읽기/뷰 갱신 최대 시간은 대부분 0.2ms이고 읽기 잠금 경합은 10초당 0~1회였다. 따라서 계속되는 낮은 콜백 빈도를 빠른 읽기 잠금 대기로 설명할 수 없다.

단독 표시에서는 페달 게이지 약 56~58Hz, Avante 약 52~57Hz, 그래프 약 55~57Hz였다. 세 개 동시 표시에서는 약 24~26Hz로 떨어졌다. 표시를 분리·추가한 이후의 회귀와 일치하는 구체적인 재현 조건이다. 그러나 **창 개수 자체가 유일한 원인이라고 확정하지 않았다.**

프로덕션 수정 없이 독립 프로브에서 동일 패널/좌표를 비교했다(합성 데이터, 게임은 계속 실행):

| 조건 | 첫 반복 콜백 Hz / p95 | 둘째 반복 콜백 Hz / p95 |
|---|---:|---:|
| 투명 창 세 개 | 18.9292 / 93.7719ms | 20.5647 / 102.3943ms |
| 하나의 Canvas 창 | 13.0886 / 152.2447ms | 14.2507 / 171.6883ms |

단일 Canvas는 더 느렸다. 창을 합치는 변경은 제품에 적용하지 않았다. 더 넓어진 렌더 면적/합성 경로의 영향을 분리해야 한다. 불투명 창 대조 실험은 약 3.16Hz였으나 실제 표시/occlusion 상태를 화면으로 검증하지 못해 유효한 해결책 비교로 채택하지 않았다.

**GPU present/합성 대기 원인 확정은 미완료다.** Windows 화면 캡처 도구는 `SetIsBorderRequired ... 0x80004002` 오류로 실패했다. WPR는 시스템 프로파일링 정책 활성화에 실패했다. 현재 토큰은 `Administrator=False`, SeSystemProfilePrivilege가 없으며 `WPR is not recording`을 확인했다. 관리자용 30초 추적 스크립트를 준비하고 사용자 승인을 요청했다.

## 리그 수집/전송 분리 판단

수집/업로드 작업은 이미 UI와 별도 작업 경로를 사용하지만 같은 프로세스/GC 힙을 공유한다. 별도 프로세스는 장애/GC 격리에는 도움이 될 수 있어도 런타임·IPC·복사 비용이 추가되므로 자원 절약을 보장하지 않는다. 현재 근거로는 프로세스를 분리한다고 렌더 콜백 지연이 해결된다고 말할 수 없다.

업로드 루프에서 대기열 ScanInternal/LoadItem/GetDueBatch의 반복 파일 접근도 관측했다. 함수 샘플에는 I/O 대기가 포함된다. 이를 순수 HTTP 전송 CPU로 계산하거나 최적화 목적으로 원본 검사/저장·전송 정책을 줄이지 않았다. 정확한 수집 CPU와 전송 CPU/공유 힙 메모리의 완전 분할은 미완료다.

11:27경의 전체 읽기 0~1Hz 붕괴는 기존 로그의 sequence 불일치까지 확인됐지만 홀수 지속/복사 중 변경 중 어느 경우인지 여전히 미확정이다. 검사 완화·가짜 값 대입·전송률 변경은 하지 않았다.

## 최종 게이트와 실행 상태

```text
VERIFY_EXIT=0
RESULT: 148 passed, 0 failed, 148 total
RESULT: 111 passed, 0 failed, 111 total (4171 ms)
PACKAGE_EXIT=0
PACKAGE_SMOKE_EXIT=0
CAPTURE_FILES=19
LAYOUT_DIFFERENCES=
```

- 최종 전체 게이트: `harness/reports/lifetime-gate5.log`. 빌드 경고/오류 0.
- 도중 그래프 타이밍 검사가 실패했다. 렌더 콜백을 측정 구간 내내 유지하고 두 그림을 먼저 캡처한 후 픽셀 분석하도록 고쳤다. 실제 표시되는 창으로 검사하며 기존 이동량 범위(8~30px)와 재렌더링 횟수 제한은 유지했다. 측정 구간의 실제 렌더 시간도 출력한다.
- 꺼진 창을 계속 재사용한다고 가정하던 검사는 재생성된 창을 확인하도록 변경했다. 13개 패널 크기 검사는 13개를 명시적으로 켜서 전부 유지했다.
- 중간 레이스 업로드 테스트가 한 번 실패했고 같은 제품 소스의 후속 전체 게이트는 통과했다. 해당 실패의 정확한 원인은 확정하지 않았다. 중간 실패 로그를 보존했다.
- 전체 `git diff --check`에는 기존 AGENTS.md/docs/TASK.md의 trailing whitespace가 남아 있다. 해당 사용자 변경은 건드리지 않았다.
- 실제 VR 헤드셋/SteamVR 주행 검증은 하지 않았다. 로컬 D3D11 텍스처/VR 합성 회귀 통과를 실제 VR 검증으로 표시하지 않는다.
- Test-6: `work/Avante-Cluster-Test-6/AMS2LeagueClient.exe --updates-disabled`. 기존 버전/설치본과 테스트 폴더를 삭제하지 않았다.
- EXE SHA256: `5E19FDC8692C3811801D00A11E584B672D311BA6D987E9A89C07BD5F1349E3CA`
- DLL SHA256: `D98AA54ABE3F7999A1DC181BCA710C0CB6C5F2F3B1CD0DFDBDAA03EE3DCB82D9`
- Test-5 정상 종료 후 Test-6로 교체했다. 중도 종료된 레이스 기록은 CLIENT_STOP/Aborted·PARTIAL로 남았고 지우지 않았다. archiveDropped=0은 전체 SHM 누락 0을 뜻하지 않는다.
- 마지막에 사용자 설정 전체를 비교해 차이 없음을 확인했다. 켜짐 9개/꺼짐 5개, 위치·크기·색상·투명도 복원. 게임 포커스도 복원했다.

## 증거 위치 / 승인 대기

추적·측정·프로브·설정 백업의 기준 폴더:
`C:\Users\User\Documents\Codex\2026-09-08\plugin-computer-use-openai-bundled-play-2\work\live-performance`

- `test6-live.log`, `test6-resource-summary.jsonl`, 각 `*-resources.jsonl`
- `restored-gc-only.nettrace`, `test5-gc-only.nettrace`, `test6-gc-only.nettrace`
- `test5-stacks.nettrace`, `test5-stacks.txt`, `test5-status-paused-stacks.txt`
- `composition-probe.out`, `composition-alpha.out`, 각 프로브 소스/빌드 로그
- `layout-before-test6.json`, `package-render6.log`
- `capture-gpu-admin.ps1`: 기존 WPR 세션이 있으면 건드리지 않고 중단한다. 승인 후 관리자 권한으로 30초 GeneralProfile.Light + GPU.Light를 수집하고 자체 세션을 종료해 ETL로 저장한다.

최종 판정: **반복 렌더링 비용과 비활성 자원 보유 수정은 검증·Test-6 실행까지 완료. 지속적인 60fps 및 이전 sequence 붕괴의 완전 해결은 PARTIAL. 관리자 GPU 추적 승인 대기.**

최종 생존 확인(15:26): Test-6 PID 7792와 AMS2 PID 25708 모두 Responding=True. 최신 표본 shmHz=29.9, renderCallbackHz=18.0, drivingWorkMaxMs=0.1, drivingReadBusy=0. 렌더 지연은 남아 있다. WPR is not recording. 원래 구성 복원 후 값이며 앞선 14:59 구간 평균과 구분한다.

