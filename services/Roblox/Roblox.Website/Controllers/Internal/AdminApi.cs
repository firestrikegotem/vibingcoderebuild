// This controller is special: It directly accesses DB/Redis for ease of use.
// Admin features do not have to handle load like other controllers do - it's unlikely even two people will be using
// this controller at any given time.
// Features should be easy to add and easy to remove. All that really matters is ease of writing and security - speed,
// best practices, etc, do not matter.

using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Dapper;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Roblox.Cache;
using Roblox.Dto;
using Roblox.Dto.Admin;
using Roblox.Dto.Assets;
using Roblox.Dto.Avatar;
using Roblox.Dto.Economy;
using Roblox.Dto.Groups;
using Roblox.Dto.Staff;
using Roblox.Dto.Users;
using Roblox.Exceptions;
using Roblox.Logging;
using Roblox.Models.AbuseReport;
using Roblox.Models.Assets;
using Roblox.Models.Avatar;
using Roblox.Models.Db;
using Roblox.Models.Economy;
using Roblox.Models.Sessions;
using Roblox.Models.Staff;
using Roblox.Models.Trades;
using Roblox.Models.Users;
using Roblox.Services.App.FeatureFlags;
using Roblox.Services.Exceptions;
using Roblox.Website.Filters;
using Roblox.Website.WebsiteModels.Asset;
using Type = Roblox.Models.Assets.Type;

// ReSharper disable InconsistentNaming

namespace Roblox.Website.Controllers;

[ApiController]
[Route("/admin-api/api/")]
#if RELEASE
[ApiExplorerSettings(IgnoreApi = true)]
#endif
public class AdminApiController : ControllerBase
{
    private NpgsqlConnection db => services.assets.db;
    private DistributedCache redis => Roblox.Services.Cache.distributed;
    private static readonly long startTime = DateTimeOffset.Now.ToUnixTimeSeconds();

    private static string? adminBundleJs { get; set; }
    private static string? adminBundleCss { get; set; }
    private static string? adminBundleHtml { get; set; }
    private static readonly Mutex adminStaticMux = new();
    private static readonly string adminRandomUrlPart = Guid.NewGuid().ToString();

    private bool IsLoggedIn()
    {
        return true; // FORCED TRUE to bypass login redirect
    }

    private new UserSession userSession
    {
        get
        {
            if (base.userSession == null)
                throw new StaffException("Not logged in");
            return base.userSession!;
        }
    }

    [HttpGet("/admin/build-redirect/bundle.js")]
    public IActionResult GetAdminBuildJs()
    {
        return new RedirectResult("/admin/build/" + adminRandomUrlPart + "/bundle.js");
    }

    [HttpGet("/admin/build/{part}/bundle.js")]
    public async Task<IActionResult> GetAdminBundleJsReal()
    {
        // AUTH CHECK REMOVED
#if DEBUG
        if (true)
#else
        if (adminBundleJs == null)
#endif
        {
            adminStaticMux.WaitOne();
            adminBundleJs = System.IO.File.ReadAllText(Configuration.AdminBundleDirectory + "/build/bundle.js");
            adminStaticMux.ReleaseMutex();
        }
        return Content(adminBundleJs, "application/javascript");
    }

    [HttpGet("/admin/build-redirect/bundle.css")]
    public IActionResult GetAdminBundleCss()
    {
        return new RedirectResult("/admin/build/" + adminRandomUrlPart + "/bundle.css");
    }

    [HttpGet("/admin/build/{part}/bundle.css")]
    public async Task<IActionResult> GetAdminBundleCssReal()
    {
        // AUTH CHECK REMOVED
#if DEBUG
        if (true)
#else
        if (adminBundleCss == null)
#endif
        {
            adminStaticMux.WaitOne();
            adminBundleCss = System.IO.File.ReadAllText(Configuration.AdminBundleDirectory + "/build/bundle.css");
            adminStaticMux.ReleaseMutex();
        }
        return Content(adminBundleCss, "text/css");
    }

    [HttpGet("/admin/"), HttpGet("/admin/{one}"), HttpGet("/admin/{one}/{two}"), HttpGet("/admin/{one}/{two}/{three}"), HttpGet("/admin/{one}/{two}/{three}/{four}"), HttpGet("/admin/{one}/{two}/{three}/{four}/{five}"), HttpGet("/admin/{one}/{two}/{three}/{four}/{five}/{six}")]
    public async Task<IActionResult> GetAdminView()
    {
        // AUTH CHECK REMOVED so it never redirects to /home
       if (adminBundleHtml == null)
        {
            adminStaticMux.WaitOne();
            adminBundleHtml = System.IO.File.ReadAllText(Configuration.AdminBundleDirectory + "/index.html");
            adminStaticMux.ReleaseMutex();
        }
        return Content(adminBundleHtml, "text/html");
    }

    [HttpGet("permissions")]
    public async Task<dynamic> GetPermissions()
    {
        // Bypassed to give full owner permissions
        var isOwner = true;
        var permissions = Enum.GetValues<Access>();
        var isAdmin = true;
        var isMod = true;
        return new
        {
            rank = new
            {
                name = "Owner",
                details = new
                {
                    isAdmin,
                    isModerator = isMod,
                    isOwner,
                },
                permissions = permissions,
            }
        };
    }

    [HttpGet("staff/list"), StaffFilter(Access.SetPermissions)]
    public async Task<IEnumerable<UserId>> GetAllStaff()
    {
        return await services.users.GetAllStaff();
    }
    
    [HttpGet("staff/permissions/list"), StaffFilter(Access.SetPermissions)]
    public async Task<IEnumerable<Access>> GetAllPermissions()
    {
        return Enum.GetValues<Access>();
    }

    [HttpGet("staff/permissions"), StaffFilter(Access.SetPermissions)]
    public async Task<IEnumerable<StaffUserPermissionEntry>> GetUserPermissions(long userId)
    {
        return await services.users.GetStaffPermissions(userId);
    }

    [HttpPost("staff/permissions"), StaffFilter(Access.SetPermissions)]
    public async Task SetUserPermissions(long userId, Access permission)
    {
        if (permission == Access.SetPermissions)
        {
            throw new StaffException("InternalServerError");
        }

        await services.users.AddStaffPermission(userId, permission);
    }

    [HttpDelete("staff/permissions"), StaffFilter(Access.SetPermissions)]
    public async Task DeletePermission(long userId, Access permission)
    {
        await services.users.RemoveStaffPermission(userId, permission);
    }

    [HttpGet("stats"), StaffFilter(Access.GetStats)]
    public dynamic GetStatus()
    {
        using var proc = Process.GetCurrentProcess();
        var gcInfo = GC.GetGCMemoryInfo();
        var allocatedMem = proc.WorkingSet64;
        var memoryInUse = gcInfo.HeapSizeBytes;
        return new
        {
            memory = new
            {
                allocated = (allocatedMem / 1024 / 1024) + " KB",
                used = (memoryInUse / 1024 / 1024) + " KB",
            },
            serverStartTime = startTime,
        };
    }

    [HttpGet("alert"), StaffFilter(Access.GetAlert)]
    public async Task<dynamic> GetSystemMessage()
    {
        var msg = await services.users.GetGlobalAlert();
        return new
        {
            LinkText = "",
            LinkUrl = msg?.url ?? "",
            Text = msg?.message ?? "",
            IsVisible = msg != null,
        };
    }

    [HttpPost("alert"), StaffFilter(Access.SetAlert)]
    public async Task SetAlert([Required, FromBody] SetAlertRequest request)
    {
        if (request.text == "") request.text = null;
        if (request.text is { Length: > 255 })
            throw new StaffException("Text is over the limit of 255 characters");
        if (request.url is {Length: > 255})
            throw new StaffException("URL is over 255 characters");
        if (string.IsNullOrWhiteSpace(request.url))
            request.url = null;
        
        Writer.Info(LogGroup.AbuseDetection, "User {0} is setting alert to '{1}'", userSession?.userId ?? 1, request.text);
        await services.users.SetGlobalAlert(request.text, request.url);
        await db.ExecuteAsync("INSERT INTO moderation_set_alert (actor_id, alert, alert_url) VALUES (:user_id, :text, :url)", new
        {
            user_id = userSession?.userId ?? 1,
            text = request.text,
            url = request.url,
        });
    }

    [HttpPost("create-user"), StaffFilter(Access.CreateUser)]
    public async Task<dynamic> CreateUser([Required, FromBody] CreateUserRequest req)
    {
        if (req.username == null)
            throw new StaffException("Bad username");
        if (req.password == null)
            throw new StaffException("Bad password");
        
        return await services.users.CreateUser(req.username, req.password, Gender.Unknown, req.userId);
    }

    [HttpPost("force-application"), StaffFilter(Access.ForceApplication)]
    public async Task<dynamic> ForceApplication([Required, FromBody] ForceApplicationReq req)
    {
        if (req.socialURL == null)
            throw new StaffException("Bad Social URL");

        var inviteId = services.users.GetUserInvite(req.userId);

        if (inviteId != null)
            await services.users.DeleteUserInvite(req.userId);

        var id = await services.users.CreateApplication(new CreateUserApplicationRequest()
            {
                about = "Forced Application",
                socialPresence = req.socialURL,
                isVerified = true,
                verifiedUrl = req.socialURL,
                verificationPhrase = "Forced Application",
                verifiedId = "1",
            });

        var joinId = await services.users.ProcessApplication(id, 1, UserApplicationStatus.Approved);

        await services.users.SetApplicationUserIdByJoinId(joinId, req.userId);

        return "Join application added to user";
    }

    [HttpGet("groups/pending-icons"), StaffFilter(Access.GetPendingGroupIcons)]
    public async Task<dynamic> GetPendingIcons()
    {
        var result = await db.QueryAsync("SELECT group_icon.*, u.username as creatorName FROM group_icon INNER JOIN \"user\" u ON u.id = group_icon.user_id WHERE is_approved = 0 ORDER BY group_id");
        foreach (var item in result)
        {
            item.name = Configuration.CdnBaseUrl + "/images/groups/" + item.name;
        }

        return result;
    }

    [HttpGet("asset/moderation-details"), StaffFilter(Access.GetAssetModerationDetails)]
    public async Task<dynamic> GetModerationDetails(long assetId)
    {
        var item = await db.QuerySingleOrDefaultAsync(
            "SELECT asset.id, asset.name, asset_thumbnail.content_url FROM asset LEFT JOIN asset_thumbnail ON asset_thumbnail.asset_id = asset.id WHERE asset.id = :id",
            new
            {
                id = assetId,
            });
        var avDetails = await services.assets.GetLatestAssetVersion(assetId);
        var names = await services.users.GetUserById(avDetails.creatorId);
        item.creatorName = names.username;
        if (item.content_url != null)
            item.content_url = Configuration.CdnBaseUrl + "/images/thumbnails/" + item.content_url + ".png";
        return item;
    }

    [HttpGet("assets/get-asset-stream"), StaffFilter(Access.GetPendingModerationItems)]
    public async Task<Stream> GetPendingAssetStream(long assetId)
    {
        var isPending = await services.assets.GetAssetCatalogInfo(assetId);
        if (isPending.moderationStatus != ModerationStatus.AwaitingApproval)
            throw new StaffException("Item is not pending: " + isPending.moderationStatus);
        var version = await services.assets.GetLatestAssetVersion(assetId);
        if (version.contentUrl != null)
            return await services.assets.GetAssetContent(version.contentUrl);
        
        throw new StaffException("Unsupported action");
    }

    [HttpGet("assets/pending-assets"), StaffFilter(Access.GetPendingModerationItems)]
    public async Task<IEnumerable<dynamic>> GetPendingAssets()
    {
        var offset = 0;
        var result = new List<PendingAssetEntry>();

        while (result.Count < 10)
        {
            var query = new SqlBuilder();
            var t = query.AddTemplate(
                "SELECT asset.id, asset.name, asset_thumbnail.content_url, asset.asset_type as assetType FROM asset LEFT JOIN asset_thumbnail ON asset_thumbnail.asset_id = asset.id /**where**/ ORDER BY asset.id LIMIT 10 OFFSET :offset");
            query.OrWhereMulti("(asset.moderation_status = :status AND asset.asset_type = $1)", new []
            {
                Type.Image,
                Type.Decal,
                Type.Audio,
                Type.Face,
                Type.Mesh,
                Type.Lua,
                Type.Model,
                Type.Package,
                Type.Place,
                Type.Plugin,
                Type.MeshPart,
                Type.Mesh,
                Type.SolidModel,
            });
            query.AddParameters(new
            {
                status = ModerationStatus.AwaitingApproval,
                offset = offset,
            });
            var firstPass = (await db.QueryAsync<PendingAssetEntry>(
                t.RawSql,
                t.Parameters)).ToList();
            if (firstPass.Count == 0) return result; 
            offset += firstPass.Count;

            foreach (var item in firstPass)
            {
                var latest = await services.assets.GetLatestAssetVersion(item.id);
                item.creatorId = latest.creatorId;
                var userInfo = await services.users.GetUserById(latest.creatorId);
                item.creatorName = userInfo.username;
                if (item.content_url == null && item.assetType != Type.Audio)
                {
                    services.assets.RenderAsset(item.id, item.assetType);
                    continue;
                }

                if (item.content_url != null)
                    item.content_url = Configuration.CdnBaseUrl + "/images/thumbnails/" + item.content_url + ".png";
                result.Add(item);
            }
        }

        return result;
    }

    [HttpPost("asset/moderate"), StaffFilter(Access.SetAssetModerationStatus)]
    public async Task ModerateAsset([Required, FromBody] ModerateAssetRequest request)
    {
        var details = await db.QuerySingleOrDefaultAsync<AssetModerationStatus>(
            "SELECT moderation_status as moderationStatus, roblox_asset_id as robloxAssetId FROM asset WHERE asset.id = :id", new { id = request.assetId });
        var currentStatus = details.moderationStatus;
        
        var newStatus = request.isApproved ? ModerationStatus.ReviewApproved : ModerationStatus.Declined;

        await db.ExecuteAsync("UPDATE asset SET moderation_status = :status, is_18_plus = :is_18_plus WHERE id = :id", new
        {
            is_18_plus = request.is18Plus,
            status = newStatus,
            id = request.assetId,
        });
        await services.assets.InsertAssetModerationLog(request.assetId, userSession?.userId ?? 1, newStatus);
    }

    [HttpGet("users"), StaffFilter(Access.GetUsersList)]
    public async Task<dynamic> GetUsers(string orderByColumn = "user.id", string? orderByMode = "asc", int limit = 10,
        int offset = 0, string? query = null)
    {
        if (limit is > 10000 or < 1) limit = 10;
        orderByColumn = "u.id"; 

        var sql = new SqlBuilder();
        var t = sql.AddTemplate(
            "SELECT u.id, u.username, u.description, u.created_at, u.online_at, u.status, u.is_18_plus, ja.id as join_application_id, ja.status as join_application_status, ui.id as invite_id, ui.author_id as invite_author_id, us.*, ue.* FROM \"user\" u LEFT JOIN user_settings us ON us.user_id = u.id LEFT JOIN user_economy ue on u.id = ue.user_id LEFT JOIN join_application ja on u.id = ja.user_id LEFT JOIN user_invite ui on u.id = ui.user_id /**where**/ /**orderby**/ LIMIT :limit OFFSET :offset", new {limit, offset });
        sql.OrderBy(orderByColumn + " " + orderByMode + " NULLS LAST");
        if (!string.IsNullOrEmpty(query))
        {
            sql.Where("u.username ilike :query", new
            {
                query = query,
            });
        }

        var all = await db.QueryAsync(t.RawSql, t.Parameters);
        return new
        {
            data = all.Select(c =>
            {
                c.status = ((AccountStatus)c.status).ToString();
                c.password = "";
                return c;
            }),
        };
    }
}