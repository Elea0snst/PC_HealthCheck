using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Layout;
using Avalonia.Media;
using PC_HealthCheck.Visualization;

namespace PC_HealthCheck.Views;

public partial class MonitoringChartPanel : UserControl
{
    private const double MarginLeft = 44;
    private const double MarginRight = 44;
    private const double MarginTop = 28;
    private const double MarginBottom = 32;

    public MonitoringChartPanel()
    {
        InitializeComponent();
        ChartBorder.SizeChanged += (_, _) => RedrawLast();
        Loaded += (_, _) => RedrawLast();
        _lastSnapshot = null;
    }

    private MonitoringChartSnapshot? _lastSnapshot;

    public void Render(MonitoringChartSnapshot snapshot)
    {
        _lastSnapshot = snapshot;
        Redraw(snapshot);
    }

    private void RedrawLast()
    {
        if (_lastSnapshot is not null)
            Redraw(_lastSnapshot);
    }

    private void Redraw(MonitoringChartSnapshot snapshot)
    {
        ChartCanvas.Children.Clear();
        LegendPanel.Items.Clear();

        var hasSeries = snapshot.Series.Count > 0;
        PlaceholderText.IsVisible = !hasSeries;
        HintText.Text = snapshot.StatusHint;
        HintText.IsVisible = !string.IsNullOrWhiteSpace(snapshot.StatusHint);

        if (!hasSeries)
            return;

        var w = Math.Max(ChartCanvas.Bounds.Width, ChartBorder.Bounds.Width - 2);
        var h = Math.Max(ChartCanvas.Bounds.Height, 260);
        if (w < 80) w = 600;
        if (h < 80) h = 260;

        var plotW = w - MarginLeft - MarginRight;
        var plotH = h - MarginTop - MarginBottom;
        var maxX = Math.Max(snapshot.MaxTimeSeconds, 1);

        var tempSeries = snapshot.Series.Where(s => s.Scale == "Temp").ToList();
        var pctSeries = snapshot.Series.Where(s => s.Scale == "Percent").ToList();

        var tempMin = 0d;
        var tempMax = Math.Max(snapshot.CriticalTempC + 10, 100d);
        if (tempSeries.Count > 0)
        {
            tempMin = Math.Max(0, tempSeries.SelectMany(s => s.Points).Min(p => p.Y) - 5);
            tempMax = Math.Max(tempMax, tempSeries.SelectMany(s => s.Points).Max(p => p.Y) + 5);
        }

        var pctMax = 100d;
        if (pctSeries.Any(s => s.Title.Contains("Вт", StringComparison.OrdinalIgnoreCase)))
        {
            pctMax = Math.Max(100, pctSeries.SelectMany(s => s.Points).Max(p => p.Y) * 1.15);
        }

        if (tempSeries.Count > 0)
            DrawTempZones(plotW, plotH, snapshot.WarningTempC, snapshot.CriticalTempC, tempMax);

        foreach (var s in snapshot.Series)
        {
            var brush = BrushFromHex(s.Color);
            var polyline = new Polyline
            {
                Stroke = brush,
                StrokeThickness = 2,
                Fill = null
            };

            foreach (var (x, y) in s.Points)
            {
                var px = MarginLeft + x / maxX * plotW;
                double py;
                if (s.Scale == "Temp")
                    py = MarginTop + (1.0 - (y - tempMin) / Math.Max(tempMax - tempMin, 1)) * plotH;
                else
                    py = MarginTop + (1.0 - y / Math.Max(pctMax, 1)) * plotH;
                polyline.Points.Add(new Point(px, py));
            }

            if (polyline.Points.Count >= 2)
                ChartCanvas.Children.Add(polyline);

            LegendPanel.Items.Add(new TextBlock
            {
                Text = s.Title,
                Foreground = brush,
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 2)
            });
        }

        DrawGridAndLabels(plotW, plotH, maxX, tempMin, tempMax, pctMax);
    }

    private void DrawTempZones(double plotW, double plotH, int warnC, int critC, double tempMax)
    {
        void Zone(double y0, double y1, byte a, byte r, byte g, byte b)
        {
            if (y1 <= y0) return;
            var top = MarginTop + (1.0 - (y1 - 0) / Math.Max(tempMax, 1)) * plotH;
            var bottom = MarginTop + (1.0 - (y0 - 0) / Math.Max(tempMax, 1)) * plotH;
            ChartCanvas.Children.Insert(0, new Rectangle
            {
                Width = plotW,
                Height = Math.Max(1, bottom - top),
                Fill = new SolidColorBrush(Color.FromArgb(a, r, g, b)),
                [Canvas.LeftProperty] = MarginLeft,
                [Canvas.TopProperty] = top
            });
        }

        Zone(0, warnC, 40, 46, 204, 113);
        Zone(warnC, critC, 50, 241, 196, 15);
        Zone(critC, tempMax, 50, 231, 76, 60);
    }

    private void DrawGridAndLabels(double plotW, double plotH, double maxX, double tempMin, double tempMax, double pctMax)
    {
        var gridBrush = new SolidColorBrush(Color.FromArgb(60, 120, 140, 160));
        for (var i = 0; i <= 4; i++)
        {
            var y = MarginTop + plotH * i / 4.0;
            ChartCanvas.Children.Add(new Line
            {
                StartPoint = new Point(MarginLeft, y),
                EndPoint = new Point(MarginLeft + plotW, y),
                Stroke = gridBrush,
                StrokeThickness = 1
            });
        }

        ChartCanvas.Children.Add(new TextBlock
        {
            Text = "°C",
            FontSize = 10,
            Foreground = Brushes.Gray,
            [Canvas.LeftProperty] = 4.0,
            [Canvas.TopProperty] = MarginTop
        });
        ChartCanvas.Children.Add(new TextBlock
        {
            Text = "%",
            FontSize = 10,
            Foreground = Brushes.Gray,
            [Canvas.LeftProperty] = MarginLeft + plotW + 8,
            [Canvas.TopProperty] = MarginTop
        });
        ChartCanvas.Children.Add(new TextBlock
        {
            Text = $"0 — {maxX:F0} с",
            FontSize = 10,
            Foreground = Brushes.Gray,
            [Canvas.LeftProperty] = MarginLeft,
            [Canvas.TopProperty] = MarginTop + plotH + 6
        });
    }

    private static IBrush BrushFromHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            var r = Convert.ToByte(hex[..2], 16);
            var g = Convert.ToByte(hex[2..4], 16);
            var b = Convert.ToByte(hex[4..6], 16);
            return new SolidColorBrush(Color.FromRgb(r, g, b));
        }

        return Brushes.SteelBlue;
    }
}
