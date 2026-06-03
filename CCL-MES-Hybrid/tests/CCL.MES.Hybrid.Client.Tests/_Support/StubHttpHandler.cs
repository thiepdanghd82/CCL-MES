using System.Net;
using System.Net.Http.Json;

namespace CCL.MES.Hybrid.Client.Tests._Support;

/// <summary>
/// Minimal HttpMessageHandler used as the leaf of the test handler chain.
/// Records every request received and returns whatever the supplied
/// <see cref="Responder"/> delegate produces. We avoid taking a real
/// mocking library so the test surface stays trivial.
/// </summary>
public sealed class StubHttpHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    public Func<HttpRequestMessage, int, Task<HttpResponseMessage>> Responder { get; set; }
        = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Snapshot the request *before* handing to the responder — the
        // request gets mutated (eg. headers) so we save a clone of the
        // path + headers for assertion purposes.
        var snapshot = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var h in request.Headers)
            snapshot.Headers.TryAddWithoutValidation(h.Key, h.Value);
        Requests.Add(snapshot);

        var index = Requests.Count - 1;
        return await Responder(request, index);
    }

    public static HttpResponseMessage Json<T>(HttpStatusCode status, T body)
    {
        var resp = new HttpResponseMessage(status)
        {
            Content = JsonContent.Create(body),
        };
        return resp;
    }
}
