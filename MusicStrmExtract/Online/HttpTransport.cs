using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace MusicStrmExtract.Online
{
    internal sealed record HttpResponse(int StatusCode, string Body);

    internal interface IHttpTransport : IDisposable
    {
        Task<HttpResponse> GetAsync(string url, CancellationToken ct);
    }

    internal sealed class HttpClientTransport : IHttpTransport
    {
        private readonly HttpClient _http;

        public HttpClientTransport(HttpClient http)
        {
            _http = http;
        }

        public async Task<HttpResponse> GetAsync(string url, CancellationToken ct)
        {
            using var response = await _http
                .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return new HttpResponse((int)response.StatusCode, body);
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
