using System.Text.Json;

namespace F1ReactionService.Auth;

/// <summary>
/// Manages the retrieval and caching of access tokens for the OpenF1 API using configured credentials.
/// </summary>
/// <remarks>This class handles token acquisition and renewal for the OpenF1 API. If credentials are not
/// configured, it assumes Free Tier access and does not request a token. Token requests are synchronized to prevent
/// concurrent refreshes. The class is intended for use in applications that require authenticated access to the OpenF1
/// API.</remarks>
/// <param name="config">The application configuration instance used to retrieve OpenF1 API credentials.</param>
/// <param name="logger">The logger used to record informational and error messages related to token management operations.</param>
public class OpenF1TokenManager(IConfiguration config, ILogger<OpenF1TokenManager> logger) {
	private string? _accessToken;
	private DateTimeOffset _tokenExpiration = DateTimeOffset.MinValue;
	private readonly SemaphoreSlim _semaphore = new(1, 1);

	/// <summary>
	/// Asynchronously retrieves an access token for the OpenF1 API, requesting a new token if the current one is expired
	/// or unavailable.
	/// </summary>
	/// <remarks>If the OpenF1 credentials are not configured, the method returns null, indicating that no token is
	/// necessary for the Free Tier. The method caches the token and only requests a new one when the current token is
	/// expired. This method is thread-safe and can be called concurrently.</remarks>
	/// <param name="cancellationToken">A cancellation token that can be used to cancel the token retrieval operation.</param>
	/// <returns>A task that represents the asynchronous operation. The task result contains the access token as a string if
	/// credentials are configured; otherwise, null if no token is required.</returns>
	public async Task<string?> GetTokenAsync(CancellationToken cancellationToken = default) {
		var username = config["OPENF1_USERNAME"];
		var password = config["OPENF1_PASSWORD"];

		if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password)) {
			return null; // Free Tier no token necessary
		}

		if (DateTimeOffset.UtcNow >= _tokenExpiration) {
			await _semaphore.WaitAsync(cancellationToken);
			try {
				if (DateTimeOffset.UtcNow >= _tokenExpiration) {
					logger.LogInformation("Requesting new OpenF1 API token...");
					using var authClient = new HttpClient();

					var content = new FormUrlEncodedContent([
						new KeyValuePair<string, string>("username", username),
						new KeyValuePair<string, string>("password", password)
					]);

					var response = await authClient.PostAsync("https://api.openf1.org/token", content, cancellationToken);

					if (response.IsSuccessStatusCode) {
						var json = await response.Content.ReadAsStringAsync(cancellationToken);
						using var doc = JsonDocument.Parse(json);

						_accessToken = doc.RootElement.GetProperty("access_token").GetString();

						if (doc.RootElement.TryGetProperty("expires_in", out var expiresProp) &&
							int.TryParse(expiresProp.GetString(), out int expiresIn)) {
							_tokenExpiration = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 120);
						} else {
							_tokenExpiration = DateTimeOffset.UtcNow.AddMinutes(58);
						}

						logger.LogInformation("Successfully retrieved new OpenF1 token.");
					} else {
						logger.LogError("Failed to retrieve OpenF1 token. Status: {StatusCode}", response.StatusCode);
					}
				}
			} finally {
				_semaphore.Release();
			}
		}

		return _accessToken;
	}
}