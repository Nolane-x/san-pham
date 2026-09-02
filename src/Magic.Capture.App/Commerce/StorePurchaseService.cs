using Magic.Capture.App.Persistence;
using Windows.Services.Store;

namespace Magic.Capture.App.Commerce;

internal sealed record StoreOwnershipResult(bool StoreReachable, bool IsProOwned, string? ErrorMessage);
internal sealed record StorePurchaseOutcome(bool Succeeded, string Message);
internal sealed record StorePriceQuote(bool Available, string? FormattedPrice, string? FormattedBasePrice, bool IsOnSale, DateTimeOffset? SaleEndDate, string? ErrorMessage);
internal sealed record StoreEntitlementCache(bool ConfirmedPro, DateTimeOffset? ConfirmedUtc);

internal sealed class StorePurchaseService
{
    public const string ProOfferToken = "magiccapture.desktop.pro";

    private readonly AppPaths _paths;
    private readonly LocalLog _log;
    private StoreContext? _context;
    private IntPtr _ownerHwnd;

    public StorePurchaseService(AppPaths paths, LocalLog log)
    {
        _paths = paths;
        _log = log;
    }

    public void Initialize(IntPtr ownerHwnd)
    {
        _ownerHwnd = ownerHwnd;
        _context ??= StoreContext.GetDefault();
        if (_ownerHwnd != IntPtr.Zero)
            WinRT.Interop.InitializeWithWindow.Initialize(_context, _ownerHwnd);
    }

    public async Task<StoreEntitlementCache> LoadCacheAsync(CancellationToken cancellationToken = default) =>
        await AtomicJsonFile.ReadAsync<StoreEntitlementCache>(_paths.EntitlementCacheFile, cancellationToken)
        ?? new StoreEntitlementCache(false, null);

    public Task SaveCacheAsync(StoreEntitlementCache cache, CancellationToken cancellationToken = default) =>
        AtomicJsonFile.WriteAsync(_paths.EntitlementCacheFile, cache, cancellationToken);

    public async Task<StoreOwnershipResult> QueryOwnershipAsync(CancellationToken cancellationToken = default)
    {
        if (_context is null) return new StoreOwnershipResult(false, false, "StoreContext is not initialized.");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var license = await _context.GetAppLicenseAsync();
            var owned = license.AddOnLicenses.Values.Any(addOn =>
                addOn.IsActive && string.Equals(addOn.InAppOfferToken, ProOfferToken, StringComparison.OrdinalIgnoreCase));

            if (!owned)
            {
                var product = await FindProProductAsync();
                owned = product?.IsInUserCollection == true;
            }

            return new StoreOwnershipResult(true, owned, null);
        }
        catch (Exception ex)
        {
            _log.Error("StoreOwnership", ex);
            return new StoreOwnershipResult(false, false, ex.Message);
        }
    }

    public async Task<StorePurchaseOutcome> PurchaseProAsync()
    {
        if (_context is null) return new StorePurchaseOutcome(false, "Microsoft Store is not initialized.");
        try
        {
            var product = await FindProProductAsync();
            if (product is null)
                return new StorePurchaseOutcome(false, "Magic Capture Desktop Pro is not associated with this Store build yet.");

            if (product.IsInUserCollection)
                return new StorePurchaseOutcome(true, "Magic Capture Desktop Pro is already owned.");

            var result = await product.RequestPurchaseAsync();
            return result.Status switch
            {
                StorePurchaseStatus.Succeeded => new StorePurchaseOutcome(true, "Magic Capture Desktop Pro unlocked."),
                StorePurchaseStatus.AlreadyPurchased => new StorePurchaseOutcome(true, "Magic Capture Desktop Pro is already owned."),
                StorePurchaseStatus.NotPurchased => new StorePurchaseOutcome(false, "Purchase was cancelled."),
                StorePurchaseStatus.NetworkError => new StorePurchaseOutcome(false, "Microsoft Store is currently unreachable."),
                StorePurchaseStatus.ServerError => new StorePurchaseOutcome(false, "Microsoft Store returned a server error."),
                _ => new StorePurchaseOutcome(false, $"Purchase could not be completed ({result.Status}).")
            };
        }
        catch (Exception ex)
        {
            _log.Error("StorePurchase", ex);
            return new StorePurchaseOutcome(false, ex.Message);
        }
    }

    public async Task<StorePriceQuote> QueryProPriceAsync()
    {
        if (_context is null)
            return new StorePriceQuote(false, null, null, false, null, "Microsoft Store is not initialized.");

        try
        {
            var product = await FindProProductAsync();
            if (product is null)
                return new StorePriceQuote(false, null, null, false, null, "Magic Capture Desktop Pro is not associated with this Store build yet.");

            var price = product.Price;
            return new StorePriceQuote(
                true,
                price.FormattedPrice,
                price.FormattedBasePrice,
                price.IsOnSale,
                price.IsOnSale ? (DateTimeOffset?)price.SaleEndDate : null,
                null);
        }
        catch (Exception ex)
        {
            _log.Error("StorePrice", ex);
            return new StorePriceQuote(false, null, null, false, null, ex.Message);
        }
    }

    public async Task<StoreProduct?> FindProProductAsync()
    {
        if (_context is null) return null;
        var query = await _context.GetAssociatedStoreProductsByInAppOfferTokenAsync([ProOfferToken]);
        if (query.ExtendedError is not null) throw query.ExtendedError;
        return query.Products.Values.FirstOrDefault(product =>
            string.Equals(product.InAppOfferToken, ProOfferToken, StringComparison.OrdinalIgnoreCase));
    }
}
