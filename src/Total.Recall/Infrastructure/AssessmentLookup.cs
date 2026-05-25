using Total.Recall.Models;

namespace Total.Recall.Infrastructure;

/// <summary>
/// Shared helpers for looking up the latest assessment per class and for
/// trying both the full / bare-nested name forms (Cobertura uses
/// <c>Parent/Nested</c>; assessments are often recorded under <c>Nested</c>).
/// </summary>
internal static class AssessmentLookup
{
    /// <summary>
    /// Build a "latest assessment per class" dictionary from a flat append-only
    /// list. Iterates in order so the last entry wins (matches the existing
    /// assessment-deduplication convention).
    /// </summary>
    internal static Dictionary<string, Assessment> BuildLatest(List<Assessment> all)
    {
        var latest = new Dictionary<string, Assessment>(StringComparer.OrdinalIgnoreCase);
        foreach (var a in all)
            latest[a.Class] = a;
        return latest;
    }

    /// <summary>
    /// Try to find an assessment for a class. Tries the full name first, then
    /// the bare nested name (Cobertura uses <c>Parent/Nested</c> but
    /// assessments are often recorded as <c>Nested</c>).
    /// </summary>
    internal static Assessment? TryGet(
        Dictionary<string, Assessment> assessments, string className, string bareName)
    {
        if (assessments.TryGetValue(className, out var assessment))
            return assessment;
        if (bareName != className && assessments.TryGetValue(bareName, out assessment))
            return assessment;
        return null;
    }
}
