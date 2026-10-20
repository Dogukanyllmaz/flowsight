namespace FlowSight.Core.Model;

/// <summary>How a step was assigned to the person who did it.</summary>
public enum AssignmentKind { Person, Role, GroupQueue, FieldRouted, System }

public enum StepOutcome { Approved, Rejected, Returned, Cancelled, Done }

public enum LinePath { Goods, Service }

/// <summary>One step performed on a request line.</summary>
/// <param name="QueuedAt">When the work arrived at the step.</param>
/// <param name="StartedAt">When someone picked it up, if known.</param>
/// <param name="CompletedAt">When it was finished; null while open.</param>
/// <param name="CandidateCount">How many people could have picked it up, if known.</param>
public sealed record StepEvent(
    string CaseId,
    string LineId,
    Stage Stage,
    string Activity,
    string ActorId,
    string Role,
    AssignmentKind Assignment,
    DateTimeOffset QueuedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    StepOutcome? Outcome,
    int? CandidateCount = null);

/// <summary>An item on a purchase request; the unit of analysis.</summary>
public sealed record RequestLine(
    string CaseId,
    string LineId,
    string SiteId,
    string ItemCategory,
    LinePath Path,
    decimal Quantity,
    decimal Amount,
    string Currency);

/// <summary>Everything known about the requests up to <paramref name="AsOf"/>.</summary>
public sealed record EventLog(
    IReadOnlyList<RequestLine> Lines,
    IReadOnlyList<StepEvent> Events,
    DateTimeOffset AsOf);
