using Magic.Capture.Core.ScreenGraph;

namespace Magic.Capture.Core.Ai;

public static class EvidenceResolver
{
    public static IReadOnlyList<ResolvedEvidence> Resolve(ScreenGraphDocument graph, IEnumerable<string> evidenceIds, string? prefix = null)
    {
        var result = new List<ResolvedEvidence>();
        foreach (var evidenceId in evidenceIds.Distinct(StringComparer.Ordinal))
        {
            var localId = evidenceId;
            if (!string.IsNullOrWhiteSpace(prefix))
            {
                var expected = prefix + ":";
                if (!evidenceId.StartsWith(expected, StringComparison.Ordinal)) continue;
                localId = evidenceId[expected.Length..];
            }
            else if (evidenceId.Contains(':'))
            {
                continue;
            }

            var node = graph.Find(localId);
            if (node is null) continue;
            result.Add(new ResolvedEvidence(graph.CaptureId, evidenceId, node.Id, node.Kind, node.Text, node.Bounds, node.Confidence));
        }
        return result;
    }
}
