using FlowSight.Core.Journeys;
using FlowSight.Core.Model;
using FlowSight.Core.Reasons;
using static FlowSight.Tests.TestCalendar;

namespace FlowSight.Tests;

public class WaitingReasonTests
{
    private const string AsOf = "2026-10-22T12:00+03:00";
    private static readonly RequestLine Line = new("R1", "L1", "Site North", "Spare parts", LinePath.Goods, 10, 100, "EUR");

    private static StepEvent Ev(Stage stage, string activity, string queued, string? completed, StepOutcome? outcome = StepOutcome.Done,
        string actor = "A-01", AssignmentKind assignment = AssignmentKind.Role, int? candidates = null) =>
        new("R1", "L1", stage, activity, actor, "Role", assignment, T(queued), null,
            completed is null ? null : T(completed), completed is null ? null : outcome, candidates);

    private static DocumentLink Link(DocumentType type, decimal quantity, decimal amount) =>
        new("R1", "L1", type, $"{type}-1", quantity, amount, T("2026-10-19T09:00+03:00"));

    private static IReadOnlyList<WaitingReason> Reasons(StepEvent[] events, DocumentLink[]? links = null, Absence[]? absences = null)
    {
        var log = new EventLog([Line], events, T(AsOf), links ?? [], absences ?? []);
        var journey = JourneyBuilder.Build(log, Create()).Single();
        return WaitingReasons.For(journey, log, ProcessMapTests.Sample());
    }

    private static readonly StepEvent[] UpToInvoice =
    [
        Ev(Stage.Request, "Create request", "2026-10-19T08:00+03:00", "2026-10-19T09:00+03:00"),
        Ev(Stage.Approval, "Line manager approval", "2026-10-19T09:00+03:00", "2026-10-19T10:00+03:00", StepOutcome.Approved),
        Ev(Stage.Purchasing, "Sourcing and quotes", "2026-10-19T10:00+03:00", "2026-10-19T11:00+03:00"),
        Ev(Stage.Order, "Create purchase order", "2026-10-19T11:00+03:00", "2026-10-19T12:00+03:00"),
        Ev(Stage.GoodsReceipt, "Goods receipt", "2026-10-19T12:00+03:00", "2026-10-19T13:00+03:00")
    ];

    private static StepEvent[] With(params StepEvent[] more) => [.. UpToInvoice, .. more];

    [Fact]
    public void AbsentAssignee_WithoutDelegate()
    {
        var reasons = Reasons(
            With(Ev(Stage.Invoice, "Invoice matching", "2026-10-19T13:00+03:00", "2026-10-19T14:00+03:00"),
                 Ev(Stage.Payment, "Payment run", "2026-10-19T14:00+03:00", null, actor: "A-09", assignment: AssignmentKind.Person)),
            absences: [new Absence("A-09", new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 30), null)]);

        Assert.Equal("assigneeAbsent", reasons[0].Code);
        Assert.Equal("2026-10-30", reasons[0].Params["untilDate"]);
    }

    [Fact]
    public void AbsentAssignee_WithDelegate_IsNotAReason()
    {
        var reasons = Reasons(
            With(Ev(Stage.Invoice, "Invoice matching", "2026-10-19T13:00+03:00", "2026-10-19T14:00+03:00"),
                 Ev(Stage.Payment, "Payment run", "2026-10-19T14:00+03:00", null, actor: "A-09")),
            absences: [new Absence("A-09", new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 30), "A-10")]);

        Assert.DoesNotContain(reasons, r => r.Code == "assigneeAbsent");
    }

    [Fact]
    public void ReturnedForCorrection()
    {
        var reasons = Reasons([
            Ev(Stage.Request, "Create request", "2026-10-19T08:00+03:00", "2026-10-19T09:00+03:00"),
            Ev(Stage.Approval, "Line manager approval", "2026-10-19T09:00+03:00", "2026-10-19T10:00+03:00", StepOutcome.Approved),
            Ev(Stage.Purchasing, "Sourcing and quotes", "2026-10-19T10:00+03:00", "2026-10-20T10:00+03:00", StepOutcome.Returned),
            Ev(Stage.Purchasing, "Clarify specification", "2026-10-20T10:00+03:00", null)]);

        Assert.Equal(["returnedForCorrection"], reasons.Select(r => r.Code));
    }

    [Theory]
    [InlineData(106, "6.0")]
    [InlineData(95, "-5.0")]
    public void PriceVariance_Signed(decimal invoiced, string percent)
    {
        var reasons = Reasons(
            With(Ev(Stage.Invoice, "Invoice matching", "2026-10-19T13:00+03:00", null, assignment: AssignmentKind.FieldRouted)),
            links: [Link(DocumentType.Order, 10, 100), Link(DocumentType.Receipt, 10, 100), Link(DocumentType.Invoice, 10, invoiced)]);

        var variance = Assert.Single(reasons);
        Assert.Equal("priceVariance", variance.Code);
        Assert.Equal(percent, variance.Params["percent"]);
    }

    [Fact]
    public void SmallDifference_IsNotAVariance()
    {
        var reasons = Reasons(
            With(Ev(Stage.Invoice, "Invoice matching", "2026-10-19T13:00+03:00", null, assignment: AssignmentKind.FieldRouted)),
            links: [Link(DocumentType.Order, 10, 100), Link(DocumentType.Receipt, 10, 100), Link(DocumentType.Invoice, 10, 101)]);

        Assert.Equal(["unknown"], reasons.Select(r => r.Code));
    }

    [Fact]
    public void PartialDelivery()
    {
        var reasons = Reasons(
            [.. UpToInvoice[..4], Ev(Stage.GoodsReceipt, "Goods receipt", "2026-10-19T12:00+03:00", null)],
            links: [Link(DocumentType.Order, 10, 100), Link(DocumentType.Receipt, 6, 60)]);

        var partial = Assert.Single(reasons);
        Assert.Equal("partialDelivery", partial.Code);
        Assert.Equal(("6", "10"), (partial.Params["received"], partial.Params["ordered"]));
    }

    [Fact]
    public void NothingReceivedYet_IsNotAPartialDelivery()
    {
        var reasons = Reasons(
            [.. UpToInvoice[..4], Ev(Stage.GoodsReceipt, "Goods receipt", "2026-10-19T12:00+03:00", null)],
            links: [Link(DocumentType.Order, 10, 100)]);

        Assert.Equal(["unknown"], reasons.Select(r => r.Code));
    }

    [Fact]
    public void SharedQueue_ReportsCandidates()
    {
        var reasons = Reasons(With(Ev(Stage.Invoice, "Invoice matching", "2026-10-19T13:00+03:00", null,
            assignment: AssignmentKind.GroupQueue, candidates: 6)));

        var queue = Assert.Single(reasons);
        Assert.Equal(("sharedQueue", "6"), (queue.Code, queue.Params["candidates"]));
    }

    [Fact]
    public void SharedQueue_WithoutQueueSize_HasNoCandidatesParam()
    {
        var reasons = Reasons(With(Ev(Stage.Invoice, "Invoice matching", "2026-10-19T13:00+03:00", null,
            assignment: AssignmentKind.GroupQueue)));

        var queue = Assert.Single(reasons);
        Assert.Equal("sharedQueue", queue.Code);
        Assert.False(queue.Params.ContainsKey("candidates"));
    }

    [Fact]
    public void WaitingForBatch()
    {
        var reasons = Reasons(With(
            Ev(Stage.Invoice, "Invoice matching", "2026-10-19T13:00+03:00", "2026-10-19T14:00+03:00"),
            Ev(Stage.Payment, "Payment run", "2026-10-19T14:00+03:00", null, assignment: AssignmentKind.Person)));

        var batch = Assert.Single(reasons);
        Assert.Equal(("waitingForBatch", "friday"), (batch.Code, batch.Params["day"]));
    }

    [Fact]
    public void NoRuleMatches_IsUnknown()
    {
        var reasons = Reasons([.. UpToInvoice[..3], Ev(Stage.Order, "Create purchase order", "2026-10-19T11:00+03:00", null)]);

        Assert.Equal(["unknown"], reasons.Select(r => r.Code));
    }

    [Fact]
    public void ClosedLine_HasNoReasons()
    {
        var reasons = Reasons(With(
            Ev(Stage.Invoice, "Invoice matching", "2026-10-19T13:00+03:00", "2026-10-19T14:00+03:00"),
            Ev(Stage.Payment, "Payment run", "2026-10-19T14:00+03:00", "2026-10-19T15:00+03:00")));

        Assert.Empty(reasons);
    }

    [Fact]
    public void SeveralReasons_KeepOrder()
    {
        var reasons = Reasons(
            With(Ev(Stage.Invoice, "Invoice matching", "2026-10-19T13:00+03:00", null, assignment: AssignmentKind.GroupQueue, candidates: 6)),
            links: [Link(DocumentType.Order, 10, 100), Link(DocumentType.Receipt, 10, 100), Link(DocumentType.Invoice, 10, 110)]);

        Assert.Equal(["priceVariance", "sharedQueue"], reasons.Select(r => r.Code));
    }
}
