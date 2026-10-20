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
