using System;
using System.Collections.Generic;
using System.IO;

namespace VepMod.VepFramework.Extensions;

/// <summary>
///     Turns any string into a valid file or folder name.
/// </summary>
/// <remarks>
///     <para>
///         Windows has naming rules that <see cref="Path.GetInvalidFileNameChars" /> does not cover:
///     </para>
///     <list type="bullet">
///         <item>A name cannot end with a dot or a space.</item>
///         <item>Some names are reserved (CON, NUL, AUX, PRN, COM1-9, LPT1-9).</item>
///     </list>
///     <para>
///         See https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file
///     </para>
///     <para>
///         Sanitizing is <b>idempotent</b> (<c>Sanitize(Sanitize(x)) == Sanitize(x)</c>): callers sanitize
///         again names they read back from the file system, when scanning folders.
///     </para>
/// </remarks>
public static class FileSystemSanitizer
{
    private const string DefaultFallback = "unknown";

    /// <summary>
    ///     Reserved whatever the extension.
    /// </summary>
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    ///     Valid on Windows, Linux and macOS.
    /// </summary>
    /// <param name="input">For instance a Steam nickname.</param>
    /// <param name="fallback">Returned when nothing is left after sanitizing.</param>
    public static string Sanitize(string input, string fallback = DefaultFallback)
    {
        if (string.IsNullOrEmpty(input))
        {
            return fallback;
        }

        // The current OS's invalid characters: < > : " / \ | ? * and 0x00-0x1F on Windows, \0 and / elsewhere.
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", input.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // Windows only, trimmed everywhere so a name is the same on every OS.
        sanitized = sanitized.TrimEnd('.', ' ');

        if (string.IsNullOrEmpty(sanitized))
        {
            return fallback;
        }

        // Idempotent: "_CON_" is not reserved.
        if (WindowsReservedNames.Contains(sanitized))
        {
            sanitized = $"_{sanitized}_";
        }

        return sanitized;
    }
}
