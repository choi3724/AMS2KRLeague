using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace AMS2LeagueClient.Runtime
{
    // Runs the 30 Hz Shared Memory read on its own thread. A thread-pool timer callback can be
    // delayed for hundreds of milliseconds while the game saturates the CPU, which froze every HUD
    // that projects from the latest snapshot. Absolute deadlines keep the cadence from drifting.
    internal sealed class TelemetryReadThread
    {
        private const uint HighResolutionTimer = 2, TimerAllAccess = 0x1F0003;
        private readonly Action _tick;
        private readonly double _hz;
        private readonly CancellationTokenSource _cancel = new CancellationTokenSource();
        private readonly TaskCompletionSource _stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Thread _thread;

        internal TelemetryReadThread(string name, double hz, Action tick)
        {
            _tick = tick; _hz = hz;
            _thread = new Thread(Loop) { IsBackground = true, Name = name, Priority = ThreadPriority.AboveNormal };
        }

        internal void Start() => _thread.Start();

        // Completes after the last tick returns, so the caller may then dispose what the tick reads.
        internal Task StopAsync()
        {
            if (!_cancel.IsCancellationRequested) _cancel.Cancel();
            return _thread.ThreadState.HasFlag(System.Threading.ThreadState.Unstarted) ? Task.CompletedTask : _stopped.Task;
        }

        private void Loop()
        {
            using var timer = CreateTimer();
            try
            {
                var waits = timer == null ? null : new WaitHandle[] { timer, _cancel.Token.WaitHandle };
                var clock = Stopwatch.StartNew();
                long deadline = 0;
                while (!_cancel.IsCancellationRequested)
                {
                    _tick();
                    deadline = Math.Max(deadline + 1, (long)(clock.Elapsed.TotalSeconds * _hz) + 1);
                    double remaining = deadline / _hz - clock.Elapsed.TotalSeconds;
                    if (remaining <= 0) continue;
                    long due = -Math.Max(1, (long)(remaining * 10_000_000));
                    if (timer != null && SetWaitableTimer(timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                    {
                        if (WaitHandle.WaitAny(waits!) != 0) break;
                    }
                    else if (_cancel.Token.WaitHandle.WaitOne(TimeSpan.FromSeconds(remaining))) break;
                }
            }
            finally { _stopped.TrySetResult(); }
        }

        private static NativeTimer? CreateTimer()
        {
            // High-resolution timers need Windows 10 1803+; fall back to a regular waitable timer.
            foreach (uint flags in new[] { HighResolutionTimer, 0U })
            {
                var timer = new NativeTimer(CreateWaitableTimerEx(IntPtr.Zero, null, flags, TimerAllAccess));
                if (!timer.SafeWaitHandle.IsInvalid) return timer;
                timer.Dispose();
            }
            return null;
        }

        private sealed class NativeTimer : WaitHandle
        {
            internal NativeTimer(IntPtr handle) => SafeWaitHandle = new SafeWaitHandle(handle, true);
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string? name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, IntPtr callback, IntPtr argument, bool resume);
    }
}
