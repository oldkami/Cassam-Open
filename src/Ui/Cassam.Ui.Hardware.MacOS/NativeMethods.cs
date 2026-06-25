using System;
using System.Runtime.InteropServices;

namespace Cassam.Ui.Hardware.MacOS;

/// <summary>
/// P/Invoke surface for the macOS frameworks the HAL needs.
///
/// <para>
/// Why hand-rolled instead of a NuGet wrapper:
/// <list type="bullet">
///   <item>The HAL surface is tiny — three Core Graphics calls for the
///         keyboard event tap and a handful of IOKit iterators. A
///         full binding (e.g. <c>MacCatalyst</c>) drags in hundreds of
///         types the cashier flow never touches.</item>
///   <item>Each method is gated on <see cref="OperatingSystem.IsMacOS"/>
///         in the public wrapper so a wrong-host invocation fails
///         loudly with <see cref="PlatformNotSupportedException"/>
///         instead of a missing-symbol DllNotFoundException at
///         process startup.</item>
///   <item>The P/Invoke signatures here are the minimum needed by the
///         macOS HAL. We deliberately do NOT surface the full
///         CGEventTap / IOKit API — adding more is mechanical once
///         station hardware confirms the exact byte-level protocol.</item>
/// </list>
/// </para>
///
/// <para>
/// Symbol names follow the Objective-C / C calling convention used
/// by Apple's frameworks. <c>__Internal</c> means the symbol resolves
/// from the host process's main binary; on macOS this is the .NET
/// runtime's executable. <c>CoreGraphics</c> / <c>IOKit</c> resolve
/// to the framework dylibs (resolved by dyld at runtime).
/// </para>
/// </summary>
internal static partial class NativeMethods
{
    /// <summary>
    /// CGEventTapCreate — install a system-wide keyboard event tap.
    /// <c>kCGEventTapOptionDefault</c> + <c>kCGHIDEventTap</c> +
    /// <c>kCGEventMaskForAllEvents</c> capture every keystroke.
    /// The callback marshals a <see cref="IntPtr"/> to the
    /// caller-supplied <c>UnsafeNativeMethods.EventTapCallback</c>
    /// trampoline.
    /// </summary>
    /// <remarks>
    /// The <c>eventsOfInterest</c> parameter is a <c>CGEventMask</c>
    /// which is typedef'd to <c>uint64_t</c> on 64-bit macOS. We
    /// expose it as <see cref="UIntPtr"/> so the marshaller picks the
    /// platform-correct width — CGEventTapCreate on 32-bit macOS
    /// (Intel Mac Mini 2007-era) takes a 32-bit mask, on 64-bit it
    /// takes 64. Production macOS retail stations are 64-bit only.
    /// </remarks>
    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    internal static extern IntPtr CGEventTapCreate(
        IntPtr tapLocation,
        uint options,
        uint eventsOfInterest,
        UIntPtr eventMask,
        IntPtr callback,
        IntPtr userInfo);

    /// <summary>
    /// CFMachPortCreateRunLoopSource — bridge the CFMachPort returned
    /// by CGEventTapCreate onto a CFRunLoop so the tap can be
    /// scheduled against the main runloop.
    /// </summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern IntPtr CFMachPortCreateRunLoopSource(
        IntPtr allocator,
        IntPtr machPort,
        IntPtr order);

    /// <summary>Run the current thread's CFRunLoop in default mode.</summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern void CFRunLoopRun();

    /// <summary>Stop the current CFRunLoop.</summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern void CFRunLoopStop(IntPtr runLoop);

    /// <summary>Get the current thread's CFRunLoop pointer.</summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern IntPtr CFRunLoopGetCurrent();

    /// <summary>Standard "main" CFRunLoop mode constant.</summary>
    internal static readonly IntPtr kCFRunLoopDefaultMode =
        CFSTR("kCFRunLoopDefaultMode");

    /// <summary>
    /// CFRunLoopAddSource — attach the event-tap source to the main
    /// run loop so keystrokes start flowing.
    /// </summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern void CFRunLoopAddSource(
        IntPtr runLoop,
        IntPtr source,
        IntPtr mode);

    /// <summary>
    /// CFRunLoopRemoveSource — detach the source so the tap stops
    /// firing. Required to release the tap before <c>StopAsync</c>.
    /// </summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern void CFRunLoopRemoveSource(
        IntPtr runLoop,
        IntPtr source,
        IntPtr mode);

    /// <summary>CFRelease — release any CF / CG object the runtime handed out.</summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern void CFRelease(IntPtr cf);

    /// <summary>Create a CFString from a UTF-8 byte pointer.</summary>
    [DllImport("/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation")]
    internal static extern IntPtr CFStringCreateWithCString(
        IntPtr alloc,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string cStr,
        uint encoding);

    /// <summary>Convenience wrapper for <c>CFSTR</c>.</summary>
    private static IntPtr CFSTR(string s) =>
        CFStringCreateWithCString(IntPtr.Zero, s, 0x08000100 /* kCFStringEncodingUTF8 */);

    // -- Event-tap option / event-mask constants ---------------------
    // Mirrors <CoreGraphics/CGEventTypes.h>. Hard-coded because the
    // headers are not part of any NuGet package the cashier flow uses.

    internal const uint kCGEventTapOptionDefault = 0;
    internal const uint kCGHIDEventTap = 0;       // tap at the HID layer
    internal const ulong kCGEventKeyDown = 1UL << 10;
    internal const ulong kCGEventKeyUp = 1UL << 11;
    internal static readonly UIntPtr kCGEventMaskForAllEvents = new(~0UL);

    /// <summary>
    /// The native callback trampoline. Marshals the raw
    /// CGEventRef + userInfo back into the managed
    /// <see cref="MacOSBarcodeScanner"/> instance via
    /// <c>GCHandle.FromIntPtr</c>. Returning <c>NULL</c> swallows
    /// the event (used when the tap is being torn down).
    /// </summary>
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate IntPtr EventTapCallback(
        IntPtr proxy,
        uint eventType,
        IntPtr eventRef,
        IntPtr userInfo);
}
