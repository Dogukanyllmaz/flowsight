using System.Text.Json;
using System.Text.Json.Serialization;

namespace FlowSight.Core.Export;

// Requester-facing contract. It must never carry actor ids, person names or individual roles.

public sealed record StageView(
    string Stage,
    string State,
    DateTimeOffset? EnteredAt,
    DateTimeOffset? LeftAt,
    double WorkingDays,
    double CalendarDays,
    double? TypicalWorkingDays);

/// <summary>Why a line is waiting: a code the UI turns into a sentence, with invariant-formatted parameters.</summary>
public sealed record WaitingReasonView(string Code, IReadOnlyDictionary<string, string> Params);

public sealed record LineView(
    string LineId,
    string ItemCategory,
    string Path,
    string Status,
    string? CurrentStage,
    IReadOnlyList<StageView> Stages,
    IReadOnlyList<WaitingReasonView> WaitingReasons);

public sealed record RequestView(
    string CaseId,
    string Status,
    string? CurrentStage,
    double TotalWorkingDays,
    double? SlowerThanPercent,
    IReadOnlyList<StageView> Stages,
    IReadOnlyList<LineView> Lines,
    DateTimeOffset AsOf,
    IReadOnlyList<WaitingReasonView> WaitingReasons);

public sealed record RequestIndexEntry(string CaseId, string Status, string? CurrentStage);

public sealed record DemoMeta(DateTimeOffset AsOf, int Seed, int Requests, int MinutesPerDay, IReadOnlyList<string> Stages);

public static class JsonDefaults
{
    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    /// <summary>Enum name in the contract's casing, e.g. <c>GoodsReceipt</c> → <c>goodsReceipt</c>.</summary>
    public static string Name<T>(T value) where T : struct, Enum => JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
}
