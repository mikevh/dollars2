using System.Net;

namespace Dollars2.Tests;

/// <summary>
/// An IHttpClientFactory whose clients answer successive requests with successive canned responses, so
/// provider code that pages through an upstream API can be exercised across more than one page —
/// something <see cref="StubHttpClientFactory"/>, which repeats a single response forever, cannot do.
/// </summary>
/// <remarks>
/// The queue is shared by every client handed out, because the code under test may create a client per
/// request rather than reusing one. Exhausting it throws: a paging loop that asks for more pages than
/// the test scripted has misread has_more, and that is a bug the test should surface rather than absorb
/// by repeating the last response (which would spin forever on a page that never ends the stream).
/// </remarks>
internal sealed class QueuedHttpClientFactory : IHttpClientFactory
{
    private readonly Queue<string> _responseBodies;

    public QueuedHttpClientFactory(params string[] responseBodies)
    {
        _responseBodies = new Queue<string>(responseBodies);
    }

    /// <summary>Responses not yet consumed — assert this is 0 to prove every scripted page was fetched.</summary>
    public int RemainingResponses => _responseBodies.Count;

    public HttpClient CreateClient(string name) => new(new QueuedHandler(_responseBodies));

    private sealed class QueuedHandler : HttpMessageHandler
    {
        private readonly Queue<string> _responseBodies;

        public QueuedHandler(Queue<string> responseBodies)
        {
            _responseBodies = responseBodies;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (_responseBodies.Count == 0)
            {
                throw new InvalidOperationException(
                    "The provider requested more responses than this test scripted; check the paging loop's has_more handling.");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(_responseBodies.Dequeue()),
            });
        }
    }
}
