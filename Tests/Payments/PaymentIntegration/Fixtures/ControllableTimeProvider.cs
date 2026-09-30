namespace PaymentIntegration.Fixtures;

// Whole seconds on purpose: Postgres timestamps hold microseconds, so a clock with 100ns ticks would make
// every timestamp assertion fail on truncation rather than on the rule under test.
public sealed class ControllableTimeProvider : TimeProvider
{
    public static readonly DateTimeOffset Start = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

    private DateTimeOffset _now = Start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Reset() => _now = Start;

    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
