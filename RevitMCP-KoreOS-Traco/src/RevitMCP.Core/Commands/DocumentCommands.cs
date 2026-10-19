using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin.Commands;

/// <summary>Read-only: open (non-linked) documents and which one is active.</summary>
public sealed class ListOpenDocumentsCommand : IRevitCommand
{
    public string Name => "list_open_documents";
    public bool IsReadOnly => true;
    public JsonNode? Execute(CommandContext ctx)
    {
        var active = ctx.App.ActiveUIDocument?.Document;
        var docs = new JsonArray(DocumentResolver.OpenDocuments(ctx.App).Select(d => (JsonNode)new JsonObject
        {
            ["title"] = d.Title, ["pathName"] = d.PathName, ["isActive"] = DocumentResolver.Same(d, active),
            ["isModified"] = d.IsModified, ["isFamilyDocument"] = d.IsFamilyDocument, ["isWorkshared"] = d.IsWorkshared,
        }).ToArray());
        return new JsonObject { ["documents"] = docs, ["activeTitle"] = active?.Title,
            ["note"] = "Any command accepts documentTitle or documentPath to target an open document without activating it (UI actions excepted)." };
    }
}

/// <summary>UI action: make an already open (saved) document the active one.</summary>
public sealed class ActivateDocumentCommand : IRevitCommand
{
    public string Name => "activate_document";
    public bool IsReadOnly => false;
    public ExecutionKind Execution => ExecutionKind.UiAction;
    public string RiskLevel => "low";
    public JsonNode? Execute(CommandContext ctx)
    {
        var p = ctx.Parameters;
        var title = P.StrOrNull(p, "title"); var path = P.StrOrNull(p, "path");
        if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(path))
            throw new RevitCommandException("invalid_parameter", "Pass title or path of an open document.");
        var matches = DocumentResolver.OpenDocuments(ctx.App).Where(d => DocumentResolver.Matches(d.Title, d.PathName, title, path)).ToList();
        if (matches.Count == 0 && P.BoolOr(p, "allowOpen", false) && !string.IsNullOrWhiteSpace(path))
        {
            if (!System.IO.File.Exists(path)) throw new RevitCommandException("not_found", $"File not found: {path}");
            var previous = ctx.App.ActiveUIDocument?.Document?.Title;
            var opened = ctx.App.OpenAndActivateDocument(path);
            return new JsonObject { ["activeTitle"] = opened.Document.Title, ["previousTitle"] = previous, ["changed"] = true, ["opened"] = true };
        }
        if (matches.Count != 1)
            throw new RevitCommandException(matches.Count == 0 ? "document_not_open" : "ambiguous_selection",
                $"{matches.Count} open documents match. This command does not open closed files.");
        var target = matches[0];
        var before = ctx.App.ActiveUIDocument?.Document;
        if (DocumentResolver.Same(target, before))
            return new JsonObject { ["activeTitle"] = target.Title, ["changed"] = false };
        if (string.IsNullOrWhiteSpace(target.PathName))
            throw new RevitCommandException("unsupported_document", "Unsaved documents cannot be activated through the API.");
        var ui = ctx.App.OpenAndActivateDocument(target.PathName);
        var now = ctx.App.ActiveUIDocument?.Document;
        if (!DocumentResolver.Same(now, target))
            throw new RevitCommandException("verification_failed", $"Active document is '{now?.Title}'.");
        return new JsonObject { ["activeTitle"] = ui.Document.Title, ["previousTitle"] = before?.Title, ["changed"] = true };
    }
}

/// <summary>
/// Save the target document to its own file, or save a copy under saveAsPath. Asked for on
/// 11/09/2026: a whole roof lived only in memory because the agent could not save. Self-managed
/// (Revit refuses to save while a transaction is open); dryRun reports what would be written.
/// Verifies afterwards that the document is no longer modified and the file timestamp moved.
/// </summary>
public sealed class SaveDocumentCommand : IRevitCommand
{
    public string Name => "save_document";
    public bool IsReadOnly => false;
    public ExecutionKind Execution => ExecutionKind.SelfManagedWrite;
    public string RiskLevel => "medium";

    public JsonNode? Execute(CommandContext ctx)
    {
        var doc = ctx.RequireDoc();
        var p = ctx.Parameters;
        var saveAs = P.StrOrNull(p, "saveAsPath");
        var overwrite = P.BoolOr(p, "overwrite", false);
        var compact = P.BoolOr(p, "compact", false);
        var wasModified = doc.IsModified;

        if (doc.IsModifiable)
            throw new RevitCommandException("document_busy", "A transaction or edit mode is open in Revit; finish it before saving.");

        string target;
        if (string.IsNullOrWhiteSpace(saveAs))
        {
            if (string.IsNullOrWhiteSpace(doc.PathName))
                throw new RevitCommandException("never_saved", "Document was never saved; pass saveAsPath.");
            if (doc.IsReadOnlyFile)
                throw new RevitCommandException("read_only_file", $"'{doc.PathName}' was opened read-only.");
            target = doc.PathName;
        }
        else
        {
            target = System.IO.Path.GetFullPath(saveAs!);
            var ext = System.IO.Path.GetExtension(target);
            var expected = doc.IsFamilyDocument ? ".rfa" : ".rvt";
            if (!ext.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new RevitCommandException("invalid_parameter", $"saveAsPath must end with {expected} for this document.");
            if (!System.IO.Directory.Exists(System.IO.Path.GetDirectoryName(target)))
                throw new RevitCommandException("not_found", $"Folder does not exist: {System.IO.Path.GetDirectoryName(target)}");
            if (System.IO.File.Exists(target) && !overwrite)
                throw new RevitCommandException("file_exists", $"{target} exists; pass overwrite:true to replace it.");
            if (doc.IsWorkshared)
                throw new RevitCommandException("unsupported_document", "Save as for workshared models is not supported (worksharing options).");
            if (DocumentResolver.OpenDocuments(ctx.App).Any(d => !DocumentResolver.Same(d, doc)
                    && string.Equals(d.PathName, target, StringComparison.OrdinalIgnoreCase)))
                throw new RevitCommandException("file_in_use", $"{target} is open as another document.");
        }

        var info = new System.IO.FileInfo(target);
        if (info.Exists && info.IsReadOnly)
            throw new RevitCommandException("read_only_file", $"{target} is read-only on disk.");
        var before = info.Exists ? info.LastWriteTimeUtc : (DateTime?)null;
        var result = new JsonObject
        {
            ["title"] = doc.Title, ["path"] = target, ["mode"] = saveAs is null ? "save" : "saveAs",
            ["wasModified"] = wasModified, ["fileExisted"] = info.Exists, ["isWorkshared"] = doc.IsWorkshared, ["compact"] = compact,
        };
        if (ctx.DryRun)
        {
            result["note"] = "dryRun: nothing written.";
            return result;
        }

        if (saveAs is null) doc.Save(new SaveOptions { Compact = compact });
        else doc.SaveAs(target, new SaveAsOptions { OverwriteExistingFile = overwrite, Compact = compact });

        info.Refresh();
        var after = info.Exists ? info.LastWriteTimeUtc : (DateTime?)null;
        if (!info.Exists)
            throw new RevitCommandException("verification_failed", $"Revit reported no error but {target} does not exist.");
        if (doc.IsModified)
            throw new RevitCommandException("verification_failed", "Document still reports unsaved changes after saving.");
        result["title"] = doc.Title;
        result["pathAfter"] = doc.PathName;
        result["isModifiedAfter"] = doc.IsModified;
        result["fileWrittenUtc"] = after?.ToString("o");
        result["fileTimestampChanged"] = before is null || after != before;
        result["sizeBytes"] = info.Length;
        if (doc.IsWorkshared) result["note"] = "Saved the local file only; synchronize with central is not done by this command.";
        return result;
    }
}

/// <summary>Read-only: Revit's recent file list (Revit.ini), with existence and open state.</summary>
public sealed class ListRecentDocumentsCommand : IRevitCommand
{
    public string Name => "list_recent_documents";
    public bool IsReadOnly => true;

    public JsonNode? Execute(CommandContext ctx)
    {
        var query = P.StrOrNull(ctx.Parameters, "query");
        var paths = OpenDocumentCommand.RecentPaths(ctx, out var ini);
        if (!string.IsNullOrWhiteSpace(query)) paths = RecentDocuments.Match(query!, paths);
        var open = DocumentResolver.OpenDocuments(ctx.App).ToList();
        var items = new JsonArray(paths.Select((path, i) =>
        {
            var f = new System.IO.FileInfo(path);
            return (JsonNode)new JsonObject
            {
                ["path"] = path, ["fileName"] = f.Name, ["extension"] = f.Extension.ToLowerInvariant(), ["exists"] = f.Exists,
                ["isOpen"] = open.Any(d => DocumentResolver.Matches(d.Title, d.PathName, null, path)),
                ["lastWriteUtc"] = f.Exists ? f.LastWriteTimeUtc.ToString("o") : null,
                ["sizeBytes"] = f.Exists ? f.Length : null,
            };
        }).ToArray());
        return new JsonObject
        {
            ["iniPath"] = ini, ["query"] = query, ["count"] = items.Count, ["documents"] = items,
            ["note"] = "Revit rewrites this list when it closes; files opened in this session may be missing until then.",
        };
    }
}

/// <summary>
/// UI action: open a project/family file and (by default) activate it. Resolves `path`, or a
/// `name` spoken by the user against the recent file list. Refuses newer-version files, asks for
/// allowUpgrade on older ones and for detachFromCentral on central models. If the file is
/// already open it is only activated. Revit dialogs raised while opening (missing links, etc.)
/// still need the user.
/// </summary>
public sealed class OpenDocumentCommand : IRevitCommand
{
    public string Name => "open_document";
    public bool IsReadOnly => false;
    public ExecutionKind Execution => ExecutionKind.UiAction;
    public string RiskLevel => "low";

    internal static List<string> RecentPaths(CommandContext ctx, out string ini)
    {
        ini = RecentDocuments.IniPath(ctx.App.Application.VersionNumber);
        if (!System.IO.File.Exists(ini)) return new List<string>();
        // Revit.ini is UTF-16; let the BOM decide.
        using var reader = new System.IO.StreamReader(ini, detectEncodingFromByteOrderMarks: true);
        var lines = new List<string>();
        while (reader.ReadLine() is { } line) lines.Add(line);
        return RecentDocuments.Parse(lines);
    }

    public JsonNode? Execute(CommandContext ctx)
    {
        var p = ctx.Parameters;
        var path = P.StrOrNull(p, "path");
        var name = P.StrOrNull(p, "name");
        var activate = P.BoolOr(p, "activate", true);
        if (string.IsNullOrWhiteSpace(path) == string.IsNullOrWhiteSpace(name))
            throw new RevitCommandException("invalid_parameter", "Pass exactly one of path or name.");

        string? resolvedFrom = null;
        if (path is null)
        {
            var recent = RecentPaths(ctx, out _).Where(System.IO.File.Exists).ToList();
            var matches = RecentDocuments.Match(name!, recent);
            if (matches.Count != 1)
                throw new RevitCommandException(matches.Count == 0 ? "not_found" : "ambiguous_selection",
                    matches.Count == 0
                        ? $"No existing recent file matches '{name}'. Pass path, or check list_recent_documents."
                        : $"{matches.Count} recent files match '{name}': {string.Join(" | ", matches)}. Pass path.");
            path = matches[0];
            resolvedFrom = "recentFileList";
        }
        path = System.IO.Path.GetFullPath(path!);
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        if (ext is not (".rvt" or ".rfa"))
            throw new RevitCommandException("invalid_parameter", "Only .rvt and .rfa files can be opened (templates create new projects).");
        if (!System.IO.File.Exists(path))
            throw new RevitCommandException("not_found", $"File not found: {path}");

        var previous = ctx.App.ActiveUIDocument?.Document;
        var already = DocumentResolver.OpenDocuments(ctx.App).FirstOrDefault(d => DocumentResolver.Matches(d.Title, d.PathName, null, path));
        var result = new JsonObject { ["path"] = path, ["resolvedFrom"] = resolvedFrom, ["previousTitle"] = previous?.Title };

        BasicFileInfo? info = null;
        if (already is null)
        {
            info = BasicFileInfo.Extract(path);
            result["savedInVersion"] = info.Format;
            result["isWorkshared"] = info.IsWorkshared;
            if (info.IsSavedInLaterVersion)
                throw new RevitCommandException("unsupported_version", $"File saved in Revit {info.Format}, newer than {ctx.App.Application.VersionNumber}.");
            if (!info.IsSavedInCurrentVersion && !P.BoolOr(p, "allowUpgrade", false))
                throw new RevitCommandException("upgrade_required",
                    $"File is Revit {info.Format}; opening upgrades it in memory (slow, saving makes it unreadable in {info.Format}). Pass allowUpgrade:true.");
            if (info.IsWorkshared && info.IsCentral && !P.BoolOr(p, "detachFromCentral", false))
                throw new RevitCommandException("central_model",
                    "This is a central model. Open a local copy yourself, or pass detachFromCentral:true to open a detached copy (preserving worksets).");
        }

        var modelPath = ModelPathUtils.ConvertUserVisiblePathToModelPath(path);
        var options = new OpenOptions();
        if (info is { IsWorkshared: true, IsCentral: true })
            options.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;

        Document opened;
        if (already is not null && !activate)
        {
            opened = already;
        }
        else if (activate)
        {
            opened = (already is not null
                ? ctx.App.OpenAndActivateDocument(already.PathName)
                : ctx.App.OpenAndActivateDocument(modelPath, options, false)).Document;
            if (!DocumentResolver.Same(ctx.App.ActiveUIDocument?.Document, opened))
                throw new RevitCommandException("verification_failed", $"Active document is '{ctx.App.ActiveUIDocument?.Document?.Title}'.");
        }
        else
        {
            opened = ctx.App.Application.OpenDocumentFile(modelPath, options);
        }

        result["title"] = opened.Title;
        result["pathName"] = opened.PathName;
        result["alreadyOpen"] = already is not null;
        result["activated"] = activate;
        result["isActive"] = DocumentResolver.Same(ctx.App.ActiveUIDocument?.Document, opened);
        result["isDetached"] = options.DetachFromCentralOption != DetachFromCentralOption.DoNotDetach;
        result["note"] = activate ? null
            : "Opened without a window: target it with documentTitle; activate_document gives it a window.";
        return result;
    }
}

/// <summary>
/// Copy model elements or element types from another open document into the target
/// document (documentTitle or active). Existing destination types with the same name are
/// reused. Hosted instances may need their host copied too; view-specific elements are
/// rejected by Revit. The showroom file keeps the office's roof/structure examples.
/// </summary>
public sealed class CopyElementsFromDocumentCommand : IRevitCommand
{
    public string Name => "copy_elements_from_document";
    public bool IsReadOnly => false;
    public JsonNode? Execute(CommandContext ctx)
    {
        var dest = ctx.RequireDoc();
        var p = ctx.Parameters;
        var sourceTitle = P.StrOrNull(p, "sourceDocumentTitle"); var sourcePath = P.StrOrNull(p, "sourceDocumentPath");
        var sources = DocumentResolver.OpenDocuments(ctx.App).Where(d => DocumentResolver.Matches(d.Title, d.PathName, sourceTitle, sourcePath)).ToList();
        if (sources.Count != 1)
            throw new RevitCommandException("document_not_open", $"Source document must be open and unique ({sources.Count} matches).");
        var source = sources[0];
        if (DocumentResolver.Same(source, dest))
            throw new RevitCommandException("invalid_parameter", "Source and destination are the same document; use copy_element.");
        if (p["elementIds"] is not JsonArray raw || raw.Count == 0 || raw.Count > 500)
            throw new RevitCommandException("invalid_parameter", "elementIds: 1..500 source ids.");
        var ids = raw.Select(n => new ElementId(n!.GetValue<long>())).ToList();
        var missing = ids.Where(id => source.GetElement(id) is null).Select(id => id.Value).ToList();
        if (missing.Count > 0)
            throw new RevitCommandException("not_found", $"Not in source: {string.Join(",", missing)}.");
        var units = P.Units(p);
        var move = p["translation"] is null ? XYZ.Zero : P.Xyz(p, "translation", units);
        var options = new CopyPasteOptions();
        options.SetDuplicateTypeNamesHandler(new UseDestinationTypes());
        var copied = ElementTransformUtils.CopyElements(source, ids, dest, Transform.CreateTranslation(move), options);
        dest.Regenerate();
        var items = new JsonArray(copied.Select(id =>
        {
            var e = dest.GetElement(id);
            return (JsonNode)new JsonObject
            {
                ["id"] = id.Value, ["uniqueId"] = e?.UniqueId, ["name"] = e?.Name, ["category"] = e?.Category?.Name,
                ["isType"] = e is ElementType, ["typeId"] = e?.GetTypeId().Value,
            };
        }).ToArray());
        return new JsonObject
        {
            ["sourceDocument"] = source.Title, ["destinationDocument"] = dest.Title,
            ["requested"] = ids.Count, ["copiedCount"] = copied.Count, ["copied"] = items,
            ["note"] = "Destination types with the same name were kept. Check copied instances for lost hosts and positions; types may bring dependent families/materials.",
        };
    }

    sealed class UseDestinationTypes : IDuplicateTypeNamesHandler
    {
        public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args) => DuplicateTypeAction.UseDestinationTypes;
    }
}
