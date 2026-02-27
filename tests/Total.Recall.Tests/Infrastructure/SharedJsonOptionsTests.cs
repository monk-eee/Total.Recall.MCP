using System.Text.Json;
using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

public sealed class SharedJsonOptionsTests
{
    [Fact]
    public void CamelCase_UsesCamelCaseNaming()
    {
        var obj = new { MyProperty = "value" };
        var json = JsonSerializer.Serialize(obj, SharedJsonOptions.CamelCase);

        Assert.Contains("myProperty", json);
        Assert.DoesNotContain("MyProperty", json);
    }

    [Fact]
    public void CamelCase_IsCompact()
    {
        var obj = new { A = 1, B = 2 };
        var json = JsonSerializer.Serialize(obj, SharedJsonOptions.CamelCase);

        Assert.DoesNotContain("\n", json);
    }

    [Fact]
    public void CamelCaseIndented_UsesCamelCaseNaming()
    {
        var obj = new { MyProperty = "value" };
        var json = JsonSerializer.Serialize(obj, SharedJsonOptions.CamelCaseIndented);

        Assert.Contains("myProperty", json);
    }

    [Fact]
    public void CamelCaseIndented_IsIndented()
    {
        var obj = new { A = 1, B = 2 };
        var json = JsonSerializer.Serialize(obj, SharedJsonOptions.CamelCaseIndented);

        Assert.Contains("\n", json);
    }

    [Fact]
    public void Indented_UsesPascalCaseNaming()
    {
        var obj = new { MyProperty = "value" };
        var json = JsonSerializer.Serialize(obj, SharedJsonOptions.Indented);

        Assert.Contains("MyProperty", json);
    }

    [Fact]
    public void Indented_IsIndented()
    {
        var obj = new { A = 1, B = 2 };
        var json = JsonSerializer.Serialize(obj, SharedJsonOptions.Indented);

        Assert.Contains("\n", json);
    }
}
