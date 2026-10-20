using FlowSight.Core.Model;

namespace FlowSight.Core.Journeys;

public enum StageState { NotStarted, InProgress, Completed }

public enum LineStatus { Open, Completed, Rejected, Cancelled }

/// <summary>Time a line (or request) spent in one stage.</summary>
public sealed record StageSpan(
    Stage Stage,
    StageState State,
    DateTimeOffset? EnteredAt,
    DateTimeOffset? LeftAt,
    double WorkingMinutes,
    double CalendarDays);

/// <summary>A request line's path through the seven stages. <see cref="Stages"/> always has seven entries in stage order.</summary>
public sealed record LineJourney(
    RequestLine Line,
    IReadOnlyList<StageSpan> Stages,
    Stage? CurrentStage,
    LineStatus Status,
    double TotalWorkingMinutes,
    double TotalCalendarDays);

public enum RequestStatus { Open, Completed, Rejected, Cancelled }

/// <summary>A purchase request: its lines plus a request-level view of the seven stages.</summary>
public sealed record RequestJourney(
    string CaseId,
    IReadOnlyList<LineJourney> Lines,
    IReadOnlyList<StageSpan> Stages,
    Stage? CurrentStage,
    RequestStatus Status,
    double TotalWorkingMinutes);
