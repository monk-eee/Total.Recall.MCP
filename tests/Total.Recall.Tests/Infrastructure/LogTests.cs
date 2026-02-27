using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

public sealed class LogTests
{
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
}
