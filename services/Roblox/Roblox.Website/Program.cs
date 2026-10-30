using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Json;
using Roblox.Rendering;
using Roblox.Website.Middleware;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using Roblox;
using Roblox.Services;
using Roblox.Services.App.FeatureFlags;
using Roblox.Website.Hubs;
using Roblox.Website.WebsiteModels;

var domain = AppDomain.CurrentDomain;
// Set a timeout interval of 5 seconds.
domain.SetData("REGEX_DEFAULT_MATCH_TIMEOUT", TimeSpan.FromSeconds(5));

IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .Build();

var builder = WebApplication.CreateBuilder(args);

// DB
Roblox.Services.Database.Configure(configuration.GetSection("Postgres").Value);
Roblox.Services.Cache.Configure(configuration.GetSection("Redis").Value);

// Config
Roblox.Configuration.CdnBaseUrl = configuration.GetSection("CdnBaseUrl").Value;
Roblox.Configuration.AssetDirectory = configuration.GetSection("Directories:Asset").Value;
Roblox.Configuration.StorageDirectory = configuration.GetSection("Directories:Storage").Value;
Roblox.Configuration.ThumbnailsDirectory = configuration.GetSection("Directories:Thumbnails").Value;
Roblox.Configuration.GroupIconsDirectory = configuration.GetSection("Directories:GroupIcons").Value;
Roblox.Configuration.PublicDirectory = configuration.GetSection("Directories:Public").Value;
Roblox.Configuration.XmlTemplatesDirectory = configuration.GetSection("Directories:XmlTemplates").Value;
Roblox.Configuration.JsonDataDirectory = configuration.GetSection("Directories:JsonData").Value;
Roblox.Configuration.AdminBundleDirectory = configuration.GetSection("Directories:AdminBundle").Value;
Roblox.Configuration.EconomyChatBundleDirectory = configuration.GetSection("Directories:EconomyChatBundle").Value;
Roblox.Configuration.BaseUrl = configuration.GetSection("BaseUrl").Value;
Roblox.Configuration.HCaptchaPublicKey = configuration.GetSection("HCaptcha:Public").Value;
Roblox.Configuration.HCaptchaPrivateKey = configuration.GetSection("HCaptcha:Private").Value;
Roblox.Configuration.GameServerAuthorization = configuration.GetSection("GameServerAuthorization").Value;
Roblox.Configuration.BotAuthorization = configuration.GetSection("BotAuthorization").Value;

// game-server config stuff
IConfiguration gameServerConfig = new ConfigurationBuilder().AddJsonFile("game-servers.json").Build();
Roblox.Configuration.GameServerIpAddresses = gameServerConfig.GetSection("GameServers").Get<IEnumerable<GameServerConfigEntry>>();
Roblox.Configuration.RccAuthorization = configuration.GetSection("RccAuthorization").Value;
Roblox.Configuration.AssetValidationServiceUrl = configuration.GetSection("AssetValidation:BaseUrl").Value;
Roblox.Configuration.AssetValidationServiceAuthorization = configuration.GetSection("AssetValidation:Authorization").Value;

Roblox.Services.GameServerService.Configure(string.Join(Guid.NewGuid().ToString(), new int [16].Select(_ => Guid.NewGuid().ToString())));

// Package Clothing
Roblox.Configuration.PackageShirtAssetId = long.Parse(configuration.GetSection("PackageShirtAssetId").Value);
Roblox.Configuration.PackagePantsAssetId = long.Parse(configuration.GetSection("PackagePantsAssetId").Value);
Roblox.Configuration.PackageLeftArmAssetId = long.Parse(configuration.GetSection("PackageLeftArmAssetId").Value);
Roblox.Configuration.PackageRightArmAssetId = long.Parse(configuration.GetSection("PackageRightArmAssetId").Value);
Roblox.Configuration.PackageLeftLegAssetId = long.Parse(configuration.GetSection("PackageLeftLegAssetId").Value);
Roblox.Configuration.PackageRightLegAssetId = long.Parse(configuration.GetSection("PackageRightLegAssetId").Value);
Roblox.Configuration.PackageTorsoAssetId = long.Parse(configuration.GetSection("PackageTorsoAssetId").Value);
Roblox.Libraries.TwitterApi.TwitterApi.Configure(configuration.GetSection("Twitter:Bearer").Value);

// Discord (signup gate). Environment variables win over appsettings so the client secret and bot
// token do not have to live in the repo. A blank value in appsettings also falls through to the
// environment, which is how the secret is supplied locally.
static string DiscordSetting(IConfiguration configuration, string key, string? envName)
{
    var fromFile = configuration.GetSection("Discord:" + key).Value;
    if (!string.IsNullOrWhiteSpace(fromFile)) return fromFile.Trim();
    if (!string.IsNullOrWhiteSpace(envName))
    {
        var fromEnv = Environment.GetEnvironmentVariable(envName);
        if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv.Trim();
    }
    return "";
}

Roblox.Configuration.DiscordClientId = DiscordSetting(configuration, "ClientId", "DISCORD_CLIENT_ID");
Roblox.Configuration.DiscordClientSecret = DiscordSetting(configuration, "ClientSecret", "DISCORD_CLIENT_SECRET");
Roblox.Configuration.DiscordBotToken = DiscordSetting(configuration, "BotToken", "DISCORD_BOT_TOKEN");
Roblox.Configuration.DiscordGuildId = DiscordSetting(configuration, "GuildId", "DISCORD_GUILD_ID");
Roblox.Configuration.DiscordGuildInviteUrl = DiscordSetting(configuration, "InviteUrl", null);
if (string.IsNullOrWhiteSpace(Roblox.Configuration.DiscordGuildInviteUrl))
    Roblox.Configuration.DiscordGuildInviteUrl = "https://discord.gg/8dt3QVWd3j";
// Blank redirect URI means "derive it from the request", so the same build works on
// http://127.0.0.1:5000 and on https://bloxyn.net without editing config.
Roblox.Configuration.DiscordRedirectUri = DiscordSetting(configuration, "RedirectUri", "DISCORD_REDIRECT_URI");
Roblox.Configuration.DiscordVerificationKey = DiscordSetting(configuration, "VerificationKey", "DISCORD_VERIFICATION_KEY");
if (string.IsNullOrWhiteSpace(Roblox.Configuration.DiscordVerificationKey))
    Roblox.Configuration.DiscordVerificationKey = Roblox.Configuration.GameServerAuthorization;
if (configuration.GetSection("Discord:SignupGateEnabled").Value != null)
    Roblox.Configuration.DiscordSignupGateEnabled =
        !configuration.GetSection("Discord:SignupGateEnabled").Value!.Equals("false", StringComparison.OrdinalIgnoreCase);
Roblox.Libraries.Discord.DiscordApi.Configure(Roblox.Configuration.DiscordClientId,
    Roblox.Configuration.DiscordClientSecret, Roblox.Configuration.DiscordGuildId,
    Roblox.Configuration.DiscordGuildInviteUrl, Roblox.Configuration.DiscordBotToken,
    Roblox.Configuration.DiscordRedirectUri ?? "", Roblox.Configuration.DiscordVerificationKey);

// Sign up asset ids
var assetIdsStart = configuration.GetSection("SignupAssetIds").GetChildren().Select(assetIdStr => long.Parse(assetIdStr.Value));
Roblox.Configuration.SignupAssetIds = assetIdsStart;
Roblox.Configuration.SignupAvatarAssetIds = configuration.GetSection("SignupAvatarAssetIds").GetChildren().Select(c => long.Parse(c.Value));
#if DEBUG
Roblox.Configuration.RobloxAppPrefix = "rbxeconsimdev:";
#endif

FeatureFlags.StartUpdateFlagTask();
Roblox.Website.Filters.StaffFilter.Configure(long.Parse(configuration.GetSection("OwnerUserId").Value));
Roblox.Website.Controllers.ThumbnailsControllerV1.StartThumbnailFixLoop();

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers().AddJsonOptions(o =>
{
    o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.JsonSerializerOptions.PropertyNamingPolicy = null;
});
builder.Services.AddSignalR();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SchemaGeneratorOptions.SchemaIdSelector = type => type.ToString();
    c.OperationFilter<SwaggerFileOperationFilter>();
});

var app = builder.Build();
app.UseRouting();

var prepareResponseForCache = (StaticFileResponseContext ctx) =>
{
    const int durationInSeconds = 86400 * 365;
    ctx.Context.Response.Headers[HeaderNames.CacheControl] = "public,max-age=" + durationInSeconds;
    ctx.Context.Response.Headers.Remove(HeaderNames.LastModified);
};

// Should be public
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Roblox.Configuration.PublicDirectory + "UnsecuredContent"),
    RequestPath = "/UnsecuredContent",
    OnPrepareResponse = prepareResponseForCache,
});

// CdnBaseUrl is empty on dev servers
if (string.IsNullOrWhiteSpace(Roblox.Configuration.CdnBaseUrl))
{
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Roblox.Configuration.ThumbnailsDirectory),
        RequestPath = "/images/thumbnails",
        OnPrepareResponse = prepareResponseForCache,
    });

    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Roblox.Configuration.GroupIconsDirectory),
        RequestPath = "/images/groups",
        OnPrepareResponse = prepareResponseForCache,
    });
}

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Roblox.Configuration.PublicDirectory + "img/"),
    RequestPath = "/img",
    OnPrepareResponse = prepareResponseForCache,
});

app.UseRobloxSessionMiddleware();
app.UseRobloxPlayerCorsMiddleware();

// CSRF FIX: Only use CSRF middleware if it is not disabled in appsettings.json
var disableCsrfStr = configuration.GetSection("DisableCsrfProtection").Value;
bool disableCsrf = disableCsrfStr != null && disableCsrfStr.ToLower() == "true";

if (!disableCsrf)
{
    app.UseRobloxCsrfMiddleware();
    Roblox.Website.Middleware.CsrfMiddleware.Configure(Guid.NewGuid().ToString() + Guid.NewGuid().ToString() + Guid.NewGuid().ToString());
}

app.UseApplicationGuardMiddleware();
Roblox.Website.Middleware.ApplicationGuardMiddleware.Configure(configuration.GetSection("Authorization").Value);

app.UseSwagger();
app.UseSwaggerUI();

app.UseMiddleware<FrontendProxyMiddleware>();
app.UseRobloxLoggingMiddleware();

app.UseExceptionHandler("/error");
CommandHandler.Configure(configuration.GetSection("Render:BaseUrl").Value, configuration.GetSection("Render:Authorization").Value);

SessionMiddleware.Configure(configuration.GetSection("Jwt:Sessions").Value);
app.UseTimerMiddleware(); // Must always be last

Task.Run(async () =>
{
    await Task.Delay(TimeSpan.FromSeconds(5));
    using var assets = Roblox.Services.ServiceProvider.GetOrCreate<AssetsService>();
    await assets.FixAssetImagesWithoutMetadata();
});

app.UseEndpoints(e =>
{
    e.MapHub<ChatHub>("/chat");
    e.MapControllers();
    e.MapRazorPages();
});

app.Run();