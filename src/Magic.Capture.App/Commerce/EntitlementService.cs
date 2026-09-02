using Magic.Capture.Core.Commerce;

namespace Magic.Capture.App.Commerce;

internal sealed class EntitlementService
{
    private readonly TrialStateStore _trialStore;
    private readonly StorePurchaseService _store;
    private TrialState? _trialState;

    public EntitlementService(TrialStateStore trialStore, StorePurchaseService store)
    {
        _trialStore = trialStore;
        _store = store;
    }

    public EntitlementSnapshot Current { get; private set; } =
        EntitlementSnapshot.Create(ProductTier.Free, DateTimeOffset.UtcNow, null, false, "startup");

    public event EventHandler<EntitlementSnapshot>? Changed;

    public bool CanUse(ProductFeature feature) => Current.CanUse(feature);

    public async Task InitializeAsync(IntPtr ownerHwnd, CancellationToken cancellationToken = default)
    {
        _store.Initialize(ownerHwnd);
        _trialState = await _trialStore.LoadOrCreateAsync(DateTimeOffset.UtcNow, cancellationToken);
        await RefreshAsync(cancellationToken);
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        _trialState ??= await _trialStore.LoadOrCreateAsync(now, cancellationToken);
        var trial = TrialClock.Evaluate(_trialState, now);
        _trialState = trial.UpdatedState;
        await _trialStore.SaveAsync(_trialState, cancellationToken);

        var cache = await _store.LoadCacheAsync(cancellationToken);
        var store = await _store.QueryOwnershipAsync(cancellationToken);

        var isPro = store.StoreReachable ? store.IsProOwned : cache.ConfirmedPro;
        if (store.StoreReachable)
        {
            cache = isPro
                ? new StoreEntitlementCache(true, now)
                : new StoreEntitlementCache(false, cache.ConfirmedUtc);
            await _store.SaveCacheAsync(cache, cancellationToken);
        }

        var tier = isPro ? ProductTier.ProLifetime : trial.Tier;
        Current = EntitlementSnapshot.Create(
            tier,
            trial.EffectiveNowUtc,
            trial.EndsUtc,
            isPro,
            isPro ? "microsoft-store" : trial.IsActive ? "plus-trial" : "free");
        Changed?.Invoke(this, Current);
    }

    public async Task<StorePurchaseOutcome> PurchaseProAsync()
    {
        var outcome = await _store.PurchaseProAsync();
        if (outcome.Succeeded) await RefreshAsync();
        return outcome;
    }

    public bool ShouldShowTrialExpiredNotice =>
        Current.Tier == ProductTier.Free && _trialState is { ExpiryNoticeShown: false } &&
        Current.TrialEndsUtc is { } end && Current.EvaluatedUtc >= end;

    public async Task MarkTrialExpiryNoticeShownAsync()
    {
        if (_trialState is null) return;
        _trialState = _trialState with { ExpiryNoticeShown = true };
        await _trialStore.SaveAsync(_trialState);
    }
}
