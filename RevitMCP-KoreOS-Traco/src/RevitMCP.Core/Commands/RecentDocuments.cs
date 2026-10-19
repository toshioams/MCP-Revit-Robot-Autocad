using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Pure helpers for Revit's recent file list (the "[Recent File List]" section of
/// %APPDATA%\Autodesk\Revit\Autodesk Revit {version}\Revit.ini, UTF-16) and for resolving a
/// spoken project name ("abra o ginásio tapurah") against it. No Revit API here, so it is unit tested.
/// </summary>
public static class RecentDocuments
{
    public static string IniPath(string revitVersion) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Autodesk", "Revit", $"Autodesk Revit {revitVersion}", "Revit.ini");

    /// <summary>FileN= entries of [Recent File List] in N order, duplicates (case/slash) removed.</summary>
    public static List<string> Parse(IEnumerable<string> iniLines)
    {
        var entries = new List<(int N, string Path)>();
        bool inSection = false;
        foreach (var raw in iniLines)
        {
            var line = raw.Trim();
            if (line.StartsWith("[", StringComparison.Ordinal))
            {
                inSection = line.Equals("[Recent File List]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inSection || !line.StartsWith("File", StringComparison.OrdinalIgnoreCase)) continue;
            var eq = line.IndexOf('=');
            if (eq < 0 || !int.TryParse(line.AsSpan(4, eq - 4), out var n)) continue;
            var path = line[(eq + 1)..].Trim();
            if (path.Length > 0) entries.Add((n, path));
        }
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return entries.OrderBy(e => e.N).Select(e => e.Path)
            .Where(p => seen.Add(p.Replace('/', '\\'))).ToList();
    }

    /// <summary>Lowercase, no diacritics, separators (_ - . parentheses) collapsed to single spaces.</summary>
    public static string Normalize(string s)
    {
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(char.IsLetterOrDigit(c) ? char.ToLowerInvariant(c) : ' ');
        }
        return string.Join(' ', sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Paths whose file name matches the query. A file name equal to the query (normalized, without
    /// extension) wins alone; otherwise every query word must appear in the file name.
    /// </summary>
    public static List<string> Match(string query, IEnumerable<string> paths)
    {
        var q = Normalize(query);
        if (q.Length == 0) return new List<string>();
        var list = paths.ToList();
        string Name(string p) => Normalize(Path.GetFileNameWithoutExtension(p));
        var exact = list.Where(p => Name(p) == q).ToList();
        if (exact.Count > 0) return exact;
        var words = q.Split(' ');
        return list.Where(p => { var n = " " + Name(p) + " "; return words.All(w => n.Contains(w, StringComparison.Ordinal)); }).ToList();
    }
}
