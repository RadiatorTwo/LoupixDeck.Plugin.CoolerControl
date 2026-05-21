using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json.Linq;

namespace LoupixDeck.Plugin.CoolerControl;

/// <summary>Minimal REST client for the CoolerControl daemon API.</summary>
public sealed class CoolerControlApiController
{
    private HttpClient _client;

    public CoolerControlApiController()
    {
        _client = Build("http://127.0.0.1:11987/");
    }

    /// <summary>Points the client at a new base URL.</summary>
    public void Configure(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            baseUrl = "http://127.0.0.1:11987/";

        if (!baseUrl.EndsWith('/'))
            baseUrl += "/";

        var old = _client;
        _client = Build(baseUrl);
        old.Dispose();
    }

    private static HttpClient Build(string baseUrl)
    {
        var handler = new HttpClientHandler
        {
            UseCookies = true,
            CookieContainer = new CookieContainer()
        };

        return new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromSeconds(2)
        };
    }

    public async Task<JArray> GetModes()
    {
        var json = await _client.GetStringAsync("modes");
        var jObj = JObject.Parse(json);
        return (JArray?)jObj["modes"] ?? new JArray();
    }

    public async Task<bool> SetMode(string uid)
    {
        if (!await Login())
            return false;

        var response = await _client.PostAsync($"modes-active/{uid}", null);
        return response.IsSuccessStatusCode;
    }

    private async Task<bool> Login()
    {
        var basic = Convert.ToBase64String(Encoding.ASCII.GetBytes("CCAdmin:coolAdmin"));
        var req = new HttpRequestMessage(HttpMethod.Post, "login");
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);

        var response = await _client.SendAsync(req);
        return response.IsSuccessStatusCode;
    }
}
