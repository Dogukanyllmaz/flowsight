using System.Globalization;
using FlowSight.Core.Model;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace FlowSight.Core.ProcessMaps;

/// <summary>Reads a process map from YAML text (the caller loads the file). Validates links, lanes and stages.</summary>
public static class ProcessMapYaml
{
    private static readonly string[] StepTypes = ["start", "task", "approval", "system", "decision", "end"];
    private static readonly string[] AssigneeTypes = ["person", "role", "group-queue", "field-routed", "manager_of", "initiator", "field", "system"];

    public static ProcessMap Parse(string yaml)
    {
        MapDto dto;
        try
        {
            dto = new DeserializerBuilder()
                .WithNamingConvention(UnderscoredNamingConvention.Instance)
                .IgnoreUnmatchedProperties()
                .Build()
                .Deserialize<MapDto>(yaml) ?? throw new ProcessMapException("The map is empty.");
        }
        catch (YamlException e)
        {
            throw new ProcessMapException($"Invalid YAML at line {e.Start.Line}: {e.Message}", e);
        }

        var lanes = dto.Lanes ?? [];
        var steps = (dto.Steps ?? []).Select(s => ToStep(s, lanes)).ToList();
        var ids = steps.Select(s => s.Id).ToHashSet();

        var duplicate = steps.GroupBy(s => s.Id).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null) throw new ProcessMapException($"Step id '{duplicate.Key}' is used twice.");
        foreach (var step in steps)
        {
            foreach (var next in step.Next.Where(n => !ids.Contains(n)))
                throw new ProcessMapException($"Step '{step.Id}': next step '{next}' does not exist.");
            if (step.ReturnTo is { } back && !ids.Contains(back))
                throw new ProcessMapException($"Step '{step.Id}': return_to step '{back}' does not exist.");
        }

        var changes = (dto.Changes ?? []).Select(c =>
        {
            if (c.Step is null || !ids.Contains(c.Step)) throw new ProcessMapException($"Change on {c.Date}: step '{c.Step}' does not exist.");
            if (!DateOnly.TryParse(c.Date, CultureInfo.InvariantCulture, out var date))
                throw new ProcessMapException($"Change on step '{c.Step}': date '{c.Date}' is not yyyy-MM-dd.");
            return new MapChange(date, c.Step, c.Description ?? "", ToAssignee(c.Before?.Assignee, c.Step), c.Before?.SlaHours);
        }).ToList();

        return new ProcessMap(dto.Code ?? "", dto.Name ?? "", lanes, steps, changes, dto.DefaultSlaHours);
    }

    private static MapStep ToStep(StepDto s, IReadOnlyList<string> lanes)
    {
        var id = s.Id ?? throw new ProcessMapException("A step has no id.");
        var type = s.Type ?? "task";
        if (!StepTypes.Contains(type)) throw new ProcessMapException($"Step '{id}': unknown type '{type}'.");
        var lane = s.Lane ?? lanes.FirstOrDefault() ?? "";
        if (!lanes.Contains(lane)) throw new ProcessMapException($"Step '{id}': lane '{lane}' is not declared in lanes.");

        return new MapStep(
            id,
            s.Name ?? id,
            type,
            lane,
            s.Stage is null ? null : ParseStage(s.Stage, id),
            ToList(s.Next),
            ToAssignee(s.Assignee, id),
            s.SlaHours,
            s.Escalation ?? false,
            s.ReturnTo,
            s.Schedule is null ? null : ParseSchedule(s.Schedule, id));
    }

    private static Stage ParseStage(string value, string id)
    {
        var key = value.Replace("_", "").Replace("-", "");
        return Enum.TryParse<Stage>(key, ignoreCase: true, out var stage) && Enum.IsDefined(stage)
            ? stage
            : throw new ProcessMapException($"Step '{id}': unknown stage '{value}'.");
    }

    private static DayOfWeek ParseSchedule(string value, string id)
    {
        const string prefix = "weekly:";
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && Enum.TryParse<DayOfWeek>(value[prefix.Length..], ignoreCase: true, out var day)
            ? day
            : throw new ProcessMapException($"Step '{id}': schedule '{value}' is not 'weekly:<day>'.");
    }

    private static MapAssignee? ToAssignee(AssigneeDto? a, string id)
    {
        if (a is null) return null;
        var type = a.Type ?? throw new ProcessMapException($"Step '{id}': assignee has no type.");
        if (!AssigneeTypes.Contains(type)) throw new ProcessMapException($"Step '{id}': unknown assignee type '{type}'.");
        return new MapAssignee(type, a.Value ?? "");
    }

    private static IReadOnlyList<string> ToList(object? next) => next switch
    {
        null => [],
        string single => [single],
        IEnumerable<object> many => many.Select(o => o.ToString() ?? "").ToList(),
        _ => [next.ToString() ?? ""]
    };

    private sealed class MapDto
    {
        public string? Code { get; set; }
        public string? Name { get; set; }
        public double? DefaultSlaHours { get; set; }
        public List<string>? Lanes { get; set; }
        public List<StepDto>? Steps { get; set; }
        public List<ChangeDto>? Changes { get; set; }
    }

    private sealed class StepDto
    {
        public string? Id { get; set; }
        public string? Name { get; set; }
        public string? Type { get; set; }
        public string? Lane { get; set; }
        public string? Stage { get; set; }
        public object? Next { get; set; }
        public AssigneeDto? Assignee { get; set; }
        public double? SlaHours { get; set; }
        public bool? Escalation { get; set; }
        public string? ReturnTo { get; set; }
        public string? Schedule { get; set; }
    }

    private sealed class AssigneeDto
    {
        public string? Type { get; set; }
        public string? Value { get; set; }
    }

    private sealed class ChangeDto
    {
        public string? Date { get; set; }
        public string? Step { get; set; }
        public string? Description { get; set; }
        public BeforeDto? Before { get; set; }
    }

    private sealed class BeforeDto
    {
        public AssigneeDto? Assignee { get; set; }
        public double? SlaHours { get; set; }
    }
}
