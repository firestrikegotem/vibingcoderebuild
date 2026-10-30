using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.Net.Http.Headers;
using Roblox.Logging;
using Roblox.Models.Sessions;
using Roblox.Models.Users;
using Roblox.Services;
using Roblox.Website.Lib;
using ServiceProvider = Microsoft.Extensions.DependencyInjection.ServiceProvider;

namespace Roblox.Website.Middleware;

public class FrontendProxyMiddleware
{
    private RequestDelegate _next;

    public FrontendProxyMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public static List<string> BypassUrls = new()
    {
        "/apisite/",
        "/swagger/",
        "/api/",
        "/api/economy-chat/",
        // Razor Files
        "/feeds/getuserfeed",
        "/auth/",
        "/membership/notapproved.aspx",
        // Razor Public
        "/unsecuredcontent/",
        // Razor - Internal
        "/internal/year",
        "/internal/updates",
        "/internal/clothingstealer",
        "/internal/age",
        "/internal/report-abuse",
        "/internal/membership",
        "/internal/apply",
        "/internal/invite",
        "/internal/dev",
        "/internal/faq",
        "/internal/donate",
        "/internal/place-update",
        "/internal/create-place",
        "/internal/migrate-to-application",
        "/internal/collectibles",
        "/internal/contest/first-contest",
        "/auth/notapproved",
        // Admin
        "/admin-api/api",
        "/admin",
        // Web
        "/asset", // <--- ADDED THIS LINE TO FIX RCC 404
        "/thumbs/avatar.ashx",
        "/thumbs/avatar-headshot.ashx",
        "/thumbs/asset.ashx",
        "/user-sponsorship/",
        "/users/inventory/list-json",
        "/users/favorites/list-json",
        "/userads/redirect",
        "/users/profile/robloxcollections-json",
        "/asset/toggle-profile",
        "/comments/get-json",
        "/comments/post",
        "/usercheck/show-tos",
        "/search/users/results",
        "/users/set-builders-club",
        // Web - Game
        "/game/get-join-script",
        "/game/placelauncher.ashx",
        "/placelauncher.ashx",
        "/game/join.ashx",
        "/game/validate-machine",
        "/game/validateticket.ashx",
        "/game/get-join-script-debug",
        "/games/getgameinstancesjson",
        "/develop/upload",
        // gs
        "/gs/activity",
        "/gs/ping",
        "/gs/delete",
        "/gs/shutdown",
        "/gs/players/report",
        "/gs/a",
        "/api/moderation/filtertext",
        // hubs
        "/chat",
        "/chat/negotiate",
    };

    private static HttpClient _httpClient { get; set; } = new(new HttpClientHandler()
    {
        AllowAutoRedirect = false,
    });

    private async Task<HttpResponseMessage> ProxyRequestAsync(string url)
    {
        var fullUrl = "http://localhost:3000" + url;
        Console.WriteLine("[PROXY] {0}", fullUrl);
        var safeUrl = new Uri(fullUrl);
        if (safeUrl.Port != 3000)
            throw new ArgumentException("Unsafe Url: " + fullUrl);
        if (safeUrl.Host != "localhost")
            throw new ArgumentException("Unsafe Url: " + fullUrl);
        
        var result = await _httpClient.GetAsync(safeUrl);
        return result;
    }

    public Task HandleProxyResult(string url, string? contentType, int statusCode, string? locationHeader, HttpContext ctx)
    {
        var frontendTimer = new MiddlewareTimer(ctx, "FProxy");
        ctx.Response.ContentType = contentType ?? "text/html";
        ctx.Response.StatusCode = statusCode;
        // required for redirects
        if (locationHeader != null)
        {
            ctx.Response.Headers["location"] = locationHeader;
        }
        // cache _next stuff
#if RELEASE
        if (url.StartsWith("/_next/") && statusCode == 200)
        {
            ctx.Response.Headers.CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.FromDays(30),
                Public = true,
            }.ToString();
        }
#endif
        // tell cloudflare to STOP CACHING 404 ERRORS ON NEW NEXTJS FILES!!!!!
        if (statusCode == 404 || (statusCode > 499 && statusCode < 599))
        {
            ctx.Response.Headers.CacheControl = new CacheControlHeaderValue()
            {
                MaxAge = TimeSpan.Zero,
                Public = true,
                NoCache = true,
                MustRevalidate = true,
            }.ToString();
        }
        frontendTimer.Stop();
        return Task.CompletedTask;
    }

    private const string FrontendUnavailablePage = @"<!DOCTYPE html>
<html><head><title>Bloxyn is starting up</title>
<style>body{font-family:Arial,Helvetica,sans-serif;background:#e3e3e3;margin:0;padding:60px;text-align:center;color:#333}
h1{color:#00a23d;font-size:28px}p{font-size:15px;line-height:1.6}code{background:#fff;padding:2px 6px;border-radius:3px}</style>
</head><body>
<h1>Bloxyn is starting up</h1>
<p>The web frontend is not running yet, so this page cannot be rendered.</p>
<p>Start it with <code>npm run start</code> in <code>services/2016-roblox-main</code>, then reload.</p>
</body></html>";

    private static Dictionary<string, Tuple<string,string,string,int>> pageCache { get; set; } = new();
    private static Mutex pageCacheMux { get; set; } = new();

    private Tuple<string,string,string,int>? GetPageFromCache(string url)
    {
        pageCacheMux.WaitOne();
        if (pageCache.ContainsKey(url))
        {
            var value = pageCache[url];
            pageCacheMux.ReleaseMutex();
            return value;
        }
        pageCacheMux.ReleaseMutex();

        return null;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        var requestUrl = ctx.Request.GetEncodedPathAndQuery();
        foreach (var item in BypassUrls)
        {
            if (requestUrl.ToLower().StartsWith(item))
            {
                await _next(ctx);
                return;
            }
        }
#if RELEASE
        var cached = GetPageFromCache(requestUrl);
        if (cached != null)
        {
            ctx.Response.Headers.Add("x-cache-dbg", "f-2016; memv1;");
            await HandleProxyResult(requestUrl, cached.Item1, cached.Item4, cached.Item3, ctx);
            await ctx.Response.WriteAsync(cached.Item2);
            return;
        }
#endif
        HttpResponseMessage result;
        try
        {
            result = await ProxyRequestAsync(requestUrl);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            // Next.js is a separate process. If it is down, say so instead of throwing a 500 on
            // every single page - a 503 with a real explanation is debuggable, an empty 500 is not.
            Console.WriteLine("[PROXY] frontend on localhost:3000 is unreachable ({0})", e.Message);
            ctx.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            ctx.Response.ContentType = "text/html";
            await ctx.Response.WriteAsync(FrontendUnavailablePage);
            return;
        }
        var str = await result.Content.ReadAsStreamAsync();
        // First, copy to memory
        var mem = new MemoryStream();
        await str.CopyToAsync(mem);
        mem.Position = 0;
        // Make a string
        var cacheStr = await new StreamReader(mem).ReadToEndAsync();
        mem.Position = 0;
        var contentType = result.Content.Headers.ContentType?.ToString();
        var locationHeader = result.Headers.Location?.ToString();
        var cacheable = contentType != null && result.IsSuccessStatusCode && (
                contentType.Contains("application/javascript") ||
                contentType.Contains("text/html"));
        if (requestUrl.ToLower().StartsWith("/forum/"))
            cacheable = false;

        if (cacheable)
        {
            pageCacheMux.WaitOne();
            if (pageCache.Count < 1000)
            {
                pageCache[requestUrl] = new(contentType ?? "", cacheStr, locationHeader ?? "", 200);
            }
            else
            {
                Writer.Info(LogGroup.PerformanceDebugging, "2016 frontend page cache is full, not saving {0}", requestUrl);
            }
            pageCacheMux.ReleaseMutex();
        }
        await HandleProxyResult(requestUrl, contentType, (int)result.StatusCode, locationHeader, ctx);
        await mem.CopyToAsync(ctx.Response.BodyWriter.AsStream());

    }
}