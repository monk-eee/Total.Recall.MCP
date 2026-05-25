using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

[Collection("ToolTests")]
public sealed class TelemetryConfigTests : IDisposable
{
    private readonly string? _originalMode;

    public TelemetryConfigTests()
    {
        _originalMode = Environment.GetEnvironmentVariable("TOTAL_RECALL_MODE");
        TelemetryConfig.ResetCache();
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("TOTAL_RECALL_MODE", _originalMode);
        TelemetryConfig.ResetCache();
    }

    [Fact]
    public void Default_IsPassive_WhenEnvUnset()
    {
        Environment.SetEnvironmentVariable("TOTAL_RECALL_MODE", null);
        TelemetryConfig.ResetCache();
        Assert.Equal(TelemetryMode.Passive, TelemetryConfig.Mode);
        Assert.True(TelemetryConfig.IsRecording);
        Assert.False(TelemetryConfig.IsActiveEval);
    }

    [Theory]
    [InlineData("off", TelemetryMode.Off)]
    [InlineData("OFF", TelemetryMode.Off)]
    [InlineData("none", TelemetryMode.Off)]
    [InlineData("disabled", TelemetryMode.Off)]
    [InlineData("0", TelemetryMode.Off)]
    [InlineData("false", TelemetryMode.Off)]
    [InlineData("passive", TelemetryMode.Passive)]
    [InlineData("observe", TelemetryMode.Passive)]
    [InlineData("telemetry", TelemetryMode.Passive)]
    [InlineData("active-eval", TelemetryMode.ActiveEval)]
    [InlineData("active_eval", TelemetryMode.ActiveEval)]
    [InlineData("eval", TelemetryMode.ActiveEval)]
    [InlineData("active", TelemetryMode.ActiveEval)]
    public void Parse_RecognizesAliases(string envValue, TelemetryMode expected)
    {
        Environment.SetEnvironmentVariable("TOTAL_RECALL_MODE", envValue);
        TelemetryConfig.ResetCache();
        Assert.Equal(expected, TelemetryConfig.Mode);
    }

    [Fact]
    public void OffMode_IsRecordingFalse()
    {
        Environment.SetEnvironmentVariable("TOTAL_RECALL_MODE", "off");
        TelemetryConfig.ResetCache();
        Assert.False(TelemetryConfig.IsRecording);
    }

    [Fact]
    public void ActiveEvalMode_IsRecordingTrueAndIsActiveEvalTrue()
    {
        Environment.SetEnvironmentVariable("TOTAL_RECALL_MODE", "active-eval");
        TelemetryConfig.ResetCache();
        Assert.True(TelemetryConfig.IsRecording);
        Assert.True(TelemetryConfig.IsActiveEval);
    }

    [Fact]
    public void UnknownValue_FallsBackToPassive()
    {
        Environment.SetEnvironmentVariable("TOTAL_RECALL_MODE", "gibberish");
        TelemetryConfig.ResetCache();
        Assert.Equal(TelemetryMode.Passive, TelemetryConfig.Mode);
    }
}
