using ArchUnitNET.xUnit;
using MediatR;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Pipelines;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PaymentArchitecture.Tests;

// TransactionBehavior only wraps ITransactionalRequest, so a Command that forgets the marker writes with
// no transaction and nothing fails. The slice convention names every write `Command` and every read
// `Query`; these hold the marker to that naming.
public class TransactionTest : BaseTest
{
    // Reflected nested names may carry the declaring type ("RequestPayment+Command"), so match the last
    // segment rather than the whole name.
    private const string CommandName = @"(^|\+)Command$";
    private const string QueryName = @"(^|\+)Query$";

    // The one exception: SubmitPaymentMethod's charge calls the PSP and must not hold a transaction while it
    // does. It writes nothing itself — RecordOutcome.Command does, and is held to the rule.
    [Fact]
    public void Commands_AreTransactionalRequests()
    {
        Types().That().ImplementInterface(typeof(IRequest<>))
            .And().HaveNameMatching(CommandName)
            .And().AreNot(typeof(SubmitPaymentMethod.Command))
            .Should().ImplementInterface(typeof(ITransactionalRequest))
            .Check(Architecture);
    }

    [Fact]
    public void Queries_AreNotTransactionalRequests()
    {
        Types().That().ImplementInterface(typeof(IRequest<>))
            .And().HaveNameMatching(QueryName)
            .Should().NotImplementInterface(typeof(ITransactionalRequest))
            .Check(Architecture);
    }

    [Fact]
    public void TransactionalRequests_AreNamedCommand()
    {
        Types().That().ImplementInterface(typeof(ITransactionalRequest))
            .And().AreNot(typeof(ITransactionalRequest))
            .Should().HaveNameMatching(CommandName)
            .Check(Architecture);
    }
}
