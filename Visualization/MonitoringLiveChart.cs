using OxyPlot;
using OxyPlot.Annotations;
using OxyPlot.Axes;
using OxyPlot.Series;
using System.Linq;

namespace PC_HealthCheck.Visualization;

/// <summary>График CPU: температура и загрузка в реальном времени с цветовыми зонами по порогам (ТЗ 2.1.2).</summary>
public sealed class MonitoringLiveChart
{
    private readonly List<RectangleAnnotation> _zoneAnnotations = new();
    private DateTime _epochUtc = DateTime.UtcNow;

    public PlotModel Model { get; }

    private readonly LineSeries _tempSeries;
    private readonly LineSeries _loadSeries;

    public MonitoringLiveChart()
    {
        Model = new PlotModel { Title = "CPU: температура и загрузка" };
        Model.IsLegendVisible = true;
        Model.PlotAreaBorderColor = OxyColors.LightGray;

        Model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Bottom,
            Key = "Time",
            Title = "Время от начала сессии, с",
            Minimum = 0,
            Maximum = 10,
            MajorGridlineStyle = LineStyle.Solid,
            MinorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(40, OxyColors.Gray),
            MinorGridlineColor = OxyColor.FromAColor(20, OxyColors.LightGray)
        });

        Model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Left,
            Key = "Temp",
            Title = "°C",
            Minimum = 0,
            Maximum = 120,
            MinimumPadding = 0.02,
            MaximumPadding = 0.08,
            MajorGridlineStyle = LineStyle.Dot,
            MajorGridlineColor = OxyColor.FromAColor(30, OxyColors.Gray)
        });

        Model.Axes.Add(new LinearAxis
        {
            Position = AxisPosition.Right,
            Key = "Load",
            Title = "%",
            Minimum = 0,
            Maximum = 100,
            MajorGridlineStyle = LineStyle.Dot
        });

        _tempSeries = new LineSeries
        {
            Title = "Температура CPU",
            YAxisKey = "Temp",
            Color = OxyColors.SteelBlue,
            StrokeThickness = 2,
            MarkerType = MarkerType.None
        };

        _loadSeries = new LineSeries
        {
            Title = "Загрузка CPU",
            YAxisKey = "Load",
            Color = OxyColors.DarkOrange,
            StrokeThickness = 2,
            MarkerType = MarkerType.None
        };

        Model.Series.Add(_tempSeries);
        Model.Series.Add(_loadSeries);
    }

    public void Reset()
    {
        _epochUtc = DateTime.UtcNow;
        _tempSeries.Points.Clear();
        _loadSeries.Points.Clear();
        ClearZones();
        ResetAxesToDefault();
        Model.InvalidatePlot(true);
    }

    private void ResetAxesToDefault()
    {
        var tempAx = Model.Axes.OfType<LinearAxis>().First(a => a.Key == "Temp");
        tempAx.Minimum = 0;
        tempAx.Maximum = 120;

        var timeAx = Model.Axes.OfType<LinearAxis>().First(a => a.Key == "Time");
        timeAx.Minimum = 0;
        timeAx.Maximum = 10;
    }

    private void ClearZones()
    {
        foreach (var z in _zoneAnnotations)
            Model.Annotations.Remove(z);
        _zoneAnnotations.Clear();
    }

    private void RebuildZones(double warnC, double critC, double maxX)
    {
        ClearZones();
        var xmax = maxX <= 0 ? 1 : maxX + Math.Max(5, maxX * 0.05);
        var ymax = Math.Max(125, critC + 15);

        void Zone(double y0, double y1, byte a, byte r, byte g, byte b)
        {
            var ann = new RectangleAnnotation
            {
                MinimumX = 0,
                MaximumX = xmax,
                MinimumY = y0,
                MaximumY = y1,
                Fill = OxyColor.FromArgb(a, r, g, b),
                Layer = AnnotationLayer.BelowSeries
            };
            Model.Annotations.Add(ann);
            _zoneAnnotations.Add(ann);
        }

        Zone(0, warnC, 30, 46, 204, 113);
        Zone(warnC, critC, 40, 241, 196, 15);
        Zone(critC, ymax, 40, 231, 76, 60);
    }

    public void Append(
        double? tempC,
        double? loadPct,
        bool showTemp,
        bool showLoad,
        int warnC,
        int critC,
        int maxPoints)
    {
        critC = Math.Max(critC, warnC);
        var x = (DateTime.UtcNow - _epochUtc).TotalSeconds;

        if (showTemp && tempC is { } t)
        {
            _tempSeries.Points.Add(new DataPoint(x, t));
            while (_tempSeries.Points.Count > maxPoints)
                _tempSeries.Points.RemoveAt(0);
        }

        if (showLoad && loadPct is { } l)
        {
            _loadSeries.Points.Add(new DataPoint(x, l));
            while (_loadSeries.Points.Count > maxPoints)
                _loadSeries.Points.RemoveAt(0);
        }

        var maxX = 0d;
        if (_tempSeries.Points.Count > 0)
            maxX = Math.Max(maxX, _tempSeries.Points[^1].X);
        if (_loadSeries.Points.Count > 0)
            maxX = Math.Max(maxX, _loadSeries.Points[^1].X);

        // Зоны температуры показываем только при включенном канале температуры.
        if (showTemp)
            RebuildZones(warnC, critC, maxX);
        else
            ClearZones();

        var timeAxis = Model.Axes.OfType<LinearAxis>().First(a => a.Key == "Time");
        timeAxis.Minimum = 0;
        // При старте графика точек ещё нет — задаём разумный максимум, чтобы аннотации/оси не были NaN.
        timeAxis.Maximum = Math.Max(maxX, 10);

        var tempAxis = Model.Axes.OfType<LinearAxis>().First(a => a.Key == "Temp");
        if (_tempSeries.Points.Count > 0)
        {
            var minT = _tempSeries.Points.Min(p => p.Y);
            var maxT = _tempSeries.Points.Max(p => p.Y);
            tempAxis.Minimum = Math.Max(0, minT - 8);
            tempAxis.Maximum = Math.Max(maxT + 8, critC + 5);
        }
        else
        {
            // При отсутствии точек всё равно показываем шкалу под зоны (иначе OxyPlot может не отрисовать аннотации).
            tempAxis.Minimum = 0;
            tempAxis.Maximum = Math.Max(125, critC + 15);
        }

        _tempSeries.IsVisible = showTemp;
        _loadSeries.IsVisible = showLoad;

        Model.InvalidatePlot(true);
    }

    public void SetSeriesVisibility(bool showTemp, bool showLoad)
    {
        _tempSeries.IsVisible = showTemp;
        _loadSeries.IsVisible = showLoad;
        if (!showTemp)
            ClearZones();
        Model.InvalidatePlot(true);
    }
}
