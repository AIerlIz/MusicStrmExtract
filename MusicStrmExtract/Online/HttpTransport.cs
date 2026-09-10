using System.Net.Http;

namespace MusicStrmExtract.Online;

internal sealed record HttpResponse(int StatusCode, string Body);

internal interface IHttpTransport : IDisposable
{
    Task<HttpResponse> GetAsync(string url, CancellationToken ct);
}

internal sealed class HttpClientTransport(HttpClient http) : IHttpTransport
{
    public async Task<HttpResponse> GetAsync(string url, CancellationToken ct)
    {
        using var response = await http
            .GetAsync(new Uri(url, UriKind.Absolute), HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return new HttpResponse((int)response.StatusCode, body);
    }

    public void Dispose()
    {
        http.Dispose();
    }
}
