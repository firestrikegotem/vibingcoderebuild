using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Roblox.Dto.Games;
using Roblox.Exceptions;
using Roblox.Models;
using Roblox.Models.Assets;
using Roblox.Services;
using Roblox.Services.Exceptions;
using Roblox.Website.WebsiteModels.Catalog;

namespace Roblox.Website.Controllers;

[ApiController]
[Route("/apisite/develop/v1")]
public class DevelopControllerV1 : ControllerBase
{
    [HttpGet("user/is-verified-creator")]
    public dynamic IsVerifiedCreator()
    {
        return new
        {
            isVerifiedCreator = true,
        };
    }

    [HttpGet("assets/genres")]
    public RobloxCollection<Models.Assets.Genre> GetAssetGenres()
    {
        return new RobloxCollection<Models.Assets.Genre>()
        {
            data = Enum.GetValues<Models.Assets.Genre>(),
        };
    }

    [HttpGet("assets")]
    public async Task<dynamic> MultiGetAssetInfo(string assetIds)
    {
        var splitIds = assetIds.Split(",").Select(long.Parse).ToList();
        if (splitIds.Count > 100) throw new BadRequestException();
        var details = await services.assets.MultiGetAssetDeveloperDetails(splitIds);
        return new
        {
            data = details,
        };
    }

    [HttpPatch("assets/{assetId:long}")]
    public async Task UpdateAsset(long assetId, [Required, FromBody] UpdateAssetRequest request)
    {
        await services.assets.ValidatePermissions(assetId, safeUserSession.userId);

        await services.assets.UpdateAsset(assetId, request.description, request.name, request.genres.First(),
            request.isCopyingAllowed, request.enableComments);
    }

    [HttpPatch("universes/{universeId:long}/max-player-count")]
    public async Task SetMaxPlayerCount(long universeId, [Required, FromBody] SetMaxPlayerCountRequest request)
    {
        var place = await services.games.GetRootPlaceId(universeId);
        await services.assets.ValidatePermissions(place, safeUserSession.userId);
        await services.games.SetMaxPlayerCount(place, request.maxPlayers);
    }

    private const long MaxPlaceFileSizeBytes = 100L * 1024 * 1024;

    public class UploadPlaceRequest
    {
        public string? name { get; set; }
        public string? description { get; set; }
        public IFormFile? file { get; set; }
    }

    private static async Task<PlaceFileInspectionResult> InspectPlaceFile(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            1 << 16, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return await PlaceFileInspector.Inspect(stream);
    }

    [HttpPost("places/upload")]
    [RequestSizeLimit(MaxPlaceFileSizeBytes)]
    public async Task<dynamic> UploadPlace([FromForm] UploadPlaceRequest request)
    {
        return await SavePlaceFile(safeUserSession.userId, request, null);
    }

    [HttpPost("places/{placeId:long}/upload")]
    [RequestSizeLimit(MaxPlaceFileSizeBytes)]
    public async Task<dynamic> UpdatePlaceFile(long placeId, [FromForm] UploadPlaceRequest request)
    {
        return await SavePlaceFile(safeUserSession.userId, request, placeId);
    }

    /// <summary>
    /// Uploads a .rbxl file. Without a placeId it creates a brand new place (and its universe, so it
    /// shows up as a game and can be joined). With a placeId it saves a new version of that place,
    /// which is how you publish an edit.
    /// </summary>
    private async Task<dynamic> SavePlaceFile(long userId, UploadPlaceRequest request, long? placeId)
    {
        if (request.file == null || request.file.Length == 0)
            throw new RobloxException(400, 0, "Choose a place file to upload.");
        if (request.file.Length > MaxPlaceFileSizeBytes)
            throw new RobloxException(400, 0, "That place file is too large. The limit is 100 MB.");

        var name = (request.name ?? "").Trim();
        var description = string.IsNullOrWhiteSpace(request.description) ? null : request.description!.Trim();
        if (name.Length > 50) name = name[..50];

        if (placeId != null)
        {
            var details = await services.assets.GetAssetCatalogInfo(placeId.Value);
            if (details.assetType != Models.Assets.Type.Place)
                throw new RobloxException(400, 0, "That asset is not a place.");
            await services.assets.ValidatePermissions(placeId.Value, userId);
            if (name.Length == 0) name = details.name;
        }
        else
        {
            if (name.Length == 0) throw new RobloxException(400, 0, "Give your place a name.");
            if (!await services.cooldown.TryIncrementBucketCooldown("PlaceUploadV1_Hour:" + userId, 10,
                    TimeSpan.FromHours(1)))
                throw new RobloxException(429, 0, "You have uploaded too many places in the last hour. Try again later.");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "place-upload-" + Guid.NewGuid().ToString("N") + ".rbxl");
        try
        {
            await using (var target = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             1 << 16, FileOptions.Asynchronous))
            {
                await using var source = request.file.OpenReadStream();
                await source.CopyToAsync(target);
            }

            var check = await InspectPlaceFile(tempPath);
            if (!check.IsPlaceFile)
                throw new RobloxException(400, 0, check.Error ?? "That place file could not be read.");

            await using var content = new FileStream(tempPath, FileMode.Open, FileAccess.Read, FileShare.Read,
                1 << 16, FileOptions.Asynchronous);
            // The validator service is authoritative when it is running. If it is down (null) we trust
            // the local checks above instead of blocking every upload.
            var serviceResult = await services.assets.ValidatePlaceFile(content);
            if (serviceResult == false)
                throw new RobloxException(400, 0,
                    "That place file did not pass validation. Save the full place from Studio and try again.");
            content.Position = 0;

            if (placeId != null)
            {
                await services.assets.CreateAssetVersion(placeId.Value, userId, content);
                long? universeId = null;
                try
                {
                    universeId = await services.games.GetUniverseId(placeId.Value);
                }
                catch (RecordNotFoundException)
                {
                    universeId = null;
                }

                return new
                {
                    placeId = placeId.Value,
                    universeId = universeId,
                    name = name,
                    isNew = false,
                };
            }

            var created = await services.assets.CreateAsset(name, description, userId,
                Models.Assets.CreatorType.User, userId, content, Models.Assets.Type.Place,
                Models.Assets.Genre.All, Models.Assets.ModerationStatus.ReviewApproved);
            var universe = await services.games.CreateUniverse(created.assetId);
            return new
            {
                placeId = created.assetId,
                universeId = (long?) universe.universeId,
                name = name,
                isNew = true,
            };
        }
        finally
        {
            try
            {
                if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
            }
            catch (System.IO.IOException)
            {
                // ignore, the OS will clean it up
            }
        }
    }
}
