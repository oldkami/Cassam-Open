using System;
using System.Collections.Generic;
using Cassam.Ui.Hardware.Common.Accessibility;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Media;

namespace Cassam.Ui.Common.Accessibility;

/// <summary>
/// Exception raised by <see cref="TouchTargetEnforcer"/>
/// when an interactive control measures below the WCAG 2.5.5
/// / Material Design ≥44dp threshold (design §9.2, SCN-UI-11).
/// The exception name + payload are what the manager view
/// surfaces in the AccessibilityAuditViewModel so the
/// reviewer knows which element to fix.
/// </summary>
public sealed class TouchTargetTooSmallException : Exception
{
    public TouchTargetTooSmallException(string elementName, double width, double height, double minSize)
        : base($"Control '{elementName}' measures {width:0.#}×{height:0.#}dp — minimum is {minSize:0.#}dp.")
    {
        ElementName = elementName;
        Width = width;
        Height = height;
        MinSize = minSize;
    }

    /// <summary>The control's display name (or AutomationId fallback).</summary>
    public string ElementName { get; }

    /// <summary>Width in device-independent pixels at audit time.</summary>
    public double Width { get; }

    /// <summary>Height in device-independent pixels at audit time.</summary>
    public double Height { get; }

    /// <summary>The minimum dimension for the platform (44dp on Android).</summary>
    public double MinSize { get; }
}

/// <summary>
/// UI-tree walking audit (T2.12, SCN-UI-11, design §9.2).
///
/// <para>
/// On Android, every <see cref="Microsoft.UI.Xaml.Controls.Button"/>,
/// <see cref="Microsoft.UI.Xaml.Controls.HyperlinkButton"/>, and
/// ComboBox must measure ≥44dp on its shortest side. The
/// enforcer walks the visual tree from a root element and
/// collects measurements into the platform-neutral
/// <see cref="AccessibilityAuditor"/> — keeping the audit
/// rule-testable while the tree walking lives where it has
/// to (here, in the Uno head project).
/// </para>
///
/// <para>
/// Per design §9.2 + §9.5, the integration test runs on
/// Android Layoutlib. The unit-test surface here is the
/// snapshot builder <see cref="BuildSnapshots"/>, which the
/// <c>Cassam.Ui.Tests</c> project exercises with a synthetic
/// snapshot list — the actual UI-tree walk is exercised by
/// the layoutlib integration test in a future PR.
/// </para>
/// </summary>
public static class TouchTargetEnforcer
{
    /// <summary>
    /// Walk the visual tree from <paramref name="root"/> and
    /// collect every interactive element into a snapshot
    /// list, then run the platform-neutral audit. Throws
    /// <see cref="TouchTargetTooSmallException"/> on the
    /// first failure if <paramref name="throwOnFailure"/>
    /// is true; otherwise returns the issue list and lets
    /// the caller surface it.
    /// </summary>
    public static IReadOnlyList<AccessibilityAuditIssue> Enforce(
        DependencyObject root,
        double minSizeDp = WcagThresholds.MinTouchTargetDp,
        bool throwOnFailure = false)
    {
        ArgumentNullException.ThrowIfNull(root);

        var snapshots = BuildSnapshots(root, minSizeDp);
        var issues = AccessibilityAuditor.Audit(snapshots, minSizeDp);

        if (throwOnFailure && issues.Count > 0)
        {
            var first = issues[0];
            throw new TouchTargetTooSmallException(
                elementName: first.ElementName,
                width: first.ObservedValue,
                height: first.ObservedValue,
                minSize: first.ThresholdValue);
        }

        return issues;
    }

    /// <summary>
    /// Pure data-collection entry point. Walks the visual
    /// tree, returns the snapshot list, performs no
    /// assertions. Useful when the caller wants to run
    /// additional audits (contrast, automation-id presence,
    /// etc.) over the same snapshot.
    /// </summary>
    public static IReadOnlyList<InteractiveElementSnapshot> BuildSnapshots(
        DependencyObject root,
        double minSizeDp = WcagThresholds.MinTouchTargetDp)
    {
        ArgumentNullException.ThrowIfNull(root);

        var snapshots = new List<InteractiveElementSnapshot>();
        var queue = new Queue<DependencyObject>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (IsInteractive(node, out var kind))
            {
                snapshots.Add(SnapshotFor(node, kind));
            }
            var childCount = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(node, i);
                if (child is not null) queue.Enqueue(child);
            }
        }
        return snapshots;
    }

    private static bool IsInteractive(DependencyObject node, out string kind)
    {
        // The literal type names match Microsoft.UI.Xaml.
        // We do a name-based check so this file compiles
        // against any Uno head that doesn't expose the
        // ButtonBase types (rare; mainly Skia fallback).
        var typeName = node.GetType().Name;
        kind = typeName;
        return typeName is "Button"
            or "HyperlinkButton"
            or "AppBarButton"
            or "ToggleButton"
            or "DropDownButton"
            or "RepeatButton"
            or "ToggleSwitch"
            or "CheckBox"
            or "RadioButton"
            or "ComboBox"
            or "ListBoxItem";
    }

    private static InteractiveElementSnapshot SnapshotFor(DependencyObject node, string kind)
    {
        var element = node as FrameworkElement;
        var width = element?.ActualWidth ?? 0;
        var height = element?.ActualHeight ?? 0;
        var elementName = element?.Name ?? kind;
        return new InteractiveElementSnapshot(
            ElementName: elementName,
            ControlKind: kind,
            AutomationId: TryGetAutomationId(element),
            WidthDp: width,
            HeightDp: height,
            IsInteractive: true);
    }

    private static string? TryGetAutomationId(FrameworkElement? element)
    {
        if (element is null) return null;
        try
        {
            var value = element.GetValue(AutomationProperties.AutomationIdProperty);
            return value as string;
        }
        catch
        {
            return null;
        }
    }
}
