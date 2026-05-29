using Total.Recall.Models;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Heuristic testability score (0.0 = unmockable / 1.0 = trivially testable) derived
/// from a type's metadata. Drives the <c>testabilityScore</c> field of <see cref="CoverageGap"/>.
/// </summary>
internal static class TestabilityClassifier
{
    /// <summary>
    /// Map a <see cref="TypeRecord"/> to its testability score. Returns null when the type is missing.
    /// </summary>
    public static double? Score(TypeRecord? type)
    {
        if (type is null) return null;

        if (type.IsAbstract || type.IsInterface)
            return 0.2;          // cannot instantiate directly

        if (type.IsStatic)
            return 0.55;         // testable but needs special handling for shared state

        var maxCtorParams = type.Constructors.Count > 0
            ? type.Constructors.Max(c => c.Params.Count)
            : 0;

        return maxCtorParams switch
        {
            0 => 0.95,           // parameterless = trivial
            <= 3 => 0.85,        // small ctor = easy
            <= 6 => 0.55,        // medium DI cost
            _ => 0.2,            // heavy DI = hard
        };
    }
}
