namespace FlowSight.Synthetic;

/// <param name="Seed">Same seed, same dataset.</param>
/// <param name="Requests">Number of purchase requests.</param>
/// <param name="Start">First day requests can arrive. Defaults to 2025-11-03.</param>
/// <param name="Months">Length of the arrival period.</param>
/// <param name="AsOf">Cut-off; defaults to 08:00 on the first working day on or after <c>Start + Months</c>.</param>
public sealed record SyntheticOptions(
    int Seed = 42,
    int Requests = 2000,
    DateOnly? Start = null,
    int Months = 12,
    DateTimeOffset? AsOf = null)
{
    public DateOnly StartDate => Start ?? new DateOnly(2025, 11, 3);

    /// <summary>Day the injected process change takes effect: invoices stop going to a shared queue
    /// and are routed to their site's clerk. Seven months after <see cref="StartDate"/>.</summary>
    public DateOnly ChangeDate => StartDate.AddMonths(7);
}
