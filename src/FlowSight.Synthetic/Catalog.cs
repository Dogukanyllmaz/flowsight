using FlowSight.Core.Model;

namespace FlowSight.Synthetic;

/// <summary>
/// Everything in this file is invented for the demo: sites, roles, people, categories and timing.
/// None of it is derived from any real organisation's data.
/// </summary>
internal static class Catalog
{
    public static readonly string[] Sites = ["Site North", "Site South", "Site East"];

    public static readonly (string Category, LinePath Path)[] Categories =
    [
        ("Spare parts", LinePath.Goods),
        ("Consumables", LinePath.Goods),
        ("Safety equipment", LinePath.Goods),
        ("IT hardware", LinePath.Goods),
        ("Maintenance service", LinePath.Service),
        ("Transport service", LinePath.Service)
    ];

    public const string Requester = "Requester";
    public const string LineManager = "Line manager";
    public const string BudgetOwner = "Budget owner";
    public const string Buyer = "Buyer";
    public const string WarehouseClerk = "Warehouse clerk";
    public const string AccountsPayable = "Accounts payable";
    public const string Treasury = "Treasury";

    /// <summary>People per role at each site.</summary>
    private static readonly (string Role, int PerSite)[] Staffing =
    [
        (Requester, 8), (LineManager, 3), (BudgetOwner, 1), (Buyer, 2),
        (WarehouseClerk, 2), (AccountsPayable, 2), (Treasury, 1)
    ];

    /// <summary>Pseudonymous actors <c>A-01</c>… grouped by (site, role), in a fixed order.</summary>
    public static readonly IReadOnlyDictionary<(string Site, string Role), string[]> Actors = BuildActors();

    /// <summary>Request lines above this amount also need the budget owner's approval.</summary>
    public const decimal BudgetApprovalThreshold = 5_000m;

    /// <summary>A step template: median duration in working minutes and the share of it spent waiting in the queue.</summary>
    public sealed record StepTemplate(Stage Stage, string Activity, string Role, AssignmentKind Assignment, double MedianMinutes, double QueueShare);

    public static readonly StepTemplate CreateRequest = new(Stage.Request, "Create request", Requester, AssignmentKind.Person, 60, 0.0);
    public static readonly StepTemplate ManagerApproval = new(Stage.Approval, "Line manager approval", LineManager, AssignmentKind.Role, 240, 0.8);
    public static readonly StepTemplate BudgetApproval = new(Stage.Approval, "Budget owner approval", BudgetOwner, AssignmentKind.Role, 300, 0.8);
    public static readonly StepTemplate Sourcing = new(Stage.Purchasing, "Sourcing and quotes", Buyer, AssignmentKind.Role, 2_400, 0.5);
    public static readonly StepTemplate CreateOrder = new(Stage.Order, "Create purchase order", Buyer, AssignmentKind.Role, 300, 0.7);
    public static readonly StepTemplate GoodsReceipt = new(Stage.GoodsReceipt, "Goods receipt", WarehouseClerk, AssignmentKind.Role, 4_000, 0.95);
    public static readonly StepTemplate ServiceAcceptance = new(Stage.GoodsReceipt, "Service acceptance", Requester, AssignmentKind.Person, 1_500, 0.9);
    // Injected scenario (ground truth for tests): until the change date every invoice lands in one queue shared by
    // all accounts-payable clerks, who search it for their own site's invoices. Afterwards each invoice is routed
    // straight to its site's clerk.
    public static readonly StepTemplate SharedInvoiceQueue = new(Stage.Invoice, "Invoice matching", AccountsPayable, AssignmentKind.GroupQueue, 6_000, 0.9);
    public static readonly StepTemplate RoutedInvoice = new(Stage.Invoice, "Invoice matching", AccountsPayable, AssignmentKind.FieldRouted, 900, 0.7);

    /// <summary>Every accounts-payable clerk across all sites, in a fixed order.</summary>
    public static readonly string[] AllClerks = Sites.SelectMany(site => Actors[(site, AccountsPayable)]).ToArray();

    /// <summary>The clerk an invoice is routed to after the change.</summary>
    public static string RoutedClerk(string site) => Actors[(site, AccountsPayable)][0];
    public static readonly StepTemplate PaymentRun = new(Stage.Payment, "Payment run", Treasury, AssignmentKind.Role, 2_700, 0.95);

    /// <summary>Spread of step durations around their median (log-normal sigma).</summary>
    public const double DurationSigma = 0.6;

    public const double RejectionRate = 0.04;
    public const double CancellationRate = 0.02;
    public const double GoodsShare = 0.8;

    private static Dictionary<(string, string), string[]> BuildActors()
    {
        var result = new Dictionary<(string, string), string[]>();
        var next = 1;
        foreach (var site in Sites)
            foreach (var (role, perSite) in Staffing)
                result[(site, role)] = Enumerable.Range(0, perSite).Select(_ => $"A-{next++:00}").ToArray();
        return result;
    }
}
