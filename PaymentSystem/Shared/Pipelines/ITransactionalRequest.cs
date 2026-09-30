namespace PaymentSystem.Shared.Pipelines;

// Marks a command whose handler writes. TransactionBehavior applies only to these, so a command that
// writes without it runs with no transaction and nothing says so.
public interface ITransactionalRequest;
