using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Encodings.Web;
using System.Web;
using System.IO;
using Microsoft.AspNetCore.Mvc;
using Roblox.Dto.Assets;
using Roblox.Exceptions;
using Roblox.Libraries.Assets;
using Roblox.Models.Assets;
using Roblox.Models.Groups;
using Roblox.Models.Staff;
using Roblox.Models.Users;
using Roblox.Services;
using Roblox.Services.App.FeatureFlags;
using Roblox.Services.Exceptions;
using Roblox.Website.Filters;
using Roblox.Website.WebsiteModels.Catalog;
using Type = System.Type;

namespace Roblox.Website.Controllers;

[ApiController]
[Route("/")]
public class WebController : ControllerBase
{
    private static ControllerServices staticServices { get; } = new();
    
    static WebController()
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try { await staticServices.gameServer.DeleteOldGameServers(); }
                catch (Exception e) { Console.WriteLine("[info] KillOldservers task failed: {0}\n{1}",e.Message,e.StackTrace); }
                await Task.Delay(TimeSpan.FromSeconds(30));
            }
        });
    }
    
    [HttpGet("thumbs/avatar.ashx")]
    public async Task<RedirectResult> GetAvatarThumbnail(long userId)
    {
        var authUser18Plus = userSession != null && await services.users.Is18Plus(userSession.userId);
        if (!authUser18Plus)
        {
            var avatar18Plus = await services.avatar.IsUserAvatar18Plus(userId);
            if (avatar18Plus) return new RedirectResult("/img/blocked.png", false);
        }
        var result = (await services.thumbnails.GetUserThumbnails(new[] {userId})).ToList();
        if (result.Count == 0) return new RedirectResult("/img/placeholder.png", false);
        var safeUrl = result[0].imageUrl ?? "/img/placeholder.png";
        return new RedirectResult(safeUrl, false);
    }
    
    [HttpGet("thumbs/avatar-headshot.ashx")]
    public async Task<RedirectResult> GetAvatarHeadShot(long userId)
    {
        var authUser18Plus = userSession != null && await services.users.Is18Plus(userSession.userId);
        if (!authUser18Plus)
        {
            var avatar18Plus = await services.avatar.IsUserAvatar18Plus(userId);
            if (avatar18Plus) return new RedirectResult("/img/blocked.png", false);
        }
        var result = (await services.thumbnails.GetUserHeadshots(new[] {userId})).ToList();
        if (result.Count == 0) return new RedirectResult("/img/placeholder.png", false);
        return new RedirectResult(result[0].imageUrl ?? "/img/placeholder.png", false);
    }

    [HttpGet("thumbs/asset.ashx")]
    public async Task<RedirectResult> GetAssetThumbnail([Required] long assetId)
    {
        var authUser18Plus = userSession != null && await services.users.Is18Plus(userSession.userId);
        if (!authUser18Plus)
        {
            var asset18Plus = await services.assets.Is18Plus(assetId);
            if (asset18Plus) return new RedirectResult("/img/blocked.png", false);
        }
        var result = (await services.thumbnails.GetAssetThumbnails(new[] {assetId})).ToList();
        if (result.Count == 0 || result[0].imageUrl == null) return new RedirectResult("/img/placeholder.png", false);
        return new RedirectResult(result[0].imageUrl ?? "/img/placeholder.png", false);
    }
    
    [HttpGet("icons/asset.ashx")]
    public async Task<RedirectResult> GetAssetIcon([Required] long assetId)
    {
        var authUser18Plus = userSession != null && await services.users.Is18Plus(userSession.userId);
        if (!authUser18Plus)
        {
            var asset18Plus = await services.assets.Is18Plus(assetId);
            if (asset18Plus) return new RedirectResult("/img/blocked.png", false);
        }
        var universe = (await services.games.MultiGetPlaceDetails(new[] {assetId})).First();
        var result = (await services.thumbnails.GetGameIcons(new[] {universe.universeId})).ToList();
        if (result.Count == 0 || result[0].imageUrl == null) return new RedirectResult("/img/placeholder.png", false);
        return new RedirectResult(result[0].imageUrl ?? "/img/placeholder.png", false);
    }

    [HttpGet("userads/redirect")]
    public async Task<IActionResult> AdRedirect(string data)
    {
        var decoded = System.Text.Encoding.UTF8.GetString(System.Convert.FromBase64String(data));
        var arr = decoded.Split("|");
        var adId = long.Parse(arr[0]);
        var ad = await services.assets.GetAdvertisementById(adId);
        if (ad.isRunning) await services.assets.IncrementAdvertisementClick(ad.id);
        switch (ad.targetType)
        {
            case UserAdvertisementTargetType.Asset:
                var itemData = await services.assets.GetAssetCatalogInfo(ad.targetId);
                var redirectUrl = "/catalog/" + itemData.id + "/" + UrlUtilities.ConvertToSeoName(itemData.name);
                return Redirect(redirectUrl);
            case UserAdvertisementTargetType.Group:
                return Redirect("/My/Groups.aspx?gid=" + ad.targetId);
            default:
                return Redirect("/home");
        }
    }

    [HttpGet("/users/favorites/list-json")]
    public async Task<dynamic> GetFavoritesLegacy(long userId, Models.Assets.Type assetTypeId, int pageNumber = 1, int itemsPerPage = 10)
    {
        if (pageNumber < 1) pageNumber = 1;
        if (itemsPerPage < 1 || itemsPerPage > 100) itemsPerPage = 10;
        var favs = await services.assets.GetFavoritesOfType(userId, assetTypeId, itemsPerPage, (itemsPerPage * pageNumber) - itemsPerPage);
        var details = (await services.assets.MultiGetInfoById(favs.Select(c => c.assetId))).ToList();
        var universeStuff = await services.games.MultiGetPlaceDetails(details.Where(c => c.assetType == Models.Assets.Type.Place).Select(c => c.id));
        return new {
            IsValid = true, Data = new {
                Page = pageNumber, ItemsPerPage = itemsPerPage, PageType = "favorites",
                Items = details.Select(c => {
                    var details = universeStuff.FirstOrDefault(x => x.placeId == c.id);
                    return new {
                        AssetRestrictionIcon = new { CssTag = c.itemRestrictions.Contains("LimitedUnique") ? "limited-unique" : c.itemRestrictions.Contains("Limited") ? "limited" : "" },
                        Item = new { AssetId = c.id, UniverseId = details?.universeId, Name = c.name, AbsoluteUrl = "/catalog/" + c.id + "/--", AssetType = (int) c.assetType, AssetCategory = 0, CurrentVersionId = 0, LastUpdated = (string?) null },
                        Creator = new { Id = c.creatorTargetId, Name = c.creatorName, Type = (int) c.creatorType, CreatorProfileLink = c.creatorType == CreatorType.Group ? "/My/Groups.aspx?gid=" + c.creatorTargetId : "/users/" + c.creatorTargetId + "/profile" },
                        Product = new { PriceInRobux = c.price, PriceInTickets = c.priceTickets, IsForSale = c.isForSale, Is18Plus = c.is18Plus, IsLimited = c.itemRestrictions.Contains("Limited"), IsLimitedUnique = c.itemRestrictions.Contains("LimitedUnique"), IsFree = c.price == 0 },
                    };
                }),
            },
        };
    }

    [HttpGet("users/inventory/list-json")]
    public async Task<dynamic> GetUserInventoryLegacy(long userId, Models.Assets.Type assetTypeId, string? cursor = "", int itemsPerPage = 10)
    {
        var count = await services.inventory.CountInventory(userId, assetTypeId);
        if (count == 0) return new { IsValid = true, Data = new { TotalItems = 0, nextPageCursor = (string?)null, previousPageCursor = (string?)null, PageType = "inventory", Items = Array.Empty<int>() } };
        int offset = !string.IsNullOrWhiteSpace(cursor) ? int.Parse(cursor) : 0;
        int limit = itemsPerPage;
        if (limit is > 100 or < 1) limit = 10;
        var canView = await services.inventory.CanViewInventory(userId, userSession?.userId ?? 0);
        if (!canView) return new { IsValid = false, Data = "User does not exist" };
        var result = (await services.inventory.GetInventory(userId, assetTypeId, "desc", limit, offset)).ToList();
        var moreAvailable = count > (offset + limit);
        return new {
            IsValid = true, Data = new {
                TotalItems = count, Start = 0, End = -1, Page = ((int) (offset / limit))+1,
                nextPageCursor = moreAvailable ? (offset + limit).ToString() : null,
                previousPageCursor = offset >= limit ? (offset - limit).ToString() : null,
                ItemsPerPage = limit, PageType = "inventory",
                Items = result.Select(c => new {
                    AssetRestrictionIcon = new { CssTag = c.isLimitedUnique ? "limited-unique" : c.isLimited ? "limited" : "" },
                    Item = new { AssetId = c.assetId, UniverseId = (long?) null, Name = c.name, AbsoluteUrl = "/item-item?id=" + c.assetId, AssetType = (int) c.assetTypeId },
                    Creator = new { Id = c.creatorId, Name = c.creatorName, Type = (int) c.creatorType, CreatorProfileLink = c.creatorType == CreatorType.User ? $"/users/{c.creatorId}/profile" : $"/My/Groups.aspx?gid={c.creatorId}" },
                    Product = new { PriceInRobux = c.originalPrice ?? 0, SerialNumber = c.serialNumber },
                    PrivateSeller = (object?) null, Thumbnail = new { }, UserItem = new { },
                }),
            },
        };
    }

    [HttpPost("users/set-builders-club")]
    public async Task SetBuildersClub(MembershipType membershipType)
    {
        if (userSession == null || !Enum.IsDefined(membershipType)) return;
        await services.users.InsertOrUpdateMembership(userSession.userId, membershipType);
    }

    [HttpPost("asset/toggle-profile")]
    public async Task<dynamic> AddAssetToProfile([Required, FromBody] AddToProfileCollectionsRequest request)
    {
        var currentCollection = (await services.inventory.GetCollections(safeUserSession.userId)).ToList();
        if (request.addToProfile)
        {
            var ownsItem = await services.users.GetUserAssets(safeUserSession.userId, request.assetId);
            if (!ownsItem.Any()) return new { isValid = false, data = new { }, error = "You do not own this item" };
            if (!currentCollection.Contains(request.assetId)) await services.inventory.SetCollections(safeUserSession.userId, currentCollection.Prepend(request.assetId).Distinct());
        }
        else
        {
            currentCollection.RemoveAll(c => c == request.assetId);
            await services.inventory.SetCollections(safeUserSession.userId, currentCollection);
        }
        return new { isValid = true, data = new { }, error = "" };
    }

    [HttpGet("users/profile/robloxcollections-json")]
    public async Task<dynamic> GetUserCollections(long userId)
    {
        var result = (await services.inventory.GetCollections(userId)).ToList();
        if (result.Count < 1)
        {
            var inventory = await services.inventory.GetInventory(userId, Models.Assets.Type.Hat, "desc", 6, 0);
            result = inventory.Take(6).Select(c => c.assetId).ToList();
        }
        var items = (await services.assets.MultiGetInfoById(result)).ToArray();
        return new {
            CollectionsItems = result.Select(id => {
                var c = items.First(i => i.id == id);
                return new {
                    Id = c.id, AssetSeoUrl = $"/item-item?id=" + c.id, Name = c.name, FormatName = (string?) null,
                    Thumbnail = new { Final = true, Url = $"/thumbs/asset.ashx?assetId={c.id}&width=420&height=420&format=png", Id = c.id },
                    AssetRestrictionIcon = new { TooltipText = (string?) null, CssTag = c.itemRestrictions.Contains("Limited") ? "limited" : c.itemRestrictions.Contains("LimitedUnique") ? "limited-unique" : null, LoadAssetRestrictionIconCss = false, HasTooltip = false },
                };
            }),
        };
    }

    [HttpGet("comments/get-json")]
    public async Task<dynamic> GetAssetComments(long assetId, int startIndex)
    {
        FeatureFlags.FeatureCheck(FeatureFlag.AssetCommentsEnabled);
        var details = (await services.assets.MultiGetAssetDeveloperDetails(new []{assetId})).First();
        if (!details.enableComments) return new { IsUserModerator = false, Comments = new List<dynamic>(), MaxRows = 10, AreCommentsDisabled = true };
        var com = await services.assets.GetComments(assetId, startIndex, 10);
        var isModerator = userSession != null && (await services.users.GetStaffPermissions(userSession.userId)).Any(a => a.permission == Access.DeleteComment);
        return new {
            IsUserModerator = isModerator, MaxRows = 10, AreCommentsDisabled = false,
            Comments = com.Select(c => new {
                Id = c.id, PostedDate = c.createdAt.ToString("MMM").Replace(".", "") + c.createdAt.ToString(" dd, yyyy | h:mm ") + c.createdAt.ToString("tt").ToUpper().Replace(".", ""),
                AuthorName = c.username, AuthorId = c.userId, Text = c.comment, ShowAuthorOwnsAsset = false,
                AuthorThumbnail = new { AssetId = 0, AssetHash = (string?) null, AssetTypeId = 0, Url = "/Thumbs/avatar.ashx?userId=" + c.userId, IsFinal = true },
            })
        };
    }

    [HttpPost("comments/post")]
    public async Task<dynamic> AddComment([Required, FromBody] AddCommentRequest request)
    {
        FeatureFlags.FeatureCheck(FeatureFlag.AssetCommentsEnabled);
        try { await services.assets.AddComment(request.assetId, safeUserSession.userId, request.text); return new { ErrorCode = (string?)null }; }
        catch (ArgumentException e) { return new { ErrorCode = e.Message }; }
    }

    [HttpGet("game/get-join-script")]
    public async Task<dynamic> GetJoinScript(long placeId)
    {
        var placeInfo = await services.assets.GetAssetCatalogInfo(placeId);
        if (placeInfo.assetType != Models.Assets.Type.Place) throw new BadRequestException();
        var modInfo = (await services.assets.MultiGetAssetDeveloperDetails(new[] {placeId})).First();
        if (modInfo.moderationStatus != ModerationStatus.ReviewApproved) throw new BadRequestException();
        var ipAddress = GetIP();
        var ticket = services.gameServer.CreateTicket(safeUserSession.userId, placeId, ipAddress);
        var encodedTicket = HttpUtility.UrlEncode(ticket);
        var args = $"--authenticationUrl {Roblox.Configuration.BaseUrl}/Login/Negotiate.ashx --authenticationTicket {ticket} --joinScriptUrl {Configuration.BaseUrl}/placelauncher.ashx?ticket={encodedTicket}";
        return new { joinScriptUrl = Configuration.BaseUrl + "/placelauncher.ashx?ticket=" + encodedTicket, retroArgs = args };
    }

    [HttpGet("usercheck/show-tos")]
    public dynamic GetIsTosCheckRequired() => new { success = true };

    [HttpGet("games/getgameinstancesjson")]
    public async Task<dynamic> GetGameServers(long placeId, int startIndex)
    {
        var limit = 10;
        var offset = startIndex;
        var servers = (await services.gameServer.GetGameServers(placeId, offset, limit)).ToList();
        var details = (await services.games.MultiGetPlaceDetails(new []{placeId})).First();
        return new {
            PlaceId = placeId, ShowShutdownAllButton = false,
            Collection = servers.Select(c => {
                var players = c.players.ToList();
                return new {
                    Capacity = details.maxPlayerCount, Ping = 100, Fps = 59.95, ShowSlowGameMessage = false, UserCanJoin = true, ShowShutdownButton = false, JoinScript = (string?) null, FriendsMouseover = "", FriendsDescription = "", PlayersCapacity = $"{players.Count} of {details.maxPlayerCount}", RobloxAppJoinScript = "",
                    CurrentPlayers = players.Select(p => new { Id = p.userId, Username = p.username, Thumbnail = new { IsFinal = true, Url = "/Thumbs/Avatar-Headshot.ashx?userid=" + p.userId } }),
                };
            }),
            TotalCollectionSize = servers.Count,
        };
    }

    [HttpGet("search/users/results")]
    public async Task<dynamic> SearchUsersJson(string? keyword = null, int offset = 0, int limit = 10)
    {
        if (limit is > 100 or < 1) limit = 10;
        if ((offset / limit) > 1000) offset = 0;
        var result = (await services.users.SearchUsers(keyword, limit, offset)).ToArray();
        if (result.Length == 0) return new { Keyword = keyword, StartIndex = offset, MaxRows = limit, TotalResults = 0, UserSearchResults = Array.Empty<int>() };
        var userInfo = await services.users.MultiGetUsersById(result.Skip(offset).Take(limit).Select(c => c.userId));
        return new {
            Keyword = keyword, StartIndex = offset, MaxRows = limit, TotalResults = result.Length,
            UserSearchResults = userInfo.Select(c => new { UserId = c.id, Name = c.name, DisplayName = c.displayName, Blurb = "", PreviousUserNamesCsv = "", IsOnline = false, LastLocation = (string?) null, UserProfilePageUrl = "/users/" + c.id + "/profile", LastSeenDate = (string?) null, PrimaryGroup = "", PrimaryGroupUrl = "" }),
        };
    }

    private static readonly Models.Assets.Type[] AccessoryTypes =
    {
        Models.Assets.Type.Hat,
        Models.Assets.Type.HairAccessory,
        Models.Assets.Type.FaceAccessory,
        Models.Assets.Type.NeckAccessory,
        Models.Assets.Type.ShoulderAccessory,
        Models.Assets.Type.FrontAccessory,
        Models.Assets.Type.BackAccessory,
        Models.Assets.Type.WaistAccessory,
        Models.Assets.Type.Face,
    };

    /// <summary>
    /// Types a regular player may create through Develop. Accessories (hats, hair, faces and every
    /// accessory slot) are intentionally absent: those are .rbxm models and staff-only, created from
    /// the admin panel so catalog content stays curated.
    /// </summary>
    private static readonly List<Models.Assets.Type> AllowedAssetTypes = new List<Models.Assets.Type>
    {
        Models.Assets.Type.Audio, Models.Assets.Type.TeeShirt, Models.Assets.Type.Shirt, Models.Assets.Type.Pants,
        Models.Assets.Type.Image,
    };
    private static int pendingAssetUploads { get; set; } = 0;
    private static readonly Mutex pendingAssetUploadsMux = new();

    [HttpPost("develop/upload-version")]
    public async Task UploadVersion([Required, FromForm] UploadAssetVersionRequest request)
    {
        var info = await services.assets.GetAssetCatalogInfo(request.assetId);
        var canUpload = await services.assets.CanUserModifyItem(info.id, safeUserSession.userId);
        if (info.assetType != Models.Assets.Type.Place) canUpload = false;
        if (canUpload == false) throw new RobloxException(403, 0, "Unauthorized");
        lock (pendingAssetUploadsMux) { if (pendingAssetUploads >= 2) throw new RobloxException(429, 0, "TooManyRequests"); pendingAssetUploads++; }
        try {
            var fs = request.file.OpenReadStream();
            if (!await services.assets.ValidateAssetFile(fs, Models.Assets.Type.Place)) throw new RobloxException(400, 0, "The asset file doesn't look correct. Please try again.");
            fs.Position = 0;
            await services.assets.CreateAssetVersion(request.assetId, safeUserSession.userId, fs);
            services.assets.RenderAsset(request.assetId, info.assetType);
        } finally { lock (pendingAssetUploadsMux) { pendingAssetUploads--; } }
    }
    
    /// <summary>
    /// Locate an XML template by name, ignoring case. The repo ships lowercase template files and
    /// this may run on a case-sensitive filesystem.
    /// </summary>
    private static string? FindXmlTemplate(string templateName)
    {
        var dir = Roblox.Configuration.XmlTemplatesDirectory;
        if (string.IsNullOrWhiteSpace(dir) || !System.IO.Directory.Exists(dir)) return null;
        var expected = templateName + ".xml";
        foreach (var file in System.IO.Directory.GetFiles(dir))
        {
            if (string.Equals(System.IO.Path.GetFileName(file), expected, StringComparison.OrdinalIgnoreCase))
                return file;
        }
        return null;
    }

    /// <summary>
    /// A Roblox model is either the binary format (starts with "&lt;roblox!") or the XML one (starts
    /// with "&lt;roblox"). Used only when the validation service cannot be reached.
    /// </summary>
    private static bool LooksLikeModelFile(byte[] fileBytes)
    {
        if (fileBytes.Length < 8) return false;
        var head = System.Text.Encoding.ASCII.GetString(fileBytes, 0, 8);
        return head.StartsWith("<roblox!", StringComparison.Ordinal) ||
               head.StartsWith("<roblox ", StringComparison.Ordinal) ||
               head.StartsWith("<roblox?", StringComparison.Ordinal);
    }

    [HttpPost("develop/upload")]
    public async Task<CreateResponse> UploadItem([Required, FromForm] UploadAssetRequest request)
    {
        FeatureFlags.FeatureCheck(FeatureFlag.UploadContentEnabled);
        if (userSession == null) throw new BadRequestException();

        // Hats, hair, faces and every accessory slot are staff-only. They are model (.rbxm) uploads and
        // are created from the admin panel, so tell players that plainly instead of a generic bad request.
        if (AccessoryTypes.Contains(request.assetType))
            throw new RobloxException(403, 0,
                "Hats, hair, faces and accessories can only be created by Bloxyn staff.");

        if (!AllowedAssetTypes.Contains(request.assetType)) throw new BadRequestException();

        var isClothing = request.assetType is Models.Assets.Type.Shirt or Models.Assets.Type.Pants or Models.Assets.Type.TeeShirt;
        var isAudio = request.assetType is Models.Assets.Type.Audio;
        var isImage = request.assetType is Models.Assets.Type.Image;

        if (!isClothing && !isAudio && !isImage) throw new RobloxException(400, 0, "Endpoint does not support this assetType: " + request.assetType);
        
        var groupId = request.groupId == null ? 0 : request.groupId.Value;
        var creatorType = groupId == 0 ? CreatorType.User : CreatorType.Group;
        var creatorId = creatorType == CreatorType.User ? userSession.userId : groupId;
        
        if (creatorType == CreatorType.Group) {
            var hasPermission = await services.groups.DoesUserHavePermission(userSession.userId, groupId, GroupPermission.CreateItems);
            if (!hasPermission) throw new RobloxException(401, 0, "Unauthorized");
        }
        
        lock (pendingAssetUploadsMux) { if (pendingAssetUploads >= 5) throw new RobloxException(409, 0, "There are too many pending assets at this time. Try again in a few minutes."); pendingAssetUploads++; }

        try
        {
            if (isClothing)
            {
                // 1. Read the upload into a byte[] up-front. The stream gets consumed by validation,
                //    hashing and the DB writes, so keep one copy in memory.
                byte[] fileBytes;
                using (var ms = new MemoryStream())
                {
                    await request.file.CopyToAsync(ms);
                    fileBytes = ms.ToArray();
                }

                using var stream = new MemoryStream(fileBytes);
                var pictureData = await services.assets.ValidateClothing(stream, request.assetType);
                if (pictureData == null) throw new BadRequestException(0, "Invalid image file");

                stream.Position = 0;
                string imageHash = await services.assets.GenerateImageHash(stream);

                // 2. Clothing uploads have to land in the folder the website serves /asset/?id= from,
                //    which is Directories:Asset. Anything else and RCC fails with "Temp read failed".
                var assetDir = (Roblox.Configuration.AssetDirectory ?? "").TrimEnd('\\', '/');
                if (string.IsNullOrWhiteSpace(assetDir))
                    throw new RobloxException(500, 0, "The asset directory is not configured. Set Directories:Asset in appsettings.json.");

                if (!System.IO.Directory.Exists(assetDir))
                    System.IO.Directory.CreateDirectory(assetDir);

                // 3. Save the physical image. The filename is the SHA256 of the bytes, which is the same
                //    key UploadAssetContent() uses, so the site can serve it with no extra work.
                var imagePath = System.IO.Path.Combine(assetDir, imageHash);
                await System.IO.File.WriteAllBytesAsync(imagePath, fileBytes);
                Console.WriteLine($"[info] saved clothing image {imagePath} ({fileBytes.Length} bytes)");

                // 4. Image asset DB entry
                stream.Position = 0;
                var imageAsset = await services.assets.CreateAsset(request.file.FileName, request.assetType + " Image",
                    userSession.userId, creatorType, creatorId, stream, Models.Assets.Type.Image,
                    Genre.All, ModerationStatus.ReviewApproved);

                await services.assets.InsertOrUpdateAssetVersionMetadataImage(imageAsset.assetVersionId, fileBytes.Length,
                    pictureData.width, pictureData.height, pictureData.imageFormat, imageHash);

                // 5. Clothing asset DB entry. contentId points at the image asset above.
                var asset = await services.assets.CreateAsset(request.name, null, userSession.userId, creatorType, creatorId,
                    null, request.assetType, Genre.All, imageAsset.moderationStatus, default, default, default, default, imageAsset.assetId);

                // 6. Save the clothing XML next to the image, built from the matching template.
                try
                {
                    var templateName = request.assetType switch
                    {
                        Models.Assets.Type.Shirt => "shirt",
                        Models.Assets.Type.TeeShirt => "teeshirt",
                        _ => "pants",
                    };

                    var templatePath = FindXmlTemplate(templateName);
                    if (templatePath == null)
                    {
                        Console.WriteLine($"[warning] XML template {templateName}.xml was not found in \"{Roblox.Configuration.XmlTemplatesDirectory}\"");
                    }
                    else
                    {
                        var xml = await System.IO.File.ReadAllTextAsync(templatePath);
                        xml = xml.Replace("{assetId}", imageAsset.assetId.ToString());
                        // The templates write their texture url as {baseUrl}/asset/?id={assetId}, and
                        // leaving {baseUrl} in place made the saved file point nowhere.
                        xml = xml.Replace("{baseUrl}", Roblox.Configuration.RccSafeBaseUrl);
                        // RCC's TrustCheck rejects "localhost", so the texture URL must use the loopback IP.
                        xml = xml.Replace("//localhost", "//127.0.0.1");

                        var xmlPath = System.IO.Path.Combine(assetDir, asset.assetId.ToString());
                        await System.IO.File.WriteAllTextAsync(xmlPath, xml);
                        Console.WriteLine($"[info] saved clothing xml {xmlPath}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[error] failed to write clothing xml: {ex.Message}");
                }

                await services.users.CreateUserAsset(userSession.userId, asset.assetId);

                // The catalog only lists items that are for sale, so put uploads on the market for free.
                await services.assets.SetItemPrice(asset.assetId, 0, null);
                await services.assets.UpdateAssetMarketInfo(asset.assetId, true, false, false, null, null);

                // Give the item its catalog picture now. Without this the shop shows an hourglass
                // until the five-minute repair loop gets to it, which looks like a broken upload
                // even when everything worked. A thumbnail problem must never fail the upload
                // itself, so it is caught and logged.
                try
                {
                    await services.assets.RenderAssetAsync(asset.assetId, request.assetType);
                }
                catch (Exception thumbnailError)
                {
                    Console.WriteLine($"[warning] could not draw a thumbnail for asset {asset.assetId}: {thumbnailError.Message}");
                }

                return asset;
            }
            else if (isImage)
            {
                var stream = request.file.OpenReadStream();
                var pictureData = await services.assets.ValidateImage(stream);
                if (pictureData == null) throw new BadRequestException(0, "Invalid image file");
                stream.Position = 0;
                var imageAsset = await services.assets.CreateAsset(request.name, "Image", userSession.userId, creatorType, creatorId, stream, Models.Assets.Type.Image, Genre.All, ModerationStatus.ReviewApproved);
                stream.Position = 0;
                await services.assets.InsertOrUpdateAssetVersionMetadataImage(imageAsset.assetVersionId, (int)stream.Length, pictureData.width, pictureData.height, pictureData.imageFormat, await services.assets.GenerateImageHash(stream));
                return imageAsset;
            }
            else if (isAudio)
            {
                var balance = await services.economy.GetBalance(creatorType, creatorId);
                if (balance.robux < 350) throw new BadRequestException(0, "Not enough Robux for purchase");
                var stream = request.file.OpenReadStream();
                var ok = await services.assets.IsAudioValid(stream);
                if (ok != AudioValidation.Ok) throw new BadRequestException(0, "Bad audio file. Error = " + ok.ToString());
                await services.economy.ChargeForAudioUpload(creatorType, creatorId);
                stream.Position = 0;
                var asset = await services.assets.CreateAsset(request.name, null, userSession.userId, CreatorType.User, userSession.userId, stream, Models.Assets.Type.Audio, Genre.All, ModerationStatus.ReviewApproved);
                return asset;
            }
            throw new BadRequestException(0, "Invalid assetType");
        }
        finally
        {
            lock (pendingAssetUploadsMux) { pendingAssetUploads--; }
        }
    }
}