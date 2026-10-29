using System.ComponentModel.DataAnnotations;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc;
using Roblox.Dto.Games;
using Roblox.Exceptions;
using Roblox.Models.Assets;
using Roblox.Models.Users;
using Roblox.Services.Exceptions;
using System.Linq;
using Npgsql;

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
        return numberRegex.Matches(ids).Select(c => long.Parse(c.Value)).Distinct().Select(c => new
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
        var result = await services.games.GetGamesList(userSession.userId, "popular", maxRows, null, null);
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
        await services.assets.VoteOnAsset(uni.rootPlaceId, safeUserSession.userId, request.vote);
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
        // Use RobloxException to avoid the int/string argument error
        if (userSession == null) throw new RobloxException(401, 0, "Unauthorized");
        if (string.IsNullOrWhiteSpace(request.name)) throw new RobloxException(400, 0, "Name is required");

        // Connect directly to the database using your credentials
        var connString = "Host=127.0.0.1; Database=ecsrev; Password=1Mosher1334*; Username=postgres; Maximum Pool Size=20";
        using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // 1. Create Asset (Place)
        using var cmdAsset = new NpgsqlCommand(
            "INSERT INTO asset (name, asset_type, asset_genre, creator_type, creator_id, moderation_status) VALUES (@name, 9, 0, 1, @creatorId, 1) RETURNING id", conn);
        cmdAsset.Parameters.AddWithValue("name", request.name);
        cmdAsset.Parameters.AddWithValue("creatorId", userSession.userId);
        var placeId = (long)await cmdAsset.ExecuteScalarAsync();

        // 2. Create Asset Version
        using var cmdVer = new NpgsqlCommand(
            "INSERT INTO asset_version (asset_id, version_number, creator_id, content_url) VALUES (@assetId, 1, @creatorId, @contentUrl) ON CONFLICT DO NOTHING", conn);
        cmdVer.Parameters.AddWithValue("assetId", placeId);
        cmdVer.Parameters.AddWithValue("creatorId", userSession.userId);
        cmdVer.Parameters.AddWithValue("contentUrl", $"http://localhost:5000/asset/?id={placeId}");
        await cmdVer.ExecuteNonQueryAsync();

        // 3. Create Asset Place Settings
        using var cmdPlace = new NpgsqlCommand(
            "INSERT INTO asset_place (asset_id, vip_price) VALUES (@assetId, 0) ON CONFLICT DO NOTHING", conn);
        cmdPlace.Parameters.AddWithValue("assetId", placeId);
        await cmdPlace.ExecuteNonQueryAsync();

        // 4. Create Universe
        using var cmdUniverse = new NpgsqlCommand(
            "INSERT INTO universe (root_asset_id, creator_id, creator_type, is_public) VALUES (@placeId, @creatorId, 1, true) RETURNING id", conn);
        cmdUniverse.Parameters.AddWithValue("placeId", placeId);
        cmdUniverse.Parameters.AddWithValue("creatorId", userSession.userId);
        var universeId = (long)await cmdUniverse.ExecuteScalarAsync();

        return new
        {
            universeId = universeId,
            placeId = placeId
        };
    }
}