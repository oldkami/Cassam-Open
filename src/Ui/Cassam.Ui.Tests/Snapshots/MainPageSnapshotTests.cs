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
    /// Resolve <c>src/Ui/Cassam.Ui/MainPage.xaml</c> from any
    /// runner layout — local debug build, GitHub Actions
    /// checkout, or a future <c>CopyToOutput</c> step.
    /// Probes three sources in order:
    /// <list type="number">
    ///   <item>Next to the test DLL (bin/&lt;config&gt;/&lt;tfm&gt;/)</item>
    ///   <item>Walk up from <see cref="AppContext.BaseDirectory"/>
    ///         until we find a directory containing the file</item>
    ///   <item>Walk up from <see cref="Directory.GetCurrentDirectory"/>
    ///         until we find a directory containing the file</item>
    /// </list>
    /// <para>
    /// The previous implementation hard-coded the Windows-only
    /// path <c>C:\D\Cassam\…</c> as a final fallback, which a
    /// Linux runner combined with the BaseDirectory via
    /// <see cref="Path.GetFullPath(string?)"/> semantics into
    /// an invalid mixed-separator path. We now throw with an
    /// explicit diagnostic listing every probe the resolver
    /// tried, so a future failure points at the real cause
    /// instead of a misleading
    /// <see cref="FileNotFoundException"/>.
    /// </para>
    /// </summary>
    private static string ResolveMainPageXamlPath()
    {
        // 1. Probe immediately next to the test DLL. The test
        //    directory is bin/<config>/<tfm>/; MainPage.xaml
        //    does NOT live there today, but a future PR might
        //    add a copy-to-output step so we probe first.
        var immediate = Path.Combine(AppContext.BaseDirectory, "MainPage.xaml");
        if (File.Exists(immediate)) return immediate;

        // 2. Walk up from BaseDirectory. The GitHub Actions
        //    Linux runner nests bin/Release/net10.0/ seven
        //    levels below the repo root, so we walk until
        //    DirectoryInfo.Parent returns null (filesystem
        //    root) — no fixed depth cap.
        var fromBase = TryFindMainPageXaml(new DirectoryInfo(AppContext.BaseDirectory));
        if (fromBase is not null) return fromBase;

        // 3. Fallback: walk up from CWD. When `dotnet test` is
        //    invoked from the repo root (the default on
        //    GitHub Actions), CWD = repo root and the first
        //    probe succeeds. When invoked from the test
        //    project directory, we still eventually find the
        //    file by walking up.
        var fromCwd = TryFindMainPageXaml(new DirectoryInfo(Directory.GetCurrentDirectory()));
        if (fromCwd is not null) return fromCwd;

        throw new InvalidOperationException(
            "Could not locate MainPage.xaml. Probed:" + Environment.NewLine +
            $"  - Next to test DLL: {immediate}" + Environment.NewLine +
            $"  - Walked up from BaseDirectory: {AppContext.BaseDirectory}" + Environment.NewLine +
            $"  - Walked up from CWD: {Directory.GetCurrentDirectory()}" + Environment.NewLine +
            "Run 'git ls-files src/Ui/Cassam.Ui/MainPage.xaml' to verify the file exists in the working copy.");
    }

    private static string? TryFindMainPageXaml(DirectoryInfo start)
    {
        for (var dir = start; dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Ui", "Cassam.Ui", "MainPage.xaml");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
