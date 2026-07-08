using System;
using System.Collections.Generic;

namespace Cassam.Ui.Hardware.Common.Accessibility;

/// <summary>
/// Lightweight snapshot of one interactive control in the UI.
/// Used by <see cref="AccessibilityAuditor"/> to evaluate
/// WCAG compliance without instantiating any platform UI
/// types.
///
/// <para>
/// The <see cref="TouchTargetEnforcer"/> in
/// <c>Cassam.Ui/Common/Accessibility/</c> walks the Uno
/// visual tree and produces a list of these snapshots;
/// passing them to <see cref="AccessibilityAuditor"/> is
/// the single audit entry point shared by the cashier
/// + manager flows.
/// </para>
/// </summary>
/// <param name="ElementName">Display name for diagnostic messages.</param>
/// <param name="ControlKind">Button, HyperlinkButton, AppBarButton, ToggleSwitch, ComboBox, etc.</param>
/// <param name="AutomationId">AutomationProperties.AutomationId, may be null.</param>
/// <param name="WidthDp">Layout width in device-independent pixels.</param>
/// <param name="HeightDp">Layout height in device-independent pixels.</param>
/// <param name="IsInteractive">True for tappable controls; false for static text + decoration.</param>
public sealed record InteractiveElementSnapshot(
    string ElementName,
    string ControlKind,
    string? AutomationId,
    double WidthDp,
    double HeightDp,
    bool IsInteractive);

/// <summary>
/// One WCAG audit issue surfaced by <see cref="AccessibilityAuditor"/>.
/// </summary>
/// <param name="ElementName">Display name of the offending element.</param>
/// <param name="AutomationId">AutomationProperties.AutomationId, may be null.</param>
/// <param name="Severity">Error (failing AA), Warning (close to threshold), Info (advisory).</param>
/// <param name="Rule">Numeric code identifying the rule (e.g. <see cref="AccessibilityRuleCode.TouchTargetTooSmall"/>).</param>
/// <param name="Message">Human-readable description, safe to surface in the manager UI.</param>
/// <param name="ObservedValue">Numeric measurement that triggered the rule (e.g. width in dp, contrast ratio).</param>
/// <param name="ThresholdValue">Threshold the rule compares against.</param>
public sealed record AccessibilityAuditIssue(
    string ElementName,
    string? AutomationId,
    AccessibilityIssueSeverity Severity,
    AccessibilityRuleCode Rule,
    string Message,
    double ObservedValue,
    double ThresholdValue);

/// <summary>
/// Severity bucket used by the manager view to colour-code
/// the audit list. Mirrors what a11y tools call "violations"
/// vs "needs review" vs "best practice".
/// </summary>
public enum AccessibilityIssueSeverity
{
    /// <summary>Rule fails the WCAG 2.1 AA threshold — must be fixed.</summary>
    Error = 0,

    /// <summary>Within 10% of the threshold — review before release.</summary>
    Warning = 1,

    /// <summary>Best-practice advisory (not WCAG-failing).</summary>
    Info = 2,
}

/// <summary>Stable enum values so audit issues can be filtered/deduped across builds.</summary>
public enum AccessibilityRuleCode
{
    TouchTargetTooSmall = 1000,
    ContrastRatioFailed = 2000,
}

/// <summary>
/// Pure-function WCAG auditor (REQ-UI-09, REQ-UI-12, SCN-UI-11,
/// SCN-UI-12, design §9). Walks a list of UI element snapshots
/// and emits a list of <see cref="AccessibilityAuditIssue"/>s.
///
/// <para>
/// Living in <c>Cassam.Ui.Hardware.Common</c> means the
/// auditor is platform-neutral and unit-testable. The UI-tree
/// walking in <see cref="TouchTargetEnforcer"/> lives in the
/// Uno project (which can see <c>FrameworkElement</c>) and
/// produces the snapshot list passed to the auditor.
/// </para>
/// </summary>
public static class AccessibilityAuditor
{
    /// <summary>
    /// Audit one snapshot list. Returns the set of issues
    /// discovered (never null). Tests assert exact issues.
    /// </summary>
    public static IReadOnlyList<AccessibilityAuditIssue> Audit(
        IReadOnlyList<InteractiveElementSnapshot> elements,
        double minTouchTargetDp = WcagThresholds.MinTouchTargetDp)
    {
        ArgumentNullException.ThrowIfNull(elements);
        var issues = new List<AccessibilityAuditIssue>();
        foreach (var el in elements)
        {
            if (!el.IsInteractive) continue;
            var shortest = Math.Min(el.WidthDp, el.HeightDp);
            if (shortest < minTouchTargetDp)
            {
                var severity = AccessibilityIssueSeverity.Error;
                // Warning band: within 10% of threshold.
                if (shortest >= minTouchTargetDp * 0.9)
                {
                    severity = AccessibilityIssueSeverity.Warning;
                }
                issues.Add(new AccessibilityAuditIssue(
                    ElementName: el.ElementName,
                    AutomationId: el.AutomationId,
                    Severity: severity,
                    Rule: AccessibilityRuleCode.TouchTargetTooSmall,
                    Message: $"{el.ControlKind} '{el.ElementName}' mide {el.WidthDp:0.#}×{el.HeightDp:0.#} dp — el mínimo recomendado es {minTouchTargetDp:0.#} dp (WCAG 2.5.5).",
                    ObservedValue: shortest,
                    ThresholdValue: minTouchTargetDp));
            }
        }
        return issues;
    }

    /// <summary>
    /// Audit a (foreground, background) colour pair against
    /// WCAG 2.1 AA. Returns the issue list (one entry if
    /// fails / warns, empty if passes).
    /// </summary>
    public static IReadOnlyList<AccessibilityAuditIssue> AuditContrast(
        string elementName,
        string? automationId,
        RgbColor foreground,
        RgbColor background,
        bool isLargeText)
    {
        var ratio = ContrastRatioCalculator.CalculateContrastRatio(foreground, background);
        var (passes, threshold, rule) = isLargeText
            ? (ContrastRatioCalculator.MeetsAA(ratio, isLargeText: true), WcagThresholds.LargeTextRatio, AccessibilityRuleCode.ContrastRatioFailed)
            : (ContrastRatioCalculator.MeetsAA(ratio, isLargeText: false), WcagThresholds.NormalTextRatio, AccessibilityRuleCode.ContrastRatioFailed);
        if (passes) return Array.Empty<AccessibilityAuditIssue>();

        var severity = ratio >= threshold * 0.9
            ? AccessibilityIssueSeverity.Warning
            : AccessibilityIssueSeverity.Error;
        var label = isLargeText ? "texto grande" : "texto normal";
        var message = $"{elementName} — contraste {ratio:F2}:1 no cumple WCAG 2.1 AA ({label}, mínimo {threshold:F1}:1).";
        return new[]
        {
            new AccessibilityAuditIssue(
                ElementName: elementName,
                AutomationId: automationId,
                Severity: severity,
                Rule: rule,
                Message: message,
                ObservedValue: ratio,
                ThresholdValue: threshold),
        };
    }
}
