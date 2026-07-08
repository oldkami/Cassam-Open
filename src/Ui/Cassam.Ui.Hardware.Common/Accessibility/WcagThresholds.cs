namespace Cassam.Ui.Hardware.Common.Accessibility;

/// <summary>
/// WCAG 2.1 AA contrast thresholds (REQ-UI-09, SCN-UI-12,
/// design §12.2). The constants are kept here so the contrast
/// ratio calculator + the audit report share one source of
/// truth — a threshold bump from 4.5:1 to (say) 7:1 for the
/// high-contrast theme is one constant change.
/// </summary>
public static class WcagThresholds
{
    /// <summary>Normal-body contrast threshold (4.5:1 per WCAG 2.1 AA).</summary>
    public const double NormalTextRatio = 4.5;

    /// <summary>Large-text contrast threshold (3:1 per WCAG 2.1 AA).</summary>
    public const double LargeTextRatio = 3.0;

    /// <summary>High-contrast theme body threshold (7:1 per design §12.2).</summary>
    public const double HighContrastNormalRatio = 7.0;

    /// <summary>Touch-target minimum dimension in dp (SCN-UI-11).</summary>
    public const double MinTouchTargetDp = 44.0;

    /// <summary>Ambient-light threshold in lux at which the high-contrast theme is auto-applied (design §9.4).</summary>
    public const double HighContrastAutoLux = 25000.0;

    /// <summary>Large text = ≥18 pt OR ≥14 pt bold (WCAG 2.1 AA definition).</summary>
    public const double LargeTextPointSize = 18.0;

    /// <summary>Large text bold threshold (≥14 pt bold counts as "large text").</summary>
    public const double LargeTextBoldPointSize = 14.0;
}
