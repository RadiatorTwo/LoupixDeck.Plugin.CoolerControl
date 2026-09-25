using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Plugin.CoolerControl;

/// <summary>
/// Minimal REST client for the CoolerControl daemon API.
/// <para>
/// CoolerControl 4.0 and later require authentication for almost every endpoint and no longer
/// accept the default password. Such a daemon is reached with an access token (created in
/// CoolerControl under Access Protection), sent as a bearer token. Without a token the client
/// behaves as before: reads go out unauthenticated and a mode change logs in with the default
/// password, which is what daemons before 4.0 accept.
/// </para>
/// <para>
/// 4.0 also serves HTTPS with a self-signed certificate by default. That certificate is accepted
/// for a daemon on this machine (loopback address) only.
/// </para>
/// </summary>
public sealed class CoolerControlApiController
{
    public const string DefaultUrl = "http://127.0.0.1:11987/";

    private volatile HttpClient _client;
    private volatile string? _token;

    public CoolerControlApiController()
    {
        _client = Build(DefaultUrl);
    }

    /// <summary>Points the client at a new base URL and access token (empty for none).</summary>
    public void Configure(string baseUrl, string? token = null)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = DefaultUrl;

        baseUrl = baseUrl.Trim();
        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();

        HttpClient old = _client;
        _client = Build(baseUrl);
        old.Dispose();
    }

    private static HttpClient Build(string baseUrl)
    {
        Uri baseAddress = new(baseUrl);
        HttpClientHandler handler = new()
        {
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        if (baseAddress.IsLoopback)
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;

        return new HttpClient(handler)
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(2)
        };
    }

    public async Task<JArray> GetModes()
    {
        JObject jObj = await GetJson("modes");
        return (JArray?)jObj["modes"] ?? new JArray();
    }

    public async Task<bool> SetMode(string uid)
    {
        if (_token is null && !await Login())
            return false;

        using HttpResponseMessage response = await Send(HttpMethod.Post, $"modes-active/{uid}");
        return response.IsSuccessStatusCode;
    }

    private async Task<JObject> GetJson(string path)
    {
        using HttpResponseMessage response = await Send(HttpMethod.Get, path);
        response.EnsureSuccessStatusCode();
        return JObject.Parse(await response.Content.ReadAsStringAsync());
    }

    private Task<HttpResponseMessage> Send(HttpMethod method, string path)
    {
        HttpRequestMessage request = new(method, path);
        if (_token is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        return _client.SendAsync(request);
    }

    /// <summary>Session login with the default password; only daemons before 4.0 accept it.</summary>
    private async Task<bool> Login()
    {
        string basic = Convert.ToBase64String(Encoding.ASCII.GetBytes("CCAdmin:coolAdmin"));
        using HttpRequestMessage req = new(HttpMethod.Post, "login");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        using HttpResponseMessage response = await _client.SendAsync(req);
        return response.IsSuccessStatusCode;
    }
}
