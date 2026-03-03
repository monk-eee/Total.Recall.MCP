using Total.Recall.Infrastructure;

namespace Total.Recall.Tests.Infrastructure;

public sealed class ParamHelperTests
{
    // ── ParseParam ──

    [Fact]
    public void ParseParam_StandardParam_ReturnsTypeAndName()
    {
        var (type, name) = ParamHelper.ParseParam("ILogger logger");
        Assert.Equal("ILogger", type);
        Assert.Equal("logger", name);
    }

    [Fact]
    public void ParseParam_UnderscorePrefix_StripsUnderscore()
    {
        var (type, name) = ParamHelper.ParseParam("ILogger _logger");
        Assert.Equal("ILogger", type);
        Assert.Equal("logger", name);
    }

    [Fact]
    public void ParseParam_NoSpace_ReturnsWholeAsType()
    {
        var (type, name) = ParamHelper.ParseParam("ILogger");
        Assert.Equal("ILogger", type);
        Assert.Equal("param", name);
    }

    [Fact]
    public void ParseParam_GenericType_PreservesGenericSyntax()
    {
        var (type, name) = ParamHelper.ParseParam("IOptions<MyConfig> options");
        Assert.Equal("IOptions<MyConfig>", type);
        Assert.Equal("options", name);
    }

    [Fact]
    public void ParseParam_WhitespacePadding_Trims()
    {
        var (type, name) = ParamHelper.ParseParam("  string   value  ");
        Assert.Equal("string", type);
        Assert.Equal("value", name);
    }

    [Fact]
    public void ParseParam_EmptyName_ReturnsParamDefault()
    {
        var (type, name) = ParamHelper.ParseParam("string ");
        Assert.Equal("string", type);
        Assert.Equal("param", name);
    }

    [Fact]
    public void ParseParam_OnlyUnderscore_ReturnsParamDefault()
    {
        var (type, name) = ParamHelper.ParseParam("string _");
        Assert.Equal("string", type);
        Assert.Equal("param", name);
    }

    // ── ExtractTypeName ──

    [Fact]
    public void ExtractTypeName_StandardParam_ReturnsType()
    {
        var type = ParamHelper.ExtractTypeName("ILogger _logger");
        Assert.Equal("ILogger", type);
    }

    [Fact]
    public void ExtractTypeName_NoSpace_ReturnsWhole()
    {
        var type = ParamHelper.ExtractTypeName("ILogger");
        Assert.Equal("ILogger", type);
    }

    [Fact]
    public void ExtractTypeName_GenericType_ReturnsFullGeneric()
    {
        // Uses first space — "IOptions<MyConfig>" has no space, so full generic preserved
        var type = ParamHelper.ExtractTypeName("IOptions<MyConfig> options");
        Assert.Equal("IOptions<MyConfig>", type);
    }

    // ── IsInterfaceLike ──

    [Theory]
    [InlineData("ILogger", true)]
    [InlineData("IDisposable", true)]
    [InlineData("IContentBase", true)]
    [InlineData("Int32", false)]       // I + lowercase
    [InlineData("Item", false)]        // 'I' + lowercase 't'
    [InlineData("I", false)]           // too short
    [InlineData("", false)]            // empty
    [InlineData("string", false)]      // no I prefix
    [InlineData("Invoice", false)]     // I + lowercase 'n'
    public void IsInterfaceLike_DetectsPatternCorrectly(string typeName, bool expected)
    {
        Assert.Equal(expected, ParamHelper.IsInterfaceLike(typeName));
    }

    // ── StripIPrefix ──

    [Theory]
    [InlineData("ILogger", "Logger")]
    [InlineData("IContentBase", "ContentBase")]
    [InlineData("IDisposable", "Disposable")]
    [InlineData("string", "string")]     // no prefix → unchanged
    [InlineData("Int32", "Int32")]       // I + lowercase → unchanged
    [InlineData("I", "I")]              // too short → unchanged
    public void StripIPrefix_ReturnsExpected(string input, string expected)
    {
        Assert.Equal(expected, ParamHelper.StripIPrefix(input));
    }

    // ── CountInterfaceParams ──

    [Fact]
    public void CountInterfaceParams_MixedParams_CountsOnlyInterfaces()
    {
        var @params = new[] { "ILogger _logger", "string name", "IContentBase content", "int count" };
        Assert.Equal(2, ParamHelper.CountInterfaceParams(@params));
    }

    [Fact]
    public void CountInterfaceParams_EmptyList_ReturnsZero()
    {
        Assert.Equal(0, ParamHelper.CountInterfaceParams([]));
    }

    [Fact]
    public void CountInterfaceParams_AllInterfaces_CountsAll()
    {
        var @params = new[] { "ILogger logger", "IDisposable disposable" };
        Assert.Equal(2, ParamHelper.CountInterfaceParams(@params));
    }

    // ── IsExternalDependency ──

    [Theory]
    [InlineData("IFileSystem", true)]
    [InlineData("FileStream", true)]
    [InlineData("HttpClient", true)]
    [InlineData("IHttpClientFactory", true)]
    [InlineData("Stream", true)]
    [InlineData("MemoryStream", true)]  // contains "Stream"
    [InlineData("SqlConnection", true)]
    [InlineData("DbContext", true)]
    [InlineData("IFileProvider", true)]
    [InlineData("IProcessRunner", true)]
    [InlineData("IEnvironmentProvider", true)]
    [InlineData("SocketFactory", true)]
    [InlineData("DatabaseClient", true)]
    [InlineData("RegistryKey", true)]
    [InlineData("ILogger", false)]
    [InlineData("IMapper", false)]
    [InlineData("IOptions", false)]
    [InlineData("IValidator", false)]
    [InlineData("String", false)]
    [InlineData("CancellationToken", false)]
    [InlineData("IDisposable", false)]
    [InlineData("MyService", false)]
    public void IsExternalDependency_DetectsCorrectly(string typeName, bool expected)
    {
        Assert.Equal(expected, ParamHelper.IsExternalDependency(typeName));
    }
}
