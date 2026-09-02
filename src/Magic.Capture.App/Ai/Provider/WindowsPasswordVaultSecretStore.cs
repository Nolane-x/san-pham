using Magic.Capture.Core.Platform;
using Windows.Security.Credentials;

namespace Magic.Capture.App.Ai.Provider;

internal sealed class WindowsPasswordVaultSecretStore : IAiSecretStore
{
    private const string Resource = "Magic Capture Desktop AI";

    public Task SaveAsync(string secretId, string value)
    {
        if (string.IsNullOrWhiteSpace(secretId)) throw new ArgumentException("Secret id is required.", nameof(secretId));
        var vault = new PasswordVault();
        try
        {
            foreach (var existing in vault.FindAllByResource(Resource).Where(c => c.UserName == secretId).ToArray())
                vault.Remove(existing);
        }
        catch (Exception ex) when (CredentialVaultErrorPolicy.IsElementNotFound(ex))
        {
            // No credential exists yet; this is the documented PasswordVault empty-result behavior.
        }
        vault.Add(new PasswordCredential(Resource, secretId, value));
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(string secretId)
    {
        try
        {
            var credential = new PasswordVault().Retrieve(Resource, secretId);
            credential.RetrievePassword();
            return Task.FromResult<string?>(credential.Password);
        }
        catch (Exception ex) when (CredentialVaultErrorPolicy.IsElementNotFound(ex))
        {
            return Task.FromResult<string?>(null);
        }
    }

    public Task DeleteAsync(string secretId)
    {
        try
        {
            var vault = new PasswordVault();
            foreach (var credential in vault.FindAllByResource(Resource).Where(c => c.UserName == secretId).ToArray())
                vault.Remove(credential);
        }
        catch (Exception ex) when (CredentialVaultErrorPolicy.IsElementNotFound(ex))
        {
            // Deleting an already-missing credential is idempotent.
        }
        return Task.CompletedTask;
    }
}
