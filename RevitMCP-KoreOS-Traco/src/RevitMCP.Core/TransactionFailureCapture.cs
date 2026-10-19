using System.Text.Json.Nodes;
using Autodesk.Revit.DB;

namespace RevitMCPAddin;

/// <summary>Capture failures and roll back, never dismiss or resolve them silently.</summary>
internal sealed class TransactionFailureCapture : IFailuresPreprocessor
{
    internal JsonArray Messages { get; } = new();

    internal void Attach(Transaction transaction)
    {
        var options = transaction.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(this);
        options.SetClearAfterRollback(true);
        transaction.SetFailureHandlingOptions(options);
    }

    public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
    {
        var failures = accessor.GetFailureMessages();
        foreach (var failure in failures)
        {
            var ids = new JsonArray();
            foreach (var id in failure.GetFailingElementIds()) ids.Add(id.Value);
            var additionalIds = new JsonArray();
            foreach (var id in failure.GetAdditionalElementIds()) additionalIds.Add(id.Value);
            Messages.Add(new JsonObject
            {
                ["failureId"] = failure.GetFailureDefinitionId().Guid.ToString(),
                ["severity"] = failure.GetSeverity().ToString(),
                ["description"] = failure.GetDescriptionText(),
                ["elementIds"] = ids,
                ["additionalElementIds"] = additionalIds,
            });
        }
        return failures.Count > 0 ? FailureProcessingResult.ProceedWithRollBack : FailureProcessingResult.Continue;
    }
}
