using FlowSight.Core.Model;
using FlowSight.Core.ProcessMaps;
using static FlowSight.Tests.TestCalendar;

namespace FlowSight.Tests;

public class ProcessMapTests
{
    internal static string SampleYaml()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, "samples", "purchase-to-pay.yaml");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException("samples/purchase-to-pay.yaml");
    }

    internal static ProcessMap Sample() => ProcessMapYaml.Parse(SampleYaml());

    [Fact]
    public void ParsesToolkitFormatPlusExtensions()
    {
        var map = Sample();
        var invoice = map.Steps.Single(s => s.Id == "invoice-matching");

        Assert.Equal("P2P", map.Code);
        Assert.Equal(8, map.DefaultSlaHours);
        Assert.Equal(Stage.Invoice, invoice.Stage);
        Assert.Equal("field-routed", invoice.Assignee!.Type);
        Assert.Equal(Stage.GoodsReceipt, map.Steps.Single(s => s.Id == "goods-receipt").Stage);
        var change = Assert.Single(map.Changes);
        Assert.Equal(new DateOnly(2026, 6, 3), change.Date);
        Assert.Equal("group-queue", change.AssigneeBefore!.Type);
    }

    [Fact]
    public void AsOf_RevertsLaterChanges()
    {
        var map = Sample();

        Assert.Equal("group-queue", map.AsOf(T("2026-03-31T23:00+03:00")).Steps.Single(s => s.Id == "invoice-matching").Assignee!.Type);
        Assert.Equal("field-routed", map.AsOf(T("2026-09-30T23:00+03:00")).Steps.Single(s => s.Id == "invoice-matching").Assignee!.Type);
    }

    [Fact]
    public void FindsStepByActivity()
    {
        Assert.Equal("invoice-matching", Sample().StepForActivity("Invoice matching")!.Id);
        Assert.Null(Sample().StepForActivity("Something else"));
    }

    [Fact]
    public void FindsSerialApprovalChain()
    {
        var chains = Sample().ApprovalChains(3);

        var chain = Assert.Single(chains);
        Assert.Equal(["it-approval", "line-manager-approval", "budget-owner-approval"], chain.Select(s => s.Id));
    }

    [Fact]
    public void ReadsReturnEdgeAndSchedule()
    {
        var map = Sample();
        var payment = map.Steps.Single(s => s.Id == "payment-run");

        Assert.Equal("clarify-spec", map.Steps.Single(s => s.Id == "sourcing").ReturnTo);
        Assert.Equal(DayOfWeek.Friday, payment.WeeklyOn);
        Assert.Null(payment.SlaHours);
    }

    [Fact]
    public void AcceptsAToolkitMapWithoutExtensions()
    {
        var map = ProcessMapYaml.Parse("""
            code: PR
            name: Purchase request
            lanes: [Requester, Finance]
            steps:
              - {id: S0, name: Request opened, type: start, lane: Requester, next: A1}
              - id: A1
                name: Spend approval
                type: approval
                lane: Finance
                assignee: {type: person, value: "F-002"}
                next: E1
              - {id: E1, name: Closed, type: end, lane: Requester}
            """);

        Assert.Equal(3, map.Steps.Count);
        Assert.All(map.Steps, s => Assert.Null(s.Stage));
        Assert.Empty(map.Changes);
    }

    [Theory]
    [InlineData("steps:\n  - {id: A, name: x, type: task, lane: L, next: missing}\nlanes: [L]\ncode: X\nname: X", "missing")]
    [InlineData("steps:\n  - {id: A, name: x, type: task, lane: L, stage: shipping}\nlanes: [L]\ncode: X\nname: X", "shipping")]
    [InlineData("steps:\n  - {id: A, name: x, type: task, lane: Nowhere}\nlanes: [L]\ncode: X\nname: X", "Nowhere")]
    [InlineData("code: X\nname: [unclosed", "line")]
    public void InvalidMap_FailsWithMessage(string yaml, string mentioned)
    {
        var error = Assert.Throws<ProcessMapException>(() => ProcessMapYaml.Parse(yaml));
        Assert.Contains(mentioned, error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
