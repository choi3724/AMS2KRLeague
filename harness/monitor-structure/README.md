# Monitor 구조/할당 진단 하네스

제품 Collector를 실행하지 않는 별도 net8.0-windows WPF/D3D 진단 프로그램이다. 제품 및 설치 파일 변경/게임 조작 없음.

## 준비

1. 현재 제품 Release DLL을 별도 baseline 디렉터리에 보존한다. 기존 dirty 소스/자산 해시도 기록한다.
2. `dotnet build harness/monitor-structure/matrix.csproj -c Release -p:ClientBin=<보존 DLL 폴더>`로 하네스를 빌드한다. Vortice 3.8.3은 기존 renderer-matrix와 동일한 harness-only dependency다.
3. runner 폴더를 Before/After로 분리하고 각각 올바른 Client/Core DLL을 명시적으로 복사한다. 모든 실행 직전 SHA256을 확인한다. MSBuild의 기존 출력 폴더가 기대 DLL을 포함한다고 추측하지 않는다.
4. 결과는 gitignored `harness/reports/monitor-structure/<새 이름>/`에 저장한다. 기존 결과 폴더를 덮어쓰지 않는다.

## 실행

`dotnet <runner>/matrix.dll <mode> <scene> <seconds> <PNG 폴더> <diagnostic> <presentation Hz>`

- mode P: 제품 native target motion. mode B: 비교용 공통 65ms RPM/35ms 핸들 보간. P 제품 시계는 현재144Hz이며 마지막 숫자는 외부 하네스 scheduler다. B 60/120/144Hz 비교와 native P Before/After를 혼용하지 않는다.
- scene all: Avante454×375, graph540×120, pedal120×120, timing390×150. 입력60Hz/Timing20Hz/10초prefill/3초warmup.
- diagnostic: full, harness, models, static, blocked, text, graph, effects.
- harness/models는 View/이미지 없이 스케줄러 또는 입력/history만 실행하는 추가 대조다.
- blocked는4개HUD를 정지 상태로 유지하고 입력/history/Timing model 갱신은 계속하면서 View 전달만 차단한다.
- text/graph/effects는 표시 제외 진단이다. 관리 모델/자원 생성까지 전부 제거하는 경우와 구분한다. effects는 opacity 대신 empty clip으로 고정한다. 제품 기능 삭제 아님.
- `P plot 30 <PNG>`: 개선 WPF 그래프의 실제 내부 plot434×106만 표시.
- `G plot 30 <PNG>`: 같은 sample/filter/Bezier/chunk를 native D2D geometry realization으로 직접 렌더. static resources 최초 생성, open chunk는 DATA update 때만 새 realization, frame에서는 재사용. WPF Drawing adapter와 별개.

## 계측

관리자 PowerShell에서 `capture.ps1 -Output <폴더>/frames -StopFile <폴더>/stop` 실행. 자체 이름의 ETW session만 만들고 marker/finally 또는12분failsafe로 중지한다. Desktop Duplication은 사용하지 않는다.

기존 `harness/renderer-matrix/trace-reader`를 사용해 `<ETL> <frames.jsonl> 0 frames`로 변환하고 stdout(START/LOST)을 frames-meta.txt로 보존한다. `python analyze.py <폴더>`는 `<label>-<mode>.jsonl`들과 프레임을 결합한다. DWM 전달은 physical FPS가 아니다. Timing20Hz의 간격은 연속 animation stall과 분리한다. D3D GetFrameStatistics와 WPF DWM UpdateWindow는 동등 관측점이 아니다.

`dotnet-trace collect --process-id <PID> --duration 00:00:30 --profile dotnet-sampled-thread-time,gc-verbose --format Speedscope --output <managed.nettrace>`로 별도 profiling 실행을 확보한다. dotnet-trace 실행에는 해당 SDK의 DOTNET_ROOT가 필요할 수 있다. allocation reader는 `-p:TraceEventDirectory=<설치된 dotnet-trace의 TraceEvent DLL 폴더>`로 빌드하고 `<managed.nettrace>`를 전달한다. 25초 미만 capture는 실패시킨다. tick별 전체 stack에 한 번만 귀속하며 inclusive 합산을 하지 않는다. GC verbose/sampled-thread-time overhead를 정상 CPU/FPS 수치로 보고하지 않는다.

Native ETW PerfInfo/Sample/CSwitch stack은 기존 kernel-capture/etl-stacks 진단 도구로 내보낸다. `schedule.py <profile 폴더>`는 app.jsonl에 기록한 native UI TID와 WPF CPartitionThread stack을 식별한다. `correlate.py`는 native-stacks.jsonl과 같은ETL의 frames.jsonl을 연계한다. ReadyThread 미수집 시 Wait 구간의 blocked/ready 지연은 구분할 수 없다.

## 이번 증거

`docs/reports/2026-09-12-monitor-structural-remediation.md` 및 `harness/reports/monitor-structure-20260912/`.
원본 trace/기준선/실행 scripts: 개발 workspace의 `work/monitor-structure/`.
최종 비교는 동일조건3회 Before→After, 실행별값/중앙값/범위/표준편차를 기록했다. 자동테스트 PASS와 GPU 사용만으로 성능 성공을 선언하지 않는다.
