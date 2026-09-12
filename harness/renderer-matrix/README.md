# Monitor renderer matrix

Isolated diagnostic harness; does not start the game, collector, uploader or updater. Uses the current built Client/Core DLLs and their original Avante images/fonts/views. No product package dependency or installed-file replacement.

## Run

1. Build the repository Release binary using .NET SDK 8.0.424.
2. Build `matrix.csproj` with the same SDK. Vortice Direct3D11/Direct2D1/DirectComposition 3.8.3 are harness-only dependencies.
3. `run.ps1 -DotnetExe <absolute dotnet.exe> -Name <new evidence directory> -Seconds 30`
4. Convert `frames.etl` with the existing TraceEvent frame reader (`etl-summary.dll <ETL> <JSONL> 0 frames`), saving stdout to `frames-meta.txt`.
5. `python analyze.py <evidence directory>`; run `python analyze.py` for the statistics self-check.

The ETW helper requests administrative elevation, uses its own named session, and stops through a marker/finally block (12 minute failsafe). Existing evidence directories are never overwritten. Raw ETL is local evidence, not a release asset.

Repository runner output is `harness/reports/renderer-matrix/<Name>` (gitignored). The local work copy used during development keeps evidence beside its source. The optional repository `trace-reader` project reuses a TraceEvent assembly from an installed dotnet-trace tool: build with `-p:TraceEventAssembly=<absolute Microsoft.Diagnostics.Tracing.TraceEvent.dll>`; no new product dependency.

For Gate B, `-Name gate-b` compares N (preserved old Client/Core DLLs, native motion, SoftwareOnly) and P (selected current Client/Core DLLs, native product motion, Default). Build the matrix first, copy its output to `control-bin`, and replace only the two Client/Core DLLs there with the preserved baseline. Record all DLL hashes. The common input scheduler remains identical; it does not override native animation values in N/P. Run order: Avante N/P, full combination N/P.

## Conditions

- A: layered transparent WPF, SoftwareOnly.
- B: layered transparent WPF, Default rendering (OS-selected hardware/fallback).
- C: opaque WPF Default, diagnostic only.
- D: independent BGRA D3D11 device shared by four Direct2D surfaces; premultiplied FlipSequential DirectComposition HWNDs, topmost/noactivate/transparent hit-test styles. No game hooks.
- Identical physical bounds: Avante 454x375, graph 540x120, pedal 120x120, timing 390x150. Source assets are unchanged.
- Deterministic sinusoidal 60Hz input with absolute sample indices, 10-second graph prefill. Current Timing updates with fresh models at 20Hz. One identical 144Hz high-resolution waitable-timer presentation scheduler; missed callbacks coalesce, sample backlog drains. No busy spin or global timeBeginPeriod.
- Product target followers/timers are stopped **inside this harness only** and identical 65ms RPM / 35ms steering interpolation and 125ms red flash are applied. This isolates render backends; results are not automatically the product's native-clock performance.
- 3 seconds warmup, 30 seconds measurement. White/yellow/red screenshots occur before measurement. D backbuffer readback occurs **before Present**, only for these snapshots; no per-frame CPU bitmap transfer.
- A/B/C and D consume the same WPF drawing/glyph/image authority. D uses a diagnostic retained-drawing adapter (including WPF scene construction and geometry conversion overhead), not a fully native rewritten Avante renderer. Do not infer the performance ceiling of Direct2D from this adapter.

## Metrics and limitations

CPU is process CPU time / elapsed / logical processors. Memory is endpoint working set and private bytes, not peak and not GPU VRAM. Allocation is GC total allocated bytes delta, GC is Gen0/1/2 counts. GPU is the sum of the process's reported engine counters, not a single normalized whole-GPU utilization. Missing counters are unavailable, never zero.

WPF DWM delivery is per-HWND `ETWGUID_DWMUPDATEWINDOW` with events closer than 0.1ms merged. D additionally records changing DXGI frame statistics, not Present call count. DXGI statistics and DWM UpdateWindow are different observation points; multi-monitor reliability is not independently established. Neither is labelled physical FPS. Missing DWM events do not become callback/Present-based PASS.

Gate A's common scheduler is an experiment, not a silent change to collection cadence. Product adoption requires its own Gate B, preservation of source contracts and settings, and no visual degradation. No process split or VR hardware validation is part of this harness.
