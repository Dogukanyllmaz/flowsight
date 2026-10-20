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

public enum DocumentType { Order, Receipt, Invoice }

/// <summary>An ERP document linked to a request line: what was ordered, received or invoiced, and when it arrived.</summary>
public sealed record DocumentLink(
    string CaseId,
    string LineId,
    DocumentType Type,
    string DocumentId,
    decimal Quantity,
    decimal Amount,
    DateTimeOffset At);

/// <summary>A planned absence (inclusive dates) and who stands in, if anyone.</summary>
public sealed record Absence(string ActorId, DateOnly From, DateOnly To, string? DelegateId);

/// <summary>Everything known about the requests up to <paramref name="AsOf"/>.
/// Document links and absences are optional; analyses that need them report "insufficient data" without them.</summary>
public sealed record EventLog(
    IReadOnlyList<RequestLine> Lines,
    IReadOnlyList<StepEvent> Events,
    DateTimeOffset AsOf,
    IReadOnlyList<DocumentLink>? Links = null,
    IReadOnlyList<Absence>? Absences = null);
