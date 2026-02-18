using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using MLoops.ViewModels;

namespace MLoops.Views;

public class WaveformControl : Control
{
    public static readonly StyledProperty<float[]> MinPeaksProperty =
        AvaloniaProperty.Register<WaveformControl, float[]>(nameof(MinPeaks), Array.Empty<float>());

    public static readonly StyledProperty<float[]> MaxPeaksProperty =
        AvaloniaProperty.Register<WaveformControl, float[]>(nameof(MaxPeaks), Array.Empty<float>());

    public static readonly StyledProperty<double> PlayheadPositionProperty =
        AvaloniaProperty.Register<WaveformControl, double>(nameof(PlayheadPosition));

    public static readonly StyledProperty<ObservableCollection<LoopMarkerInfo>> LoopMarkersProperty =
        AvaloniaProperty.Register<WaveformControl, ObservableCollection<LoopMarkerInfo>>(
            nameof(LoopMarkers), new ObservableCollection<LoopMarkerInfo>());


    public float[] MinPeaks
    {
        get => GetValue(MinPeaksProperty);
        set => SetValue(MinPeaksProperty, value);
    }

    public float[] MaxPeaks
    {
        get => GetValue(MaxPeaksProperty);
        set => SetValue(MaxPeaksProperty, value);
    }

    public double PlayheadPosition
    {
        get => GetValue(PlayheadPositionProperty);
        set => SetValue(PlayheadPositionProperty, value);
    }

    public ObservableCollection<LoopMarkerInfo> LoopMarkers
    {
        get => GetValue(LoopMarkersProperty);
        set => SetValue(LoopMarkersProperty, value);
    }

    public event Action<double>? SeekRequested;

    private static readonly Color BackgroundColor = Color.Parse("#0c1116");
    private static readonly Color CenterLineColor = Color.Parse("#666666");
    private static readonly Color WaveformStrokeColor = Color.Parse("#1976D2");
    private static readonly Color WaveformFillTop = Color.Parse("#E64F8CFF"); // 90% opacity
    private static readonly Color WaveformFillBottom = Color.Parse("#4D4F8CFF"); // 30% opacity
    private static readonly Color PlayheadColor = Color.Parse("#FF9800");
    private static readonly Color LoopStartColor = Color.Parse("#4F8CFF");
    private static readonly Color LoopEndColor = Color.Parse("#E06B6B");
    private static readonly Color LoopRegionColor = Color.Parse("#1F4F8CFF"); // ~12% opacity
    private const double WaveformVerticalPaddingRatio = 0.08;

    private bool _isDragging;

    static WaveformControl()
    {
        AffectsRender<WaveformControl>(
            MinPeaksProperty,
            MaxPeaksProperty,
            PlayheadPositionProperty,
            LoopMarkersProperty);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        var width = bounds.Width;
        var height = bounds.Height;
        var centerY = height / 2;
        var verticalPadding = Math.Max(6, height * WaveformVerticalPaddingRatio);
        var drawableHeight = Math.Max(1, height - (verticalPadding * 2));

        // Background
        context.FillRectangle(new SolidColorBrush(BackgroundColor), new Rect(0, 0, width, height));

        // Center line
        context.DrawLine(new Pen(new SolidColorBrush(CenterLineColor), 1),
            new Point(0, centerY), new Point(width, centerY));

        var minPeaks = MinPeaks;
        var maxPeaks = MaxPeaks;

        if (minPeaks.Length > 0 && maxPeaks.Length > 0)
        {
            // Build waveform geometry
            var peakCount = Math.Min(minPeaks.Length, maxPeaks.Length);
            var geometry = new StreamGeometry();

            using (var ctx = geometry.Open())
            {
                // Draw top edge (max peaks) left to right
                for (var x = 0; x < peakCount; x++)
                {
                    var xPos = x / (double)peakCount * width;
                    var rawMax = float.IsFinite(maxPeaks[x]) ? maxPeaks[x] : 0f;
                    var peakMax = Math.Clamp(rawMax, -1f, 1f);
                    var yMin = verticalPadding + (1 - peakMax) * 0.5 * drawableHeight;

                    // Clamp first/last to center
                    if (x == 0 || x == peakCount - 1)
                        yMin = centerY;

                    if (x == 0)
                        ctx.BeginFigure(new Point(xPos, yMin), true);
                    else
                        ctx.LineTo(new Point(xPos, yMin));
                }

                // Draw bottom edge (min peaks) right to left
                for (var x = peakCount - 1; x >= 0; x--)
                {
                    var xPos = x / (double)peakCount * width;
                    var rawMin = float.IsFinite(minPeaks[x]) ? minPeaks[x] : 0f;
                    var peakMin = Math.Clamp(rawMin, -1f, 1f);
                    var yMax = verticalPadding + (1 - peakMin) * 0.5 * drawableHeight;

                    if (x == 0 || x == peakCount - 1)
                        yMax = centerY;

                    ctx.LineTo(new Point(xPos, yMax));
                }

                ctx.EndFigure(true);
            }

            // Fill with gradient
            var gradientBrush = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(WaveformFillTop, 0),
                    new GradientStop(WaveformFillBottom, 1)
                }
            };

            context.DrawGeometry(gradientBrush, new Pen(new SolidColorBrush(WaveformStrokeColor), 1), geometry);

        }

        // Draw loop regions
        if (LoopMarkers is not null)
        {
            foreach (var marker in LoopMarkers)
            {
                var startX = marker.StartNormalized * width;
                var endX = marker.EndNormalized * width;
                var regionWidth = endX - startX;

                // Region fill
                context.FillRectangle(new SolidColorBrush(LoopRegionColor),
                    new Rect(startX, 0, regionWidth, height));

                // Start marker
                context.DrawLine(new Pen(new SolidColorBrush(LoopStartColor), 2),
                    new Point(startX, 0), new Point(startX, height));

                // End marker
                context.DrawLine(new Pen(new SolidColorBrush(LoopEndColor), 2),
                    new Point(endX, 0), new Point(endX, height));
            }
        }

        // Draw playhead
        if (PlayheadPosition > 0)
        {
            var playheadX = PlayheadPosition * width;
            var playheadPen = new Pen(new SolidColorBrush(PlayheadColor), 2);
            context.DrawLine(playheadPen, new Point(playheadX, 0), new Point(playheadX, height));

            // Triangle tip at top
            var triangleGeometry = new StreamGeometry();
            using (var ctx = triangleGeometry.Open())
            {
                ctx.BeginFigure(new Point(playheadX - 6, 0), true);
                ctx.LineTo(new Point(playheadX + 6, 0));
                ctx.LineTo(new Point(playheadX, 10));
                ctx.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(PlayheadColor), null, triangleGeometry);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        _isDragging = true;
        HandleSeek(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_isDragging)
        {
            HandleSeek(e.GetPosition(this));
            e.Handled = true;
        }
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        _isDragging = false;
    }

    private void HandleSeek(Point position)
    {
        var normalized = Math.Max(0, Math.Min(1, position.X / Bounds.Width));
        SeekRequested?.Invoke(normalized);
    }
}
