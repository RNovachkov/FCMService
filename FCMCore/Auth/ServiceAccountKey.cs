using System.Security.Cryptography;
using System.Text.Json;

namespace FCMCore.Auth;

/// <summary>
/// The fields read from a Google service-account JSON key file that are needed to obtain an OAuth2
/// access token for FCM.
/// </summary>
public sealed class ServiceAccountKey
{
    /// <summary>The service account's email address (the JWT <c>iss</c> and <c>sub</c> claim).</summary>
    public required string ClientEmail { get; init; }

    /// <summary>The PEM-encoded RSA private key (the <c>private_key</c> field).</summary>
    public required string PrivateKeyPem { get; init; }

    /// <summary>The OAuth2 token endpoint (the <c>token_uri</c> field).</summary>
    public required string TokenUri { get; init; }

    /// <summary>The Google Cloud / Firebase project ID (the <c>project_id</c> field), if present.</summary>
    public string? ProjectId { get; init; }

    /// <summary>Reads and parses a service-account JSON key from a file on disk.</summary>
    /// <param name="path">Path to the service-account JSON key file.</param>
    public static ServiceAccountKey FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
            throw new FileNotFoundException("The FCM service-account key file was not found.", path);

        return FromJson(File.ReadAllText(path));
    }

    /// <summary>Parses a service-account JSON key from its string content.</summary>
    /// <param name="json">The full JSON content of the service-account key.</param>
    public static ServiceAccountKey FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FcmAuthenticationException("The service-account key is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;

            var clientEmail = GetString(root, "client_email")
                ?? throw new FcmAuthenticationException("The service-account key is missing 'client_email'.");
            var privateKey = GetString(root, "private_key")
                ?? throw new FcmAuthenticationException("The service-account key is missing 'private_key'.");
            var tokenUri = GetString(root, "token_uri") ?? "https://oauth2.googleapis.com/token";
            var projectId = GetString(root, "project_id");

            return new ServiceAccountKey
            {
                ClientEmail = clientEmail,
                PrivateKeyPem = privateKey,
                TokenUri = tokenUri,
                ProjectId = projectId,
            };
        }
    }

    /// <summary>Imports the <see cref="PrivateKeyPem"/> into a new <see cref="RSA"/> instance.</summary>
    public RSA CreateRsa()
    {
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(PrivateKeyPem);
        }
        catch (Exception ex)
        {
            rsa.Dispose();
            throw new FcmAuthenticationException("The service-account 'private_key' is not a valid PEM-encoded RSA key.", ex);
        }

        return rsa;
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;
}
