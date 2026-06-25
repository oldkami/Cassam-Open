namespace Cassam.Ui.Hardware.Common;

/// <summary>
/// Logical paper width for a thermal receipt printer. The HAL
/// implementation maps the enum to the right ESC/POS pitch commands
/// (32-col, 42-col, or full 80-col receipt paper). Keeping the
/// width as an enum (not a number) prevents accidental coupling
/// between the renderer and the device driver.
/// </summary>
public enum PaperWidth
{
    /// <summary>58 mm / 32-column paper (small receipt printers).</summary>
    Mm58,

    /// <summary>80 mm / 42-column paper (most retail thermal printers).</summary>
    Mm80,

    /// <summary>112 mm / full 80-column wide-format (kitchen printers).</summary>
    Mm112,
}

/// <summary>
/// A pending print job — what to print, how many lines of text it
/// contains (so the implementation can advance paper correctly),
/// and the paper width so the byte stream uses the right pitch.
/// The actual ESC/POS bytes are produced by <c>ReceiptRenderer</c>
/// (PR 9, T2.09); the HAL does not interpret content.
/// </summary>
public sealed record PrintJob(
    string Content,
    int LineCount,
    PaperWidth Width);
