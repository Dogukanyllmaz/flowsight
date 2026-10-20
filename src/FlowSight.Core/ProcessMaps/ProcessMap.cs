using FlowSight.Core.Model;

namespace FlowSight.Core.ProcessMaps;

/// <summary>How a step reaches people: person, role, group-queue, field-routed, manager_of, initiator, field or system.</summary>
public sealed record MapAssignee(string Type, string Value);

/// <param name="Name">Equals the <see cref="StepEvent.Activity"/> of the step's events.</param>
/// <param name="ReturnTo">Step that work is sent back to for rework, if any.</param>
/// <param name="WeeklyOn">Set when the step is processed in a weekly batch on this day.</param>
public sealed record MapStep(
    string Id,
    string Name,
    string Type,
    string Lane,
    Stage? Stage,
    IReadOnlyList<string> Next,
    MapAssignee? Assignee,
    double? SlaHours,
    bool Escalation,
    string? ReturnTo,
    DayOfWeek? WeeklyOn);

/// <summary>A dated process change. The "before" values applied until <paramref name="Date"/>.</summary>
public sealed record MapChange(DateOnly Date, string Step, string Description, MapAssignee? AssigneeBefore, double? SlaHoursBefore);

public sealed record ProcessMap(
    string Code,
    string Name,
    IReadOnlyList<string> Lanes,
    IReadOnlyList<MapStep> Steps,
    IReadOnlyList<MapChange> Changes,
    double? DefaultSlaHours)
{
    /// <summary>The map as it was at <paramref name="t"/>: every change dated after that day is reverted.</summary>
    public ProcessMap AsOf(DateTimeOffset t)
    {
        var day = DateOnly.FromDateTime(t.DateTime);
        var steps = Steps.ToDictionary(s => s.Id);
        foreach (var change in Changes.Where(c => c.Date > day).OrderByDescending(c => c.Date))
        {
            var step = steps[change.Step];
            steps[change.Step] = step with
            {
                Assignee = change.AssigneeBefore ?? step.Assignee,
                SlaHours = change.SlaHoursBefore ?? step.SlaHours
            };
        }
        return this with { Steps = Steps.Select(s => steps[s.Id]).ToList() };
    }

    public MapStep? StepForActivity(string activity) => Steps.FirstOrDefault(s => s.Name == activity);

    /// <summary>Longest runs of consecutive approval steps (following <c>next</c>) with at least <paramref name="minLength"/> steps.</summary>
    public IReadOnlyList<IReadOnlyList<MapStep>> ApprovalChains(int minLength)
    {
        var byId = Steps.ToDictionary(s => s.Id);
        bool IsApproval(string id) => byId.TryGetValue(id, out var s) && s.Type == "approval";
        var approvalPredecessors = Steps.SelectMany(s => s.Next.Select(n => (From: s, To: n)))
            .Where(e => e.From.Type == "approval").Select(e => e.To).ToHashSet();

        List<MapStep> Longest(MapStep step, HashSet<string> seen)
        {
            var best = new List<MapStep>();
            foreach (var next in step.Next.Where(n => IsApproval(n) && !seen.Contains(n)))
            {
                var candidate = Longest(byId[next], [.. seen, next]);
                if (candidate.Count > best.Count) best = candidate;
            }
            return [step, .. best];
        }

        return Steps
            .Where(s => s.Type == "approval" && !approvalPredecessors.Contains(s.Id))
            .Select(s => (IReadOnlyList<MapStep>)Longest(s, [s.Id]))
            .Where(c => c.Count >= minLength)
            .ToList();
    }
}

public sealed class ProcessMapException(string message, Exception? inner = null) : Exception(message, inner);
