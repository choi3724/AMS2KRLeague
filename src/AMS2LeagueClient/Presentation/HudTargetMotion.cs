using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Media;

namespace AMS2LeagueClient.Presentation
{
    // One reusable target follower per moving property. No per-sample Animation/Clock objects.
    internal sealed class HudTargetMotion
    {
        private readonly Action<double> _apply;
        private readonly double _duration;
        private double _from, _target;
        private long _started;
        private bool _subscribed;
        private bool _monitorClock;
        private readonly FrameworkElement? _owner;
        internal bool IsActive => _subscribed;
        internal HudTargetMotion(double milliseconds, Action<double> apply)
        { _duration = milliseconds / 1000; _apply = apply; }
        internal HudTargetMotion(double milliseconds, Action<double> apply, FrameworkElement owner) : this(milliseconds, apply) { _owner = owner; }
        internal void Set(double target, bool animate)
        {
            if (_owner != null && !MonitorPresentationClock.HasVisibleContent(_owner)) animate = false;
            if (!animate) { Stop(); _from = _target = target; _apply(target); return; }
            if (_monitorClock && !MonitorPresentationClock.CanUse(_owner)) Stop();
            if (_target == target)
            {
                // A hide/output change may stop motion before its last value was applied.
                // Reappearing with the same observed target must not leave a stale partial value.
                if (!_subscribed && _from != target) { _from = target; _apply(target); }
                return;
            }
            double current = Value();
            _from = current; _target = target; _started = Stopwatch.GetTimestamp();
            if (!_subscribed)
            {
                _subscribed = true;
                _monitorClock = MonitorPresentationClock.CanUse(_owner) && MonitorPresentationClock.Subscribe(Frame);
                if (!_monitorClock) CompositionTarget.Rendering += Frame;
            }
        }
        private double Value() => !_subscribed ? _target : _from + (_target - _from) *
            Math.Clamp(Stopwatch.GetElapsedTime(_started).TotalSeconds / _duration, 0, 1);
        private void Frame(object? sender, EventArgs args)
        {
            if (_owner != null && !MonitorPresentationClock.HasVisibleContent(_owner)) { Stop(); return; }
            if (_monitorClock && !MonitorPresentationClock.CanUse(_owner)) { Stop(); return; }
            _apply(Value());
            if (Stopwatch.GetElapsedTime(_started).TotalSeconds >= _duration) Stop();
        }
        internal void Stop()
        {
            if (_subscribed)
            {
                if (_monitorClock) MonitorPresentationClock.Unsubscribe(Frame);
                else CompositionTarget.Rendering -= Frame;
            }
            _subscribed = false;
        }
    }

    internal sealed class HudHistoryScroll
    {
        private readonly TranslateTransform _transform;
        private readonly Stopwatch _clock;
        private double _width;
        private bool _subscribed;
        private bool _monitorClock;
        private readonly FrameworkElement? _owner;
        internal HudHistoryScroll(TranslateTransform transform, Stopwatch clock) { _transform = transform; _clock = clock; }
        internal HudHistoryScroll(TranslateTransform transform, Stopwatch clock, FrameworkElement owner) : this(transform, clock) { _owner = owner; }
        internal void Update(double width, bool active)
        {
            _width = width;
            if (_monitorClock && !MonitorPresentationClock.CanUse(_owner)) Stop();
            if (_owner != null && !MonitorPresentationClock.HasVisibleContent(_owner)) active = false;
            if (!active) { Stop(); return; }
            if (!_subscribed)
            {
                _subscribed = true;
                _monitorClock = MonitorPresentationClock.CanUse(_owner) && MonitorPresentationClock.Subscribe(Frame);
                if (!_monitorClock) CompositionTarget.Rendering += Frame;
            }
            Frame(null, EventArgs.Empty);
        }
        private void Frame(object? sender, EventArgs args)
        {
            if (_owner != null && !MonitorPresentationClock.HasVisibleContent(_owner)) { Stop(); return; }
            if (_monitorClock && !MonitorPresentationClock.CanUse(_owner)) { Stop(); return; }
            double age = Math.Min(1, _clock.Elapsed.TotalSeconds);
            _transform.X = -_width / 10 * age;
            if (age >= 1) Stop();
        }
        internal void Stop()
        {
            if (_subscribed)
            {
                if (_monitorClock) MonitorPresentationClock.Unsubscribe(Frame);
                else CompositionTarget.Rendering -= Frame;
            }
            _subscribed = false;
        }
    }
}
