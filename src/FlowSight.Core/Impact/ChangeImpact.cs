using FlowSight.Core.Model;
using FlowSight.Core.ProcessMaps;

namespace FlowSight.Core.Impact;

/// <summary>A comparison window <c>[Start, End)</c>.</summary>
public sealed record ImpactWindow(DateTimeOffset Start, DateTimeOffset End);

/// <param name="Steps">Events of the changed step completed in the window.</param>
/// <param name="MedianQueueMinutes">Queued → picked up; events without a start time are left out.</param>
/// <param name="AverageWaiting">Average number of the step's items open at noon on the window's working days; null when no working day falls in the window.</param>
public sealed record ImpactStats(int Steps, double? MedianStepMinutes, double? P90StepMinutes, double? MedianQueueMinutes, double? AverageWaiting);

public enum ImpactVerdict { InsufficientData, Improved, Unchanged, Worse }

public sealed record MonthlyPoint(string PeriodId, double? MedianStepMinutes, int Steps);

/// <summary>What happened to a step after a dated change in the process map.</summary>
/// <param name="MedianChangePercent">(after median ÷ before median − 1) × 100; null when either median is missing.</param>
public sealed record ChangeImpact(
    MapChange Change,
    string StepId,
    string StepName,
    Stage? Stage,
    ImpactWindow Before,
    ImpactWindow After,
    ImpactStats BeforeStats,
    ImpactStats AfterStats,
    double? MedianChangePercent,
    ImpactVerdict Verdict,
    IReadOnlyList<MonthlyPoint> Monthly);
