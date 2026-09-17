using System.Net;

namespace ZapperRadio.Core.Tests;

/// <summary>Serves canned responses by URL; null content means 404.</summary>
internal sealed class FakeHandler(Func<Uri, string?> content) : HttpMessageHandler
{
    public bool Offline { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Offline)
        {
            throw new HttpRequestException("offline");
        }

        var body = content(request.RequestUri!);
        return Task.FromResult(body is null
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
