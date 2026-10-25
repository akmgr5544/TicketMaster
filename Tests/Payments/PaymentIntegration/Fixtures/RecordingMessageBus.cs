using System.Collections.Concurrent;
using System.Reflection;
using Wolverine;

namespace PaymentIntegration.Fixtures;

// What a slice handed to Wolverine. Only SubmitPaymentMethod uses the bus — to invoke RecordPaymentOutcome — and that
// handler has its own tests, so here it is enough to see what was invoked.
public sealed class MessageBusLog
{
    private readonly ConcurrentQueue<object> _invoked = new();

    public IReadOnlyCollection<object> Invoked => _invoked;

    public void Add(object message) => _invoked.Enqueue(message);

    public void Reset() => _invoked.Clear();
}

// A proxy rather than a hand-written class: IMessageBus has dozens of members, and every one but the first argument
// of a call is irrelevant here. Each call records its message and completes at once.
public class RecordingMessageBus : DispatchProxy
{
    private MessageBusLog _log = null!;

    public static IMessageBus Create(MessageBusLog log)
    {
        var bus = Create<IMessageBus, RecordingMessageBus>();
        ((RecordingMessageBus)(object)bus)._log = log;
        return bus;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (args is [{ } message, ..] && targetMethod!.Name is "InvokeAsync" or "PublishAsync" or "SendAsync")
            _log.Add(message);

        var returnType = targetMethod!.ReturnType;
        if (returnType == typeof(Task))
            return Task.CompletedTask;
        if (returnType == typeof(ValueTask))
            return ValueTask.CompletedTask;
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
            return typeof(Task).GetMethod(nameof(Task.FromResult))!
                .MakeGenericMethod(returnType.GetGenericArguments()[0])
                .Invoke(null, [null]);
        return returnType.IsValueType ? Activator.CreateInstance(returnType) : null;
    }
}
