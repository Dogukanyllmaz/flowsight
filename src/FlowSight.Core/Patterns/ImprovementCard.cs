using FlowSight.Core.Model;

namespace FlowSight.Core.Patterns;

/// <summary>One measured fact behind a card. Values are working minutes, ratios (0–1) or counts; the code says which.</summary>
public sealed record Evidence(string Code, IReadOnlyDictionary<string, double> Values);

/// <summary>
/// A detected bottleneck pattern: what the data shows, which map element explains it, and what to change.
/// Computed and read-only; <see cref="Id"/> (<c>pattern:stepId</c>) is stable so a host can attach owner and status.
/// </summary>
/// <param name="Segment">Narrower scope when the pattern only shows in part of the flow (e.g. one item category); null for the whole step.</param>
/// <param name="MapProperty">The map element the cause points at, e.g. <c>assignee: group-queue</c>.</param>
/// <param name="ExpectedSavingMinutesPerLine">Median working minutes the symptom adds per affected line; null when not estimable.</param>
public sealed record ImprovementCard(
    string Id,
    string Pattern,
    Stage Stage,
    string StepId,
    string StepName,
    string? Segment,
    string MapProperty,
    IReadOnlyList<Evidence> Evidence,
    string ProposedChange,
    double? ExpectedSavingMinutesPerLine,
    int AffectedLines);

/// <param name="InsufficientData"><c>pattern:stepId</c> checks that could not run because an optional input is missing.</param>
/// <param name="UnmappedActivities">Activities seen in the period that have no step in the map.</param>
public sealed record PatternResult(
    IReadOnlyList<ImprovementCard> Cards,
    IReadOnlyList<string> InsufficientData,
    IReadOnlyList<string> UnmappedActivities);

public static class PatternCodes
{
    public const string SharedQueueSearch = "sharedQueueSearch";
    public const string SerialApprovalStack = "serialApprovalStack";
    public const string SinglePointOfFailure = "singlePointOfFailure";
    public const string ReworkLoop = "reworkLoop";
    public const string BatchWaiting = "batchWaiting";
    public const string ExceptionQueue = "exceptionQueue";

    public const string RouteByAttribute = "routeByAttribute";
    public const string ParallelOrThresholdApproval = "parallelOrThresholdApproval";
    public const string AssignToRoleWithDelegation = "assignToRoleWithDelegation";
    public const string ValidateAtEntry = "validateAtEntry";
    public const string DailyProcessingWithSla = "dailyProcessingWithSla";
    public const string ToleranceRulesUpstream = "toleranceRulesUpstream";
}
