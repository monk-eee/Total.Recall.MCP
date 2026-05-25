using Total.Recall.Reporting;

namespace Total.Recall.Tests.Reporting;

public class TableRendererTests
{
    [Fact]
    public void Render_NonJsonInput_ReturnsAsIs()
    {
        const string text = "No tool calls recorded yet.";
        Assert.Equal(text, TableRenderer.Render(text));
    }

    [Fact]
    public void Render_EmptyInput_ReturnsAsIs()
    {
        Assert.Equal("", TableRenderer.Render(""));
    }

    [Fact]
    public void Render_MalformedJson_ReturnsAsIs()
    {
        const string text = "{not really json";
        Assert.Equal(text, TableRenderer.Render(text));
    }

    [Fact]
    public void Render_FlatObject_RendersAsKeyValueList()
    {
        const string json = """
        { "sessionId": "abc", "toolCalls": 42, "dedupeRatePct": 12.5 }
        """;
        var output = TableRenderer.Render(json);
        Assert.Contains("sessionId", output);
        Assert.Contains("abc", output);
        Assert.Contains("toolCalls", output);
        Assert.Contains("42", output);
        Assert.Contains("12.5", output);
    }

    [Fact]
    public void Render_ObjectWithArrayProperty_RendersScalarsThenTable()
    {
        const string json = """
        {
          "totalCalls": 5,
          "sessions": 2,
          "tools": [
            { "name": "get_gotchas", "calls": 3, "avgLatencyMs": 12 },
            { "name": "get_context", "calls": 2, "avgLatencyMs": 8 }
          ]
        }
        """;
        var output = TableRenderer.Render(json);
        Assert.Contains("totalCalls", output);
        Assert.Contains("[tools]", output);
        Assert.Contains("name", output);
        Assert.Contains("get_gotchas", output);
        Assert.Contains("avgLatencyMs", output);
        // Header separator should exist
        Assert.Contains("---", output);
    }

    [Fact]
    public void Render_RootArray_RendersAsTable()
    {
        const string json = """
        [
          { "id": 1, "label": "first" },
          { "id": 2, "label": "second" }
        ]
        """;
        var output = TableRenderer.Render(json);
        Assert.Contains("id", output);
        Assert.Contains("label", output);
        Assert.Contains("first", output);
        Assert.Contains("second", output);
    }

    [Fact]
    public void Render_EmptyArrayProperty_ReportsEmpty()
    {
        const string json = """{ "totalCalls": 0, "tools": [] }""";
        var output = TableRenderer.Render(json);
        Assert.Contains("(empty)", output);
    }

    [Fact]
    public void Render_HeterogeneousRows_UsesUnionOfColumns()
    {
        const string json = """
        {
          "rows": [
            { "a": 1, "b": "x" },
            { "a": 2, "c": true }
          ]
        }
        """;
        var output = TableRenderer.Render(json);
        Assert.Contains("a", output);
        Assert.Contains("b", output);
        Assert.Contains("c", output);
        Assert.Contains("true", output);
        // The missing 'b' in row 2 should render as empty, not crash.
    }

    [Fact]
    public void Render_NestedObjectColumn_RendersAsCompactJson()
    {
        const string json = """
        {
          "rows": [ { "name": "x", "meta": { "k": "v" } } ]
        }
        """;
        var output = TableRenderer.Render(json);
        Assert.Contains("name", output);
        Assert.Contains("\"k\"", output);
    }
}
