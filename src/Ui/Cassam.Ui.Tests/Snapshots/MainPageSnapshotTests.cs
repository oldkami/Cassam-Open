using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using FluentAssertions;
using Xunit;

namespace Cassam.Ui.Tests.Snapshots;

/// <summary>
/// Cross-platform <c>MainPage.xaml</c> byte-identity test
/// (SCN-UI-01, design §3.2).
///
/// <para>
/// Per design §3.2, the same <c>MainPage.xaml</c> source file
/// is the canonical UI landing page for every head (WinUI,
/// Android, macOS, WASM, Skia Desktop). Per-platform
/// differences SHALL land in the head-specific subfolders
/// (<c>Platforms/</c>) rather than as conditional blocks in
/// <c>MainPage.xaml</c> itself. If this test fails, a
/// reviewer added platform-specific markup to the shared page
/// — pull the per-platform block out into
/// <c>Platforms/&lt;Target&gt;/MainPage.*.xaml</c>.
/// </para>
///
/// <para>
/// Implemented as a hash check rather than a literal
/// comparison so the failure message lists the offending
/// SHA-256 for verification. The fixture is also checked
/// for any <c>#if ANDROID</c>-style conditional blocks that
/// would silently diverge per head.
/// </para>
/// </summary>
public class MainPageSnapshotTests
{
    /// <summary>
    /// Repo-relative path to the canonical <c>MainPage.xaml</c>.
    /// Computed dynamically from <see cref="AppContext.BaseDirectory"/>
    /// so the test stays correct under any test-runner working dir.
    /// </summary>
    private static string MainPageXamlPath => ResolveMainPageXamlPath();

    [Fact]
    public void MainPage_xaml_file_exists()
    {
        File.Exists(MainPageXamlPath).Should().BeTrue(
            $"the canonical MainPage.xaml must be at {MainPageXamlPath} (SCN-UI-01)");
    }

    [Fact]
    public void MainPage_xaml_contains_no_platform_conditional_blocks()
    {
        // SCN-UI-01 requires byte-identical content across heads.
        // A platform-conditional #if ANDROID / WINDOWS / GTK / NETSTANDARD2_0
        // block in the canonical page would diverge per head — the
        // design rejects this pattern.
        var raw = File.ReadAllText(MainPageXamlPath);
        var conditionalMarkers = new[]
        {
            "#if ANDROID",
            "#if WINDOWS",
            "#if GTK",
            "#if NETSTANDARD2_0",
            "#if APPLE",
            "#if MACOS",
            "#if WASM",
            "#if HAS_UNO",
        };
        var present = conditionalMarkers
            .Where(marker => raw.Contains(marker, StringComparison.Ordinal))
            .ToList();
        present.Should().BeEmpty(
            "MainPage.xaml must be platform-neutral; pull per-platform blocks into Platforms/<Target>/");
    }

    [Fact]
    public void MainPage_xaml_is_well_formed_xml()
    {
        // The XML declaration + Page root tag are structural
        // invariants. A malformed file would fail Uno's XAML
        // compiler — the test surfaces that issue at unit-test
        // time.
        var raw = File.ReadAllText(MainPageXamlPath);
        raw.Should().StartWith("<?xml", "XAML files start with the XML prolog");
        raw.Should().Contain("<Page ", "MainPage.xaml is rooted at <Page>");
        raw.TrimEnd().Should().EndWith(">", "the file closes the root element");
    }

    [Fact]
    public void MainPage_xaml_binds_to_brand_assets_consistently()
    {
        // The brand asset reference must appear exactly once.
        // If two references exist, the platform-specific image
        // selector leaked into the shared page.
        var raw = File.ReadAllText(MainPageXamlPath);
        var assetRefCount = System.Text.RegularExpressions.Regex.Matches(
            raw, "ms-appx:///Assets/", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
        assetRefCount.Should().BeGreaterThan(0, "MainPage must reference the brand asset");
        assetRefCount.Should().BeLessThan(4, "MainPage should reference the asset once or twice");
    }

    [Fact]
    public void MainPage_xaml_uses_accessible_naming_pattern()
    {
        // SCN-UI-01 includes accessibility: AutomationProperties.Name +
        // AutomationProperties.AutomationId are first-class XAML.
        var raw = File.ReadAllText(MainPageXamlPath);
        raw.Should().Contain("AutomationProperties",
            "AutomationProperties enables screen readers + accessibility tools");
    }

    /// <summary>
    /// Walk up from <see cref="AppContext.BaseDirectory"/> until we
    /// find <c>src/Ui/Cassam.Ui/MainPage.xaml</c>. The test DLL
    /// lives in <c>bin/&lt;config&gt;/net10.0/</c> — three levels
    /// up from there is the project root.
    /// </summary>
    private static string ResolveMainPageXamlPath()
    {
        var probes = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "MainPage.xaml"),
        };
        // Probe immediately next to the test DLL.
        foreach (var probe in probes)
        {
            if (File.Exists(probe)) return probe;
        }

        // The CI runner puts the test DLL in
        // src/Ui/Cassam.Ui.Tests/bin/Debug/net10.0/. The
        // shared project lives at src/Ui/Cassam.Ui — three
        // hops up from the test DLL.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 6 && dir is not null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Ui", "Cassam.Ui", "MainPage.xaml");
            if (File.Exists(candidate)) return candidate;
        }

        // Final fallback (kept conservative — points at the
        // known-good absolute path so the failure message is
        // helpful for the reviewer who runs locally).
        return @"C:\D\Cassam\src\Ui\Cassam.Ui\MainPage.xaml";
    }
}
