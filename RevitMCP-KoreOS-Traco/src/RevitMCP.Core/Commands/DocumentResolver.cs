using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace RevitMCPAddin.Commands;

/// <summary>
/// Picks the document a command acts on. Without documentTitle/documentPath it is the
/// active document (historic behaviour). With them, any open non-linked document can be
/// targeted, so a user switching windows mid-session cannot redirect writes. Observed
/// 11/09/2026: the user opened the showroom file and reads silently hit the wrong model.
/// </summary>
public static class DocumentResolver
{
    public const string TitleKey = "documentTitle";
    public const string PathKey = "documentPath";

    public static Document? Resolve(UIApplication app, JsonObject parameters)
    {
        var title = P.StrOrNull(parameters, TitleKey);
        var path = P.StrOrNull(parameters, PathKey);
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(path))
            return app.ActiveUIDocument?.Document;
        var open = OpenDocuments(app).ToList();
        var matches = open.Where(d => Matches(d.Title, d.PathName, title, path)).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new RevitCommandException("document_not_open",
                $"No open document matches title '{title}' / path '{path}'. Open: {string.Join(" | ", open.Select(d => d.Title))}."),
            _ => throw new RevitCommandException("ambiguous_selection", "More than one open document matches; pass documentPath."),
        };
    }

    /// <summary>
    /// Same open document. Never use ReferenceEquals: the API returns a new Document wrapper on
    /// every access (0.8.43 regression — every write batch and UI action was refused live).
    /// </summary>
    public static bool Same(Document? a, Document? b) => a is not null && b is not null && a.Equals(b);

    public static IEnumerable<Document> OpenDocuments(UIApplication app)
    {
        foreach (Document d in app.Application.Documents)
            if (!d.IsLinked) yield return d;
    }

    /// <summary>Pure matching rule: title without extension, case-insensitive; path compared normalised.</summary>
    public static bool Matches(string docTitle, string? docPath, string? title, string? path)
    {
        static string Strip(string s) => s.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) || s.EndsWith(".rfa", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
        static string Norm(string s) => s.Replace('/', '\\').TrimEnd('\\');
        if (!string.IsNullOrWhiteSpace(path))
            return !string.IsNullOrWhiteSpace(docPath) && string.Equals(Norm(docPath!), Norm(path!), StringComparison.OrdinalIgnoreCase);
        return string.Equals(Strip(docTitle), Strip(title!), StringComparison.OrdinalIgnoreCase);
    }
}
