using MediatR;

namespace BookingArchitecture.Tests;

/// <summary>
/// `Bookings.Application` is organised by type then area: a request under `Commands` or `Queries`, its
/// handler under `CommandHandlers/<Area>` or `QueryHandlers/<Area>`. These guard that layout, and
/// replace a colocation rule that the layout deliberately does not satisfy.
/// </summary>
public class LayoutTest : BaseTest
{
    private const string CommandHandlerRoot = "Bookings.Application.CommandHandlers";
    private const string QueryHandlerRoot = "Bookings.Application.QueryHandlers";
    private const string CommandRoot = "Bookings.Application.Commands";
    private const string QueryRoot = "Bookings.Application.Queries";

    /// <summary>
    /// A handler's suffix has to agree with where it lives, so a query handler cannot sit among the
    /// command handlers.
    /// </summary>
    [Theory]
    [MemberData(nameof(GetHandlers))]
    public void Handlers_ShouldResideUnder_TheRootTheirNameClaims(Type handlerType)
    {
        var expectedRoot = handlerType.Name.Contains("QueryHandler", StringComparison.Ordinal)
            ? QueryHandlerRoot
            : CommandHandlerRoot;

        Assert.True(handlerType.Namespace?.StartsWith(expectedRoot, StringComparison.Ordinal) is true,
            $"{handlerType.Name} should reside under {expectedRoot} but is in {handlerType.Namespace}");
    }

    [Theory]
    [MemberData(nameof(GetRequests))]
    public void Requests_ShouldResideUnder_CommandsOrQueries(Type requestType)
    {
        var isPlaced = requestType.Namespace?.StartsWith(CommandRoot, StringComparison.Ordinal) is true
                       || requestType.Namespace?.StartsWith(QueryRoot, StringComparison.Ordinal) is true;

        Assert.True(isPlaced,
            $"{requestType.Name} should reside under {CommandRoot} or {QueryRoot} " +
            $"but is in {requestType.Namespace}");
    }

    public static TheoryData<Type> GetHandlers() =>
        Collect([typeof(IRequestHandler<>), typeof(IRequestHandler<,>)]);

    public static TheoryData<Type> GetRequests() =>
        Collect([typeof(IRequest), typeof(IRequest<>)]);

    /// <summary>
    /// Generic type definitions are skipped: the `IdentifiedCommand` pair in `Abstractions` wraps
    /// other commands rather than being a feature of its own, so the layout does not apply to it.
    /// </summary>
    private static TheoryData<Type> Collect(Type[] markers)
    {
        var matches = new TheoryData<Type>();

        var candidates = ApplicationAssembly.GetTypes()
            .Where(type => type is { IsAbstract: false, IsGenericTypeDefinition: false })
            .Where(type => type.DeclaringType is null);

        foreach (var candidate in candidates)
        {
            var implementsMarker = candidate.GetInterfaces().Any(@interface =>
                markers.Contains(@interface)
                || (@interface.IsGenericType && markers.Contains(@interface.GetGenericTypeDefinition())));

            if (implementsMarker)
                matches.Add(candidate);
        }

        return matches;
    }
}
