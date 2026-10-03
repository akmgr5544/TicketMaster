using ArchUnitNET.xUnit;
using MediatR;
using PaymentSystem.Features.Checkouts;
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

    // The exceptions share one reason: each calls the PSP and must not hold a transaction while it does, and
    // writes nothing itself. SubmitPaymentMethod's charge is written by RecordOutcome.Command, RefundCheckout's
    // refunds by RecordRefund.Command, and both of those are held to the rule. A new exception needs that reason.
    [Fact]
    public void Commands_AreTransactionalRequests()
    {
        Types().That().ImplementInterface(typeof(IRequest<>))
            .And().HaveNameMatching(CommandName)
            .And().AreNot(typeof(SubmitPaymentMethod.Command), typeof(RefundCheckout.Command))
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
