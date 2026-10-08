using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ChiliMusic;

// Animate presentation between authoritative engine samples. Seeking and lyric timing
// always use the real playback position, never this interpolated display value.
public sealed class PlaybackSeekSlider : Slider
{
    public static readonly DependencyProperty PlaybackPositionProperty = DependencyProperty.Register(nameof(PlaybackPosition), typeof(double), typeof(PlaybackSeekSlider), new PropertyMetadata(0d, SampleChanged));
    public static readonly DependencyProperty PlaybackActiveProperty = DependencyProperty.Register(nameof(PlaybackActive), typeof(bool), typeof(PlaybackSeekSlider), new PropertyMetadata(false, ActivityChanged));
    public double PlaybackPosition { get => (double)GetValue(PlaybackPositionProperty); set => SetValue(PlaybackPositionProperty, value); }
    public bool PlaybackActive { get => (bool)GetValue(PlaybackActiveProperty); set => SetValue(PlaybackActiveProperty, value); }
    private long _sampleTime;
    private bool _rendering, _interacting, _dragging, _dragCommitted;
    public PlaybackSeekSlider()
    {
        Style = (Style)Application.Current.Resources[typeof(Slider)]; IsMoveToPointEnabled = true;
        SetBinding(MaximumProperty, new Binding("Duration")); SetBinding(PlaybackPositionProperty, new Binding("Position")); SetBinding(PlaybackActiveProperty, new Binding("IsProgressMoving"));
        Loaded += (_, _) => { _sampleTime = Stopwatch.GetTimestamp(); Value = PlaybackPosition; UpdateRendering(); };
        Unloaded += (_, _) => StopRendering(); IsVisibleChanged += (_, _) => UpdateRendering();
        AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler((_, _) => { _interacting = _dragging = true; _dragCommitted = false; }));
        AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler((_, e) => { _dragging = false; _dragCommitted = true; if (!e.Canceled) Commit(); else { _interacting = false; Value = PlaybackPosition; } }));
    }
    private static void SampleChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var slider = (PlaybackSeekSlider)owner; slider._sampleTime = Stopwatch.GetTimestamp();
        if (!slider._interacting && (!slider.PlaybackActive || (double)args.NewValue < (double)args.OldValue - .1 || Math.Abs((double)args.NewValue - slider.Value) > .75)) slider.Value = (double)args.NewValue;
        slider.UpdateRendering();
    }
    private static void ActivityChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var slider = (PlaybackSeekSlider)owner; slider._sampleTime = Stopwatch.GetTimestamp();
        if (!slider._interacting) slider.Value = slider.PlaybackPosition;
        slider.UpdateRendering();
    }
    private void UpdateRendering()
    {
        bool active = IsLoaded && IsVisible && PlaybackActive;
        if (active && !_rendering) { CompositionTarget.Rendering += Render; _rendering = true; }
        else if (!active) StopRendering();
    }
    private void StopRendering() { if (!_rendering) return; CompositionTarget.Rendering -= Render; _rendering = false; }
    private void Render(object? sender, EventArgs args)
    {
        if (_interacting || !PlaybackActive) return;
        double elapsed = Math.Clamp((Stopwatch.GetTimestamp() - _sampleTime) / (double)Stopwatch.Frequency, 0, .3);
        double target = Math.Clamp(PlaybackPosition + elapsed, Minimum, Maximum);
        if (target >= Value || Math.Abs(target - Value) > .75) Value = target;
    }
    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e) { _interacting = true; _dragCommitted = false; base.OnPreviewMouseLeftButtonDown(e); }
    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnPreviewMouseLeftButtonUp(e); if (!_dragging && !_dragCommitted) Commit(); _dragCommitted = false;
    }
    protected override void OnLostMouseCapture(MouseEventArgs e) { base.OnLostMouseCapture(e); if (!_dragging) _interacting = false; }
    protected override void OnPreviewKeyDown(KeyEventArgs e) { if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) _interacting = true; base.OnPreviewKeyDown(e); }
    protected override void OnPreviewKeyUp(KeyEventArgs e)
    {
        base.OnPreviewKeyUp(e); if (_interacting && e.Key is Key.Left or Key.Right or Key.Up or Key.Down or Key.Home or Key.End or Key.PageUp or Key.PageDown) Commit();
    }
    private void Commit()
    {
        if (DataContext is PlayerViewModel vm) vm.Seek(Value);
        _interacting = false; _sampleTime = Stopwatch.GetTimestamp(); Value = PlaybackPosition;
    }
}
