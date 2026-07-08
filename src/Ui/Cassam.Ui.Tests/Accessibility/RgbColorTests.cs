using Cassam.Ui.Hardware.Common.Accessibility;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Accessibility;

/// <summary>
/// Unit tests for <see cref="RgbColor"/> parsing. PR 10
/// (T2.12). Validates the <c>#RRGGBB</c> hex format accepted
/// from XAML theme resource dictionaries.
/// </summary>
public class RgbColorTests
{
    [Fact]
    public void FromHex_parses_lowercase_six_chars()
    {
        var color = RgbColor.FromHex("1976d2");
        color.R.Should().Be(0x19);
        color.G.Should().Be(0x76);
        color.B.Should().Be(0xd2);
    }

    [Fact]
    public void FromHex_parses_uppercase_six_chars()
    {
        var color = RgbColor.FromHex("1976D2");
        color.R.Should().Be(0x19);
        color.G.Should().Be(0x76);
        color.B.Should().Be(0xD2);
    }

    [Fact]
    public void FromHex_strips_leading_hash()
    {
        var color = RgbColor.FromHex("#FFFFFF");
        color.R.Should().Be(0xFF);
        color.G.Should().Be(0xFF);
        color.B.Should().Be(0xFF);
    }

    [Fact]
    public void FromHex_throws_on_null_or_empty()
    {
        var act = () => RgbColor.FromHex("");
        act.Should().Throw<System.ArgumentException>();
    }

    [Fact]
    public void FromHex_throws_on_wrong_length()
    {
        var act = () => RgbColor.FromHex("ABC");
        act.Should().Throw<System.FormatException>();
    }

    [Fact]
    public void FromHex_throws_on_non_hex_chars()
    {
        var act = () => RgbColor.FromHex("ZZZZZZ");
        act.Should().Throw<System.FormatException>();
    }

    [Fact]
    public void ToHex_emits_leading_hash_plus_uppercase()
    {
        var color = new RgbColor(0x19, 0x76, 0xd2);
        color.ToHex().Should().Be("#1976D2");
    }

    [Fact]
    public void ToString_returns_hex()
    {
        var color = new RgbColor(0, 0, 0);
        color.ToString().Should().Be("#000000");
    }
}
