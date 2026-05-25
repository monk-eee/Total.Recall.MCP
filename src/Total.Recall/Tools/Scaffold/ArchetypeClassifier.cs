using Total.Recall.Models;

namespace Total.Recall.Tools.Scaffold;

/// <summary>
/// Classifies the shape of a class under test (static helper, POCO, service, builder, ...)
/// and emits anti-pattern warnings that nudge the agent away from common mistakes.
///
/// Both methods are pure functions of a <see cref="TypeRecord"/> plus precomputed mock
/// and concrete dependency counts produced by the scaffold planner. They are kept
/// together because they share the same vocabulary about class shapes.
/// </summary>
internal static class ArchetypeClassifier
{
    /// <summary>
    /// Classify the class archetype and return a testing-strategy comment, or null if
    /// nothing distinctive applies.
    /// </summary>
    public static string? ClassifyArchetype(
        TypeRecord typeRecord,
        int mockFieldCount,
        int concreteFieldCount,
        CoverageGap? coverageGap)
    {
        if (typeRecord.IsStatic)
            return "STATIC HELPER \u2014 Pure function tests. No mocking needed. " +
                   "Test each method with representative inputs, edge cases (null, empty, boundary values), " +
                   "and verify return values with Assert.Equal/Assert.Contains.";

        if (mockFieldCount == 0 && concreteFieldCount == 0 && typeRecord.Properties.Count > 3)
            return "POCO/DATA CLASS \u2014 Test property round-trips, constructor defaults, " +
                   "and any computed properties. Verify equality/GetHashCode if it's a record.";

        if (mockFieldCount > 0 && concreteFieldCount == 0)
        {
            if (mockFieldCount >= 5)
                return $"HEAVY-DI SERVICE ({mockFieldCount} dependencies) \u2014 Focus on the most important " +
                       "interactions. Use mock.Verify sparingly (only essential side-effects). " +
                       "Consider testing method groups that share common mock setups together.";

            return $"STANDARD SERVICE ({mockFieldCount} dependencies) \u2014 Classic mock + verify pattern. " +
                   "Arrange mock returns, Act on SUT, Assert return values and Verify mock interactions.";
        }

        if (mockFieldCount > 0 && concreteFieldCount > 0)
            return $"MIXED-DI SERVICE ({mockFieldCount} mocked + {concreteFieldCount} concrete) \u2014 " +
                   "Concrete dependencies can't be mocked; use known-good values. " +
                   "Focus tests on behavior that doesn't depend on concrete dep internals.";

        if (typeRecord.Name.Contains("Builder") || typeRecord.Name.Contains("Factory")
            || typeRecord.Name.Contains("Provider") || typeRecord.Name.Contains("Creator"))
            return "BUILDER/FACTORY \u2014 Test the build/create output, not the process. " +
                   "Verify key properties of created objects. Test with various configurations.";

        return null;
    }

    /// <summary>
    /// Returns anti-pattern warning comments based on type characteristics to help
    /// the AI agent avoid common test authoring mistakes.
    /// </summary>
    public static List<string> GetAntiPatternWarnings(
        TypeRecord typeRecord,
        List<(string FieldName, string InterfaceType, string ShortName, MockRecipe? Recipe)> mockFields,
        List<(string FieldName, string ParamType, string DefaultValue)> concreteFields,
        MockLibrary mockLib = MockLibrary.Moq)
    {
        var warnings = new List<string>();

        if (typeRecord.IsStatic)
            warnings.Add("STATIC CLASS: Static state may leak between tests. Use [Collection] to prevent parallel execution, or reset state in Dispose().");

        if (typeRecord.Interfaces.Any(i => i.Contains("IDisposable", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("IMPLEMENTS IDisposable: Ensure _sut is disposed in test cleanup. Implement IDisposable on this test class and call _sut.Dispose() in Dispose().");

        var concreteDeps = concreteFields.Where(f => !TypeDefaults.IsCommon(f.ParamType)).ToList();
        if (concreteDeps.Count > 0)
        {
            var depNames = string.Join(", ", concreteDeps.Select(d => d.ParamType));
            warnings.Add($"CONCRETE DEPENDENCIES ({depNames}): Avoid mocking concrete classes. Consider extracting interfaces or using real instances with known state.");
        }

        if (mockFields.Count >= 5)
            warnings.Add($"HIGH MOCK COUNT ({mockFields.Count}): Many mocks may indicate tight coupling. Tests may become brittle \u2014 focus {FrameworkTemplates.GetVerifyHint(mockLib)} on essential interactions only.");

        if (typeRecord.IsAbstract)
            warnings.Add("ABSTRACT CLASS: Cannot instantiate directly. Create a minimal test subclass that implements abstract members.");

        if (typeRecord.IsInternal)
            warnings.Add("INTERNAL CLASS: Ensure test project has [InternalsVisibleTo] or use a public API surface for testing.");

        if (typeRecord.Properties.Any(p =>
            p.ClrType.Contains("EventHandler")
            || p.ClrType == "Action"
            || p.ClrType.StartsWith("Action<")
            || p.Name.StartsWith("On", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("HAS EVENTS: Subscribe to events before acting, unsubscribe in cleanup to prevent memory leaks in tests.");

        return warnings;
    }
}
