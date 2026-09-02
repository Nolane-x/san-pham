using Magic.Capture.App.Persistence;
using Magic.Capture.Core.LocalActions;
using Magic.Capture.Core.Storage;

namespace Magic.Capture.App.LocalActions;

internal sealed class LocalActionApprovalStore
{
    private readonly AppPaths _paths;

    public LocalActionApprovalStore(AppPaths paths) => _paths = paths;

    public async Task<IReadOnlyList<LocalActionApproval>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var approvals = await AtomicJsonFile.ReadAsync<List<LocalActionApproval>>(
            _paths.LocalActionApprovalsFile, cancellationToken, LocalConfigurationLimits.MaximumLocalActionApprovalJsonBytes) ?? [];
        LocalConfigurationLimits.ValidateCount(approvals.Count, LocalConfigurationLimits.MaximumLocalActionApprovals, "Local Action approvals");

        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var approval in approvals)
        {
            if (!LocalActionApprovalPolicy.IsValid(approval))
                throw new InvalidDataException("Local Action approval storage contains an invalid entry.");
            var key = approval.ExecutablePath + "\n" + approval.Sha256;
            if (!keys.Add(key)) throw new InvalidDataException("Local Action approval storage contains a duplicate entry.");
        }
        return approvals.ToArray();
    }

    public async Task<bool> IsApprovedAsync(string executablePath, string sha256, CancellationToken cancellationToken = default)
    {
        var approvals = await LoadAsync(cancellationToken);
        return approvals.Any(a =>
            string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(a.Sha256, sha256, StringComparison.OrdinalIgnoreCase));
    }

    public async Task ApproveAsync(string executablePath, string sha256, CancellationToken cancellationToken = default)
    {
        var approvals = (await LoadAsync(cancellationToken)).ToList();
        approvals.RemoveAll(a => string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase));
        approvals.Add(new LocalActionApproval(executablePath, sha256.ToUpperInvariant(), DateTimeOffset.UtcNow));
        LocalConfigurationLimits.ValidateCount(approvals.Count, LocalConfigurationLimits.MaximumLocalActionApprovals, "Local Action approvals");
        await AtomicJsonFile.WriteAsync(
            _paths.LocalActionApprovalsFile, approvals, cancellationToken, LocalConfigurationLimits.MaximumLocalActionApprovalJsonBytes);
    }

    public async Task RevokeAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        var approvals = (await LoadAsync(cancellationToken)).ToList();
        var changed = approvals.RemoveAll(a => string.Equals(a.ExecutablePath, executablePath, StringComparison.OrdinalIgnoreCase)) > 0;
        if (!changed) return;
        await AtomicJsonFile.WriteAsync(
            _paths.LocalActionApprovalsFile, approvals, cancellationToken, LocalConfigurationLimits.MaximumLocalActionApprovalJsonBytes);
    }
}
