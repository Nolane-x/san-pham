namespace Magic.Capture.Core.History;

public static class HistoryRetentionPlanner
{
    public static IReadOnlySet<Guid> SelectForDeletion(
        IReadOnlyCollection<HistoryItem> items,
        HistoryRetentionPolicy policy,
        DateTimeOffset now)
    {
        var deleted = new HashSet<Guid>();
        var ordered = items.OrderByDescending(item => item.CreatedUtc).ToArray();

        if (policy.MaximumAgeDays is > 0)
        {
            var cutoff = now.AddDays(-policy.MaximumAgeDays.Value);
            foreach (var item in ordered.Where(item => item.CreatedUtc < cutoff)) deleted.Add(item.Id);
        }

        var remaining = ordered.Where(item => !deleted.Contains(item.Id)).ToArray();
        if (policy.MaximumCount is >= 0 && remaining.Length > policy.MaximumCount.Value)
        {
            foreach (var item in remaining.Skip(policy.MaximumCount.Value)) deleted.Add(item.Id);
        }

        remaining = ordered.Where(item => !deleted.Contains(item.Id)).ToArray();
        if (policy.MaximumBytes is >= 0)
        {
            long keptBytes = 0;
            var budgetExhausted = false;
            foreach (var item in remaining)
            {
                var fileBytes = Math.Max(0, item.FileBytes);
                if (!budgetExhausted && fileBytes <= policy.MaximumBytes.Value - keptBytes)
                {
                    keptBytes += fileBytes;
                    continue;
                }

                budgetExhausted = true;
                deleted.Add(item.Id);
            }
        }

        return deleted;
    }
}
