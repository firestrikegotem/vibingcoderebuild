using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Roblox.Dto.Games;
using Roblox.Exceptions;
using Roblox.Models.Assets;
using Roblox.Models.Users;
using Roblox.Services.Exceptions;
using System.Linq;

namespace Roblox.Website.Controllers;

[ApiController]
[Route("/apisite/games/v1")]
public class GamesControllerV1 : ControllerBase
{
    [HttpGet("games")]
    public async Task<dynamic> MultiGetUniverseInfo(string universeIds)
    {
        var sp = universeIds.Split(",").Select(long.Parse);
        var result = await services.games.MultiGetUniverseInfo(sp);
        return new
        {
            data = result,
        };
    }

    [HttpGet("games/sorts")]
    public async Task<dynamic> GetGameSorts(string? gameSortsContext)
    {
        var sorts = new Dictionary<string, dynamic>()
        {
            { "popular", new { token = "popular", name = "popular", displayName = "popular" } },
            { "recent", new { token = "recent", name = "Recent", displayName = "Recent" } },
            { "mostFavorited", new { token = "mostFavorited", name = "Most Favorited", displayName = "Most Favorited" } },
            { "recentlyUpdated", new { token = "recentlyUpdated", name = "Recently Updated", displayName = "Recently Updated" } },
            { "recentlyCreated", new { token = "recentlyCreated", name = "Recently Created", displayName = "Recently Created" } },
        };

        var results = new List<dynamic>();
        if (gameSortsContext == "HomeSorts")
        {
            if (userSession == null)
                throw new ForbiddenException();
            var recent = await services.games.GetRecentGames(userSession.userId, 1);
            if (recent.Any())
            {
                results.Add(sorts["recent"]);
                results.Add(sorts["popular"]);
            }
        }
        else
        {
            results.Add(sorts["popular"]);
            results.Add(sorts["mostFavorited"]);
            results.Add(sorts["recentlyUpdated"]);
        }

        return new
        {
            sorts = results.Select(c => new
            {
                c.token,
                c.name,
                c.displayName,
                getSetTypeId = 0,
                gameSetTargetId = 0,
                timeOptionsAvailable = false,
                genreOptionsAvailable = false,
                numberOfRows = 1,
                numberOfGames = 0,
                isDefaultSort = true,
                contextUniverseId = (long?) null,
                contextCountryRegionId = (int?) null,
                tokenExpiryInSeconds = 86400,
            }),
        };
    }

    [HttpGet("games/list")]
    public async Task<dynamic> GetGamesList(string? sortToken, int maxRows = 10, Genre? genre = null, string? keyword = null)
    {
        if (maxRows is > 100 or < 1) maxRows = 10;
        var result = await services.games.GetGamesList(userSession?.userId, sortToken, maxRows, genre, keyword);
        return new
        {
            games = result,
        };
    }

    private static Regex numberRegex { get; } = new("([0-9]+)");
    
    [HttpGet("games/multiget-playability-status")]
    public dynamic MultiGetPlayabilityStatus()
    {
        var ids = HttpContext.Request.QueryString.Value;
        return numberRegex.Matches(ids ?? "").Select(c => long.Parse(c.Value)).Distinct().Select(c => new
        {
            playabilityStatus = "Playable",
            isPlayable = true,
            universeId = c,
        });
    }

    [HttpGet("games/{universeId:long}/social-links/list")]
    public dynamic GetSocialLinks()
    {
        return new
        {
            data = new List<int>(),
        };
    }

    [HttpGet("games/recommendations/game/{universeId:long}")]
    public async Task<dynamic> GetRecommendedGames(long universeId, int maxRows = 6)
    {
        if (maxRows is > 100 or < 1) maxRows = 10;
        var result = await services.games.GetGamesList(safeUserSession.userId, "popular", maxRows, null, null);
        return new
        {
            games = result,
        };
    }

    [HttpGet("games/multiget-place-details")]
    public async Task<IEnumerable<PlaceEntry>> MultiGetPlaceDetails(string placeIds)
    {
        return await services.games.MultiGetPlaceDetails(placeIds.Split(",").Select(long.Parse));
    }

    [HttpGet("games/votes")]
    public async Task<dynamic> GetGameVotes(string universeIds)
    {
        var ids = universeIds.Split(",").Select(long.Parse).Distinct().ToList();
        if (ids.Count is < 1 or > 100)
            throw new RobloxException(400, 0, "BadRequest");
        var uni = await services.games.MultiGetUniverseInfo(ids);

        var result = new List<dynamic>();
        foreach (var item in uni)
        {
            var votes = await services.assets.GetVoteForAsset(item.rootPlaceId);
            result.Add(new
            {
                id = item.id,
                upVotes = votes.upVotes,
                downVotes = votes.downVotes,
            });
        }

        return new
        {
            data = result,
        };
    }

    [HttpPatch("games/{universeId:long}/user-votes")]
    public async Task VoteOnUniverse(long universeId, [Required, FromBody] VoteRequest request)
    {
        var uni = (await services.games.MultiGetUniverseInfo(new[] {universeId})).FirstOrDefault();
        await services.assets.VoteOnAsset(uni!.rootPlaceId, safeUserSession.userId, request.vote);
    }

    // ==========================================
    // NEW CODE ADDED BELOW TO FIX GAME CREATION
    // ==========================================

    public class CreateGameRequest
    {
        public string name { get; set; }
        public long templatePlaceId { get; set; }
    }

    [HttpPost("games/create")]
    public async Task<dynamic> CreateGame([FromBody] CreateGameRequest request)
    {
        var userId = safeUserSession.userId;
        if (string.IsNullOrWhiteSpace(request.name)) throw new RobloxException(400, 0, "Name is required");
        var name = request.name.Trim();
        if (name.Length > 50) name = name[..50];

        if (!await services.cooldown.TryIncrementBucketCooldown("CreateGameV1_Day:" + userId, 25, TimeSpan.FromDays(1)))
            throw new RobloxException(429, 0, "You have created too many games today. Try again tomorrow.");

        // Prefer to start the new game from the template the creator picked (a baseplate or one of
        // their own games). If the template can't be copied we still create a playable baseplate place
        // instead of failing the request.
        long? placeId = null;
        if (request.templatePlaceId > 0)
            placeId = await CopyPlaceContent(userId, request.templatePlaceId, name);
        if (placeId == null)
        {
            var place = await services.assets.CreatePlace(userId, Models.Assets.CreatorType.User, userId);
            placeId = place.placeId;
        }

        var universe = await services.games.CreateUniverse(placeId.Value);

        return new
        {
            universeId = universe.universeId,
            placeId = placeId.Value,
        };
    }

    /// <summary>
    /// Copies the latest place file of an existing place into a new place owned by userId.
    /// Returns null when the template does not exist or has no file, so the caller can fall back.
    /// </summary>
    private async Task<long?> CopyPlaceContent(long userId, long templatePlaceId, string name)
    {
        try
        {
            var details = await services.assets.GetAssetCatalogInfo(templatePlaceId);
            if (details.assetType != Models.Assets.Type.Place) return null;
            var version = await services.assets.GetLatestAssetVersion(templatePlaceId);
            if (string.IsNullOrEmpty(version.contentUrl)) return null;
            await using var content = await services.assets.GetAssetContent(version.contentUrl);
            var created = await services.assets.CreateAsset(name, null, userId, Models.Assets.CreatorType.User,
                userId, content, Models.Assets.Type.Place, Models.Assets.Genre.All,
                Models.Assets.ModerationStatus.ReviewApproved);
            return created.assetId;
        }
        catch (Exception e)
        {
            Console.WriteLine("[warn] could not copy place template " + templatePlaceId + ": " + e.Message);
            return null;
        }
    }
}