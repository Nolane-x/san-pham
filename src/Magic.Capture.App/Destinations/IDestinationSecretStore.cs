namespace Magic.Capture.App.Destinations;

internal interface IDestinationSecretStore
{
    Task SaveAsync(string secretId, string value);
    Task<string?> GetAsync(string secretId);
    Task DeleteAsync(string secretId);
}
