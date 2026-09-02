namespace Magic.Capture.App.Ai.Provider;

internal interface IAiSecretStore
{
    Task SaveAsync(string secretId, string value);
    Task<string?> GetAsync(string secretId);
    Task DeleteAsync(string secretId);
}
