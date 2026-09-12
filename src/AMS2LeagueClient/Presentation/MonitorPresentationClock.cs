using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32.SafeHandles;

namespace AMS2LeagueClient.Presentation
{
    // Display interpolation only. No SHM reads, capture, session projection or upload here.
    // One shared timer runs only while a visible hardware Monitor surface needs motion.
    internal static class MonitorPresentationClock
    {
        private static EventHandler? _frames;
        private static Run? _run;
        internal static bool IsRunning => _run != null;
        internal static bool CanUse(FrameworkElement? owner) => HasVisibleContent(owner)
            && PresentationSource.FromVisual(owner) is HwndSource source
            && source.CompositionTarget.RenderMode == RenderMode.Default;

        internal static bool HasVisibleContent(FrameworkElement? owner)
        {
            if (owner == null || !owner.IsVisible) return false;
            for (DependencyObject? current = owner; current != null; current = VisualTreeHelper.GetParent(current))
                if (current is UIElement element && element.Opacity <= 0) return false;
            return true;
        }

        internal static bool Subscribe(EventHandler callback)
        {
            if (_run == null)
            {
                var timer = new NativeTimer(CreateWaitableTimerEx(IntPtr.Zero, null, 2, 0x1F0003));
                if (timer.SafeWaitHandle.IsInvalid) { timer.Dispose(); return false; }
                _run = new Run(Dispatcher.CurrentDispatcher, timer);
                _frames += callback;
                _run.Start();
            }
            else _frames += callback;
            return true;
        }
        internal static void Unsubscribe(EventHandler callback)
        {
            _frames -= callback;
            if (_frames == null && _run != null)
            {
                Run old = _run; _run = null; old.Stop();
            }
        }
        private sealed class NativeTimer : WaitHandle
        {
            internal NativeTimer(IntPtr handle) => SafeWaitHandle = new SafeWaitHandle(handle, true);
        }
        private sealed class Run
        {
            internal readonly CancellationTokenSource Cancel = new CancellationTokenSource();
            private readonly Dispatcher _dispatcher;
            private readonly NativeTimer _timer;
            private readonly Action _dispatch;
            private int _pending;
            private readonly object _stopGate = new object();
            private bool _disposed;
            internal void Stop() { lock (_stopGate) { if (!_disposed) Cancel.Cancel(); } }
            internal Run(Dispatcher dispatcher, NativeTimer timer)
            {
                _dispatcher = dispatcher; _timer = timer;
                _dispatch = () =>
                {
                    try { if (ReferenceEquals(_run, this)) _frames?.Invoke(null, EventArgs.Empty); }
                    finally { Interlocked.Exchange(ref _pending, 0); }
                };
            }
            internal void Start() => new Thread(Loop) { IsBackground = true, Name = "Monitor presentation" }.Start();
            private void Loop()
            {
                try
                {
                    var waits = new[] { _timer, Cancel.Token.WaitHandle };
                    var clock = Stopwatch.StartNew(); long deadline = 0;
                    while (!Cancel.IsCancellationRequested && !_dispatcher.HasShutdownStarted)
                    {
                        // Absolute deadlines prevent drift. Coalesce work when the UI is busy.
                        deadline = Math.Max(deadline + 1, (long)(clock.Elapsed.TotalSeconds * 144) + 1);
                        long due = -Math.Max(1, (long)((deadline / 144.0 - clock.Elapsed.TotalSeconds) * 10_000_000));
                        if (!SetWaitableTimer(_timer.SafeWaitHandle, ref due, 0, IntPtr.Zero, IntPtr.Zero, false))
                        {
                            Trace.TraceError("Monitor presentation waitable timer failed: {0}", Marshal.GetLastWin32Error());
                            // Avoid a busy spin on a runtime timer failure; preserve presentation.
                            if (Cancel.Token.WaitHandle.WaitOne(7)) break;
                        }
                        else if (WaitHandle.WaitAny(waits) != 0) break;
                        if (Interlocked.Exchange(ref _pending, 1) == 0 && !_dispatcher.HasShutdownStarted)
                            _dispatcher.BeginInvoke(DispatcherPriority.Render, _dispatch);
                    }
                }
                finally { lock (_stopGate) { _disposed = true; _timer.Dispose(); Cancel.Dispose(); } }
            }
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateWaitableTimerEx(IntPtr attributes, string? name, uint flags, uint access);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long due, int period, IntPtr callback, IntPtr argument, bool resume);
    }
}
