using System.Collections;
using System.Reflection;
using System.Text.Json.Serialization.Metadata;
using Events.Domain.Entities;

namespace Events.Cosmos.Serialization;

/// <summary>
/// Teaches System.Text.Json how to rebuild encapsulated domain entities without putting a single
/// serialization attribute in Events.Domain.
/// <para>
/// Two things need teaching. Entities are constructed through a private parameterless constructor
/// so that loading a stored document never re-runs creation invariants — an event that has already
/// happened must still load. And properties the domain exposes read-only (private setters, or
/// collections projected over a backing field) are written to directly, because the public surface
/// deliberately offers no way to set them.
/// </para>
/// </summary>
internal static class DomainBinding
{
    private static readonly Assembly DomainAssembly = typeof(Venue).Assembly;

    public static void Apply(JsonTypeInfo typeInfo)
    {
        if (typeInfo.Kind != JsonTypeInfoKind.Object || typeInfo.Type.Assembly != DomainAssembly)
            return;

        UseRehydrationConstructor(typeInfo);

        foreach (var property in typeInfo.Properties)
        {
            if (property.Set is not null || property.AttributeProvider is not PropertyInfo reflected)
                continue;

            if (reflected.GetSetMethod(nonPublic: true) is { } setter)
            {
                property.Set = (target, value) => setter.Invoke(target, [value]);
                continue;
            }

            if (BackingFieldFor(typeInfo.Type, reflected.Name) is { } backingField)
                property.Set = (target, value) => RefillCollection(backingField, target, value);
        }
    }

    // S3011: reaching non-public members is the entire purpose of this resolver. Rehydrating an
    // entity means writing state the domain deliberately exposes no public way to write.
#pragma warning disable S3011
    private static void UseRehydrationConstructor(JsonTypeInfo typeInfo)
    {
        var constructor = typeInfo.Type.GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (constructor is not null)
            typeInfo.CreateObject = () => constructor.Invoke(null);
    }

    private static FieldInfo? BackingFieldFor(Type owner, string propertyName) =>
        owner.GetField($"_{char.ToLowerInvariant(propertyName[0])}{propertyName[1..]}",
            BindingFlags.Instance | BindingFlags.NonPublic);
#pragma warning restore S3011

    /// <summary>
    /// Fills the existing list rather than assigning the field, because the backing fields are
    /// readonly and are already initialised by the rehydration constructor.
    /// </summary>
    private static void RefillCollection(FieldInfo backingField, object target, object? value)
    {
        if (backingField.GetValue(target) is not IList destination)
            return;

        destination.Clear();

        if (value is not IEnumerable items)
            return;

        foreach (var item in items)
            destination.Add(item);
    }
}
