using System;
using System.Collections.Generic;
using System.IO;

namespace VepMod.VepFramework.Extensions;

/// <summary>
///     Utilitaire de sanitisation de chaînes pour une utilisation comme noms
///     de fichiers ou dossiers sur le filesystem.
/// </summary>
/// <remarks>
///     <para>
///         Windows impose des contraintes spécifiques sur les noms de dossiers qui ne sont
///         pas couvertes par <see cref="Path.GetInvalidFileNameChars" /> :
///     </para>
///     <list type="bullet">
///         <item>Les noms ne peuvent pas se terminer par un point '.' ou un espace ' '.</item>
///         <item>Certains noms sont réservés par le système (CON, NUL, AUX, PRN, COM1-9, LPT1-9).</item>
///     </list>
///     <para>
///         Cette classe centralise toutes ces règles afin de garantir la compatibilité cross-platform.
///         Voir: https://learn.microsoft.com/en-us/windows/win32/fileio/naming-a-file
///     </para>
///     <para>
///         <b>Propriété clé</b> : la sanitisation est <b>idempotente</b> —
///         <c>Sanitize(Sanitize(x)) == Sanitize(x)</c> — ce qui est requis par les appelants
///         qui peuvent re-sanitiser des noms déjà extraits du filesystem (ex: scan de dossiers).
///     </para>
/// </remarks>
public static class FileSystemSanitizer
{
    private const string DefaultFallback = "unknown";

    /// <summary>
    ///     Noms réservés par Windows qui ne peuvent pas être utilisés comme noms de fichiers
    ///     ou dossiers, quelle que soit l'extension.
    /// </summary>
    private static readonly HashSet<string> WindowsReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>
    ///     Transforme une chaîne arbitraire en nom valide pour le filesystem,
    ///     compatible Windows, Linux et macOS.
    /// </summary>
    /// <param name="input">La chaîne à sanitiser (ex: pseudo joueur Steam).</param>
    /// <param name="fallback">
    ///     Valeur de repli si le résultat est vide après sanitisation.
    ///     Par défaut : "unknown".
    /// </param>
    /// <returns>Un nom de fichier/dossier valide sur tous les OS supportés.</returns>
    public static string Sanitize(string input, string fallback = DefaultFallback)
    {
        if (string.IsNullOrEmpty(input))
        {
            return fallback;
        }

        // 1. Retirer les caractères invalides pour le filesystem de l'OS courant.
        //    Sur Windows : < > : " / \ | ? * et caractères de contrôle (0x00-0x1F).
        //    Sur Linux/macOS : \0 et /.
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = string.Join("_", input.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries));

        // 2. Windows interdit les noms de dossier/fichier se terminant par '.' ou ' '.
        //    Inoffensif sur Linux/macOS (ces caractères y sont valides en fin de nom),
        //    mais garantit un comportement cohérent cross-platform.
        sanitized = sanitized.TrimEnd('.', ' ');

        // 3. Si le résultat est vide (ex: pseudo composé uniquement de caractères invalides
        //    ou de points), utiliser la valeur de repli.
        if (string.IsNullOrEmpty(sanitized))
        {
            return fallback;
        }

        // 4. Windows réserve certains noms (CON, NUL, AUX, etc.) qui ne peuvent pas être
        //    utilisés comme noms de dossier, même avec une extension.
        //    On les préfixe et suffixe avec '_' pour les rendre valides.
        //    Idempotent : "_CON_" ne fait pas partie des noms réservés.
        if (WindowsReservedNames.Contains(sanitized))
        {
            sanitized = $"_{sanitized}_";
        }

        return sanitized;
    }
}