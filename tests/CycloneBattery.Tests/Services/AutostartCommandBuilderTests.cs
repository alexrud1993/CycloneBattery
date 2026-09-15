using CycloneBattery.Core.Services;
using Xunit;

namespace CycloneBattery.Tests.Services;

/// <summary>
/// Covers the autostart registration details: the current-user Run key, the value name, and the
/// quoting rules for executable paths.
/// </summary>
public class AutostartCommandBuilderTests
{
    [Fact]
    public void UsesTheCurrentUserRunKey()
    {
        AutostartEntry entry = AutostartCommandBuilder.Build(@"C:\Tools\CycloneBattery.exe");

        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Run", entry.RegistryKeyPath);
        Assert.Equal(AutostartCommandBuilder.RunKeyPath, entry.RegistryKeyPath);
    }

    [Fact]
    public void KeyPathIsRelativeSoItCannotAddressTheMachineWideHive()
    {
        // The value is written under HKEY_CURRENT_USER; the path itself must stay hive-relative.
        Assert.False(AutostartCommandBuilder.RunKeyPath.StartsWith("HKEY", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("CurrentControlSet", AutostartCommandBuilder.RunKeyPath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsesTheDocumentedValueName()
    {
        AutostartEntry entry = AutostartCommandBuilder.Build(@"C:\Tools\CycloneBattery.exe");

        Assert.Equal("CycloneBattery", entry.ValueName);
    }

    [Fact]
    public void QuotesPathContainingSpaces()
    {
        AutostartEntry entry = AutostartCommandBuilder.Build(@"C:\Program Files\Cyclone Battery\CycloneBattery.exe");

        Assert.StartsWith("\"C:\\Program Files\\Cyclone Battery\\CycloneBattery.exe\"", entry.ValueData, StringComparison.Ordinal);
    }

    [Fact]
    public void AppendsTheSilentStartArgument()
    {
        AutostartEntry entry = AutostartCommandBuilder.Build(@"C:\Tools\CycloneBattery.exe", startSilently: true);

        Assert.EndsWith(AutostartCommandBuilder.SilentStartArgument, entry.ValueData, StringComparison.Ordinal);
    }

    [Fact]
    public void CanOmitTheSilentStartArgument()
    {
        AutostartEntry entry = AutostartCommandBuilder.Build(@"C:\Tools\CycloneBattery.exe", startSilently: false);

        Assert.Equal("\"C:\\Tools\\CycloneBattery.exe\"", entry.ValueData);
    }

    [Fact]
    public void EscapesEmbeddedQuotes()
    {
        string quoted = AutostartCommandBuilder.QuotePath(@"C:\Weird""Path\CycloneBattery.exe");

        Assert.Equal("\"C:\\Weird\\\"Path\\CycloneBattery.exe\"", quoted);
    }

    [Fact]
    public void ParseRoundTripsAQuotedPathWithSpaces()
    {
        AutostartEntry entry = AutostartCommandBuilder.Build(@"C:\Program Files\Cyclone Battery\CycloneBattery.exe");

        string? parsed = AutostartCommandBuilder.TryParseExecutablePath(entry.ValueData);

        Assert.Equal(@"C:\Program Files\Cyclone Battery\CycloneBattery.exe", parsed);
    }

    [Fact]
    public void ParseRoundTripsAnEscapedPath()
    {
        AutostartEntry entry = AutostartCommandBuilder.Build(@"C:\Weird""Path\CycloneBattery.exe");

        Assert.Equal(@"C:\Weird""Path\CycloneBattery.exe", AutostartCommandBuilder.TryParseExecutablePath(entry.ValueData));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("C:\\Unquoted\\CycloneBattery.exe")]
    [InlineData("\"C:\\Unterminated\\CycloneBattery.exe")]
    public void ParseRejectsValuesItCannotUnderstand(string? valueData)
    {
        Assert.Null(AutostartCommandBuilder.TryParseExecutablePath(valueData));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildRejectsAnEmptyExecutablePath(string? path)
    {
        Assert.ThrowsAny<ArgumentException>(() => AutostartCommandBuilder.Build(path!));
    }
}
