using System.Net.Http.Headers;

namespace F1ReactionService.Auth;

/// <summary>
/// A delegating HTTP message handler that adds a Bearer token to outgoing requests using the specified token manager.
/// </summary>
/// <remarks>This handler is typically used to automatically attach authentication tokens to requests sent to APIs
/// that require Bearer token authentication. It should be added to the HTTP message handler pipeline before sending
/// requests to protected endpoints.</remarks>
/// <param name="tokenManager">The token manager responsible for acquiring and refreshing authentication tokens to be included in the Authorization
/// header of outgoing HTTP requests. Cannot be null.</param>
public class OpenF1AuthHandler(OpenF1TokenManager tokenManager) : DelegatingHandler {

	/// <inheritdoc/>
	protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) {
		var token = await tokenManager.GetTokenAsync(cancellationToken);

		if (!string.IsNullOrEmpty(token)) {
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
		}

		return await base.SendAsync(request, cancellationToken);
	}
}