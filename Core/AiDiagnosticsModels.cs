namespace PC_HealthCheck.Core;

public sealed class AiDiagnosticResult
{
    public List<AiAnomaly> Anomalies { get; set; } = new();
    public List<string> Recommendations { get; set; } = new();
    public string SsdPrediction { get; set; } = "";
    public string Summary { get; set; } = "";
    public string FullResponse { get; set; } = "";
    public bool IsUserQuestionMode { get; set; } = false;
}

public sealed class AiAnomaly
{
    public string Component { get; set; } = "";
    public string Time { get; set; } = "";
    public double Value { get; set; }
    public string Reason { get; set; } = "";
    public string Severity { get; set; } = "medium";
}