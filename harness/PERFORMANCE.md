# 안정화 성능 측정 하네스

저장소 루트에서 실행한다. Windows / .NET SDK 8.0.424가 필요하다. 게임, 헤드셋, 서버에 연결하지 않는 합성 검사다. 결과와 임시 파일은 Git에서 제외된 `harness/reports/` 아래에 생성한다.

```powershell
$sdk = 'C:\Users\User\Documents\Codex\2026-08-25\files-pasted-by-the-user-2026\outputs\AMS2KRLeague\work\dotnet8\dotnet.exe'
& $sdk run --project harness/render-perf/RenderBench.csproj -c Release -- harness/reports/perf-desktop.json
& $sdk run --project harness/render-perf/RenderBench.csproj -c Release --no-build -- harness/reports/perf-vr.json --vr
& $sdk run --project harness/archive-perf/ArchiveBench.csproj -c Release -- harness/reports/archive-300s.json 300
& $sdk run --project harness/archive-perf/ArchiveBench.csproj -c Release --no-build -- harness/reports/archive-60s.json 60
& $sdk run --project harness/archive-perf/ArchiveBench.csproj -c Release --no-build -- harness/reports/archive-30s.json 30
```

빌드나 다른 성능 측정을 동시에 실행하지 않는다. 성공 종료 코드와 JSON의 `mode`, `chunkSeconds`를 확인한다. `--vr`를 생략하면 파일 이름에 vr가 있어도 데스크톱 검사다. `OFFSCREEN_VR_CAPTURE_NO_HEADSET` 결과만 VR 캡처 비교에 사용한다.

## RenderBench

- 32대 고정 순위 데이터, 개량 타워·개량 그래프·페달·계기판 표시. 기본 변형·독립 속도·기어 숨김.
- WPF 창은 화면 밖, 1920×1080 / 96dpi. 5초 준비 후 30초 측정.
- 합성 입력 생성 목표 100Hz. 생산된 최신 값을 60Hz 목표 `DispatcherTimer`가 읽고 HUD를 갱신한다. 별도 로그 생산 부하가 있다.
- VR 옵션은 동일 UI 처리 경로에서 15Hz 목표로 bitmap 캡처한다. 실제 SteamVR 제출은 하지 않는다.
- `dispatcherIntervalMs`: 합성 Timer 처리 시작 사이 시간. `dispatcherWorkMs`: HUD 갱신 및 해당 tick의 VR 캡처 처리 시간. 실제 production `CompositionTarget.Rendering` 스케줄러를 그대로 실행한 측정이 아니다.
- `renderingCallbacks`: WPF 이벤트 횟수이며 화면에 표시된 프레임 수가 아니다. `syntheticCoalesced`는 소비 전에 대체된 합성 최신 값 수이며 실제 SHM 누락 수가 아니다.
- 수집 Hz, 기록 표본 간격, HTTP 요청 빈도, 화면 갱신 목표, 모니터 주사율, presentation FPS는 각각 별개다. 모니터 주사율 및 실게임 FPS를 이 하네스로 판정하지 않는다.
- VR compose / RenderTargetBitmap / CopyPixels / RGBA 변환을 각각 기록한다. 실제 Submit 시간은 제품의 `VrOverlayController.LastSubmissionMs`에 계측점이 있으나 헤드셋 검증은 별도다.

기준 비교는 원본 HEAD를 별도 디렉터리에 `git archive`로 복사해 동일 하네스를 실행했다. 기준 코드에는 VR 단계별 stopwatch 계측만 적용했다. 작업 트리를 reset/checkout하지 않았다. 새로운 기준 비교도 이 방식으로 독립 복사본을 사용하며, 현재 소스를 기준 디렉터리에 덮어쓰지 않는다.

## ArchiveBench

- 60분 / 32대 / 72,000개 프레임을 빠르게 공급한다. 20Hz 합성 source, 기존 replay gate/500ms world 및 private driver 경로 사용.
- 사고·전체 세션 metadata·네트워크·실제 SHM은 포함하지 않는다. 실제 R4 경기 용량 예측 도구가 아니다.
- 가속 공급 때문에 생기는 인위적인 input overflow를 피하려고 fixture에서만 대기열 깊이를 제한한다. 제품 cadence와 기본값은 변경하지 않는다.
- 실제 `LocalDurableTelemetryArchive`와 `CompactTelemetryChunkStore`를 호출한다. 내부 생성자·Commit은 reflection으로 연결하므로 내부 구조 변경 시 실패할 수 있다. 검사용 공개 API는 추가하지 않았다.
- `encodingAndCommitThreadCpuMs`: 인코딩과 파일 commit을 실행한 스레드의 CPU 시간 합계. 순수 인코더 CPU만 측정한 값이 아니다.
- `peakSampledWorkingSetBytes`: 1,000프레임마다 관측한 최대 프로세스 working set. OS가 측정한 정확한 전체 실행 최고치는 아니다.
- `commitCalls`, `localFiles`, gzip 파일 수는 서로 다르며 OS WriteFile 호출 수를 뜻하지 않는다.
- nominal 미커밋 시간은 chunk 길이다. I/O 장애·재시도·백로그는 이 시간을 초과할 수 있다. 실제 전원 차단·taskkill 검사는 수행하지 않는다.
- 기본 chunk 길이 변경 시 gzip/HTTP chunk 수도 증가한다. 이 하네스 실행은 제품 기본값 변경이나 전송 정책 변경을 승인하지 않는다.

현재 검증 결과와 제한은 `docs/reports/2026-09-10-reliability-render-remediation.md`를 참조한다.
