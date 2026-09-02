using System.Numerics;

namespace Magic.Capture.Core.History;

public sealed record HistoryDuplicateGroup(string Key, IReadOnlyList<HistoryItem> Items, bool IsExact, int MaximumHammingDistance = 0);

public static class HistoryDuplicateIndex
{
    public const int MaximumNearDuplicateHammingDistance = 7;

    public static IReadOnlyList<HistoryDuplicateGroup> FindExact(IEnumerable<HistoryItem>? items)
    {
        return (items ?? [])
            .Where(item => item is not null && IsSha256(item.ContentSha256))
            .GroupBy(item => item.ContentSha256!.ToLowerInvariant(), StringComparer.Ordinal)
            .Where(group => group.Skip(1).Any())
            .Select(group => new HistoryDuplicateGroup(
                group.Key,
                group.OrderByDescending(item => item.CreatedUtc).ThenBy(item => item.Id).ToArray(),
                true))
            .OrderByDescending(group => group.Items.Count)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public static IReadOnlyList<HistoryDuplicateGroup> FindNear(IEnumerable<HistoryItem>? items, int maximumHammingDistance = 6)
    {
        if (maximumHammingDistance is < 0 or > MaximumNearDuplicateHammingDistance)
            throw new ArgumentOutOfRangeException(nameof(maximumHammingDistance));

        var candidates = (items ?? []).Where(item => item is not null && item.PerceptualHash64.HasValue).ToArray();
        if (candidates.Length < 2) return [];

        var parent = Enumerable.Range(0, candidates.Length).ToArray();
        var bandMaps = Enumerable.Range(0, 8).Select(_ => new Dictionary<byte, List<int>>()).ToArray();

        for (var index = 0; index < candidates.Length; index++)
        {
            var hash = candidates[index].PerceptualHash64!.Value;
            var compared = new HashSet<int>();
            for (var band = 0; band < 8; band++)
            {
                var value = (byte)((hash >> (band * 8)) & 0xFF);
                if (bandMaps[band].TryGetValue(value, out var prior))
                {
                    foreach (var otherIndex in prior)
                    {
                        if (!compared.Add(otherIndex)) continue;
                        var otherHash = candidates[otherIndex].PerceptualHash64!.Value;
                        if (HammingDistance(hash, otherHash) <= maximumHammingDistance)
                            Union(parent, index, otherIndex);
                    }
                }
                else
                {
                    bandMaps[band][value] = prior = [];
                }
                prior.Add(index);
            }
        }

        return Enumerable.Range(0, candidates.Length)
            .GroupBy(index => Find(parent, index))
            .Where(group => group.Skip(1).Any())
            .Select(group =>
            {
                var members = group.Select(index => candidates[index]).OrderByDescending(item => item.CreatedUtc).ThenBy(item => item.Id).ToArray();
                var max = 0;
                for (var i = 0; i < members.Length; i++)
                    for (var j = i + 1; j < members.Length; j++)
                        max = Math.Max(max, HammingDistance(members[i].PerceptualHash64!.Value, members[j].PerceptualHash64!.Value));
                return new HistoryDuplicateGroup($"dhash:{members[0].PerceptualHash64!.Value:x16}", members, false, max);
            })
            .OrderByDescending(group => group.Items.Count)
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToArray();
    }

    public static int HammingDistance(ulong left, ulong right) => BitOperations.PopCount(left ^ right);

    public static bool IsSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private static int Find(int[] parent, int index)
    {
        while (parent[index] != index)
        {
            parent[index] = parent[parent[index]];
            index = parent[index];
        }
        return index;
    }

    private static void Union(int[] parent, int left, int right)
    {
        var a = Find(parent, left);
        var b = Find(parent, right);
        if (a != b) parent[b] = a;
    }
}
