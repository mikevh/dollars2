using System.Net;

namespace Dollars2.Tests;

/// <summary>
/// An IHttpClientFactory whose clients always return one canned response, so provider code that
/// makes an outbound HTTP call can be exercised without a real network.
/// </summary>
internal sealed class StubHttpClientFactory : IHttpClientFactory
{
    private readonly string _responseBody;
    private readonly HttpStatusCode _status;

    public StubHttpClientFactory(string responseBody, HttpStatusCode status = HttpStatusCode.OK)
    {
        _responseBody = responseBody;
        _status = status;
    }

    public HttpClient CreateClient(string name) => new(new StubHandler(_responseBody, _status));

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly string _responseBody;
        private readonly HttpStatusCode _status;

        public StubHandler(string responseBody, HttpStatusCode status)
        {
            _responseBody = responseBody;
            _status = status;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(_status)
            {
                Content = new StringContent(_responseBody),
            });
        }
    }
}
