using ArchUnitNET.xUnit;
using MediatR;
using PaymentSystem.Features.PaymentOrders;
using PaymentSystem.Shared.Pipelines;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace PaymentArchitecture.Tests;

// TransactionBehavior only wraps ITransactionalRequest, so a Command that forgets the marker writes with no
// transaction and nothing fails. The slice convention names every write Command and every read Query; these hold the
// marker to that naming.
public class TransactionTest : BaseTest
{
    // Reflected nested names may carry the declaring type ("AddPaymentMethod+Command"), so match the last segment.
    private const string CommandName = @"(^|\+)Command$";
    private const string QueryName = @"(^|\+)Query$";

    // The two exceptions share one reason: each calls a PSP and must not hold a database transaction open across that
    // call. StartCheckout's single save is atomic on its own; SubmitPaymentMethod's outcome is written by
    // RecordPaymentOutcome in its own transaction. A new exception needs that reason.
    [Fact]
    public void Commands_AreTransactionalRequests()
    {
        Types().That().ImplementInterface(typeof(IRequest<>))
            .And().HaveNameMatching(CommandName)
            .And().AreNot(typeof(StartCheckout.Command), typeof(SubmitPaymentMethod.Command))
            .Should().ImplementInterface(typeof(ITransactionalRequest))
            .Check(Architecture);
    }

    [Fact]
    public void PspCommands_AreNotTransactionalRequests()
    {
        Types().That().Are(typeof(StartCheckout.Command), typeof(SubmitPaymentMethod.Command))
            .Should().NotImplementInterface(typeof(ITransactionalRequest))
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
