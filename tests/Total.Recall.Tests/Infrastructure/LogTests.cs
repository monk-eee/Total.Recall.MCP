using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

public sealed class LogTests : IDisposable
{
    private readonly string? _originalLogLevel;

    public LogTests()
    {
        _originalLogLevel = Environment.GetEnvironmentVariable(Log.LogLevelEnvVar);
        // Tests run at Debug so all levels are visible
        Log.SetLevel(LogLevel.Debug);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Log.LogLevelEnvVar, _originalLogLevel);
        Log.ResetLevel();
    }

    [Fact]
    public void Info_WritesToStdErr()
    {
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Info("test message");
            var output = sw.ToString();
            Assert.Contains("[Total.Recall]", output);
            Assert.Contains("test message", output);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void Warn_WritesToStdErr_WithWarnPrefix()
    {
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Warn("warning message");
            var output = sw.ToString();
            Assert.Contains("WARN:", output);
            Assert.Contains("warning message", output);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void Error_WritesToStdErr_WithErrorPrefix()
    {
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Error("error message");
            var output = sw.ToString();
            Assert.Contains("ERROR:", output);
            Assert.Contains("error message", output);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void Debug_WritesToStdErr_WithDebugPrefix()
    {
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Debug("debug detail");
            var output = sw.ToString();
            Assert.Contains("DEBUG:", output);
            Assert.Contains("debug detail", output);
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void Debug_SuppressedAtInfoLevel()
    {
        Log.SetLevel(LogLevel.Info);
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Debug("should not appear");
            Assert.Empty(sw.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void Info_SuppressedAtWarnLevel()
    {
        Log.SetLevel(LogLevel.Warn);
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Info("should not appear");
            Assert.Empty(sw.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void Warn_SuppressedAtErrorLevel()
    {
        Log.SetLevel(LogLevel.Error);
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Warn("should not appear");
            Assert.Empty(sw.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void Error_SuppressedAtQuietLevel()
    {
        Log.SetLevel(LogLevel.Quiet);
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Error("should not appear");
            Assert.Empty(sw.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void QuietLevel_SuppressesAllOutput()
    {
        Log.SetLevel(LogLevel.Quiet);
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Debug("nope");
            Log.Info("nope");
            Log.Warn("nope");
            Log.Error("nope");
            Assert.Empty(sw.ToString());
        }
        finally
        {
            Console.SetError(original);
        }
    }

    [Fact]
    public void SetLevel_ChangesLevel()
    {
        Log.SetLevel(LogLevel.Error);
        Assert.Equal(LogLevel.Error, Log.Level);

        Log.SetLevel(LogLevel.Debug);
        Assert.Equal(LogLevel.Debug, Log.Level);
    }

    [Fact]
    public void IsEnabled_ReturnsCorrectly()
    {
        Log.SetLevel(LogLevel.Warn);

        Assert.False(Log.IsEnabled(LogLevel.Debug));
        Assert.False(Log.IsEnabled(LogLevel.Info));
        Assert.True(Log.IsEnabled(LogLevel.Warn));
        Assert.True(Log.IsEnabled(LogLevel.Error));
    }

    [Theory]
    [InlineData("debug", LogLevel.Debug)]
    [InlineData("verbose", LogLevel.Debug)]
    [InlineData("trace", LogLevel.Debug)]
    [InlineData("info", LogLevel.Info)]
    [InlineData("information", LogLevel.Info)]
    [InlineData("warn", LogLevel.Warn)]
    [InlineData("warning", LogLevel.Warn)]
    [InlineData("error", LogLevel.Error)]
    [InlineData("err", LogLevel.Error)]
    [InlineData("quiet", LogLevel.Quiet)]
    [InlineData("silent", LogLevel.Quiet)]
    [InlineData("none", LogLevel.Quiet)]
    public void ResetLevel_ReadsEnvVar(string envValue, LogLevel expected)
    {
        Environment.SetEnvironmentVariable(Log.LogLevelEnvVar, envValue);
        Log.ResetLevel();
        Assert.Equal(expected, Log.Level);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("bogus")]
    public void ResetLevel_DefaultsToInfo_WhenEnvVarInvalid(string? envValue)
    {
        Environment.SetEnvironmentVariable(Log.LogLevelEnvVar, envValue);
        Log.ResetLevel();
        Assert.Equal(LogLevel.Info, Log.Level);
    }

    [Fact]
    public void Output_IncludesTimestamp()
    {
        var original = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            Log.Info("timestamp check");
            var output = sw.ToString();
            // Timestamp format: HH:mm:ss.fff — look for the pattern
            Assert.Matches(@"\d{2}:\d{2}:\d{2}\.\d{3}", output);
        }
        finally
        {
            Console.SetError(original);
        }
    }
}
