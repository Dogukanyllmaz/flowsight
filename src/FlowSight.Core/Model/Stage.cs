namespace FlowSight.Core.Model;

/// <summary>The seven purchase-to-pay stages, in flow order.</summary>
public enum Stage
{
    Request = 1,
    Approval,
    Purchasing,
    Order,
    GoodsReceipt,
    Invoice,
    Payment
}

public static class Stages
{
    public static IReadOnlyList<Stage> All { get; } = Enum.GetValues<Stage>().OrderBy(s => s).ToArray();
}
