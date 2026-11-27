namespace VailITDashboard.Pages;

/// <summary>What the _ThresholdEditor partial needs.</summary>
public sealed class ThresholdEditorModel
{
    public ThresholdScope Scope { get; init; }
    public string ScopeValue { get; init; } = "";
    public IEnumerable<Threshold> Thresholds { get; init; } = [];
    public IEnumerable<string> KnownInstances { get; init; } = [];
    public string ReturnUrl { get; init; } = "/thresholds";
    public string EmptyText { get; init; } = "None at this level.";
}
