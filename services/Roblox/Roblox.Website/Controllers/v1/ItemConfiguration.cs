using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Roblox.Dto.Assets;
using Roblox.Exceptions;
using Roblox.Models;
using Roblox.Models.Assets;
using Roblox.Services.Exceptions;
using Roblox.Website.WebsiteModels.Catalog;
using Type = Roblox.Models.Assets.Type;

namespace Roblox.Website.Controllers;

[ApiController]
[Route("/apisite/itemconfiguration/v1")]
public class ItemConfigurationV1 : ControllerBase
{
    [HttpGet("metadata")]
    public dynamic GetMetadata()
    {
        return new
        {
            allowedAssetTypeForUpload = (object?)null,
            allowedAssetTypesForRelease = (object?)null,
            allowedAssetTypesForFree = new List<string>(){"Plugin"},
            allowedAssetTypesForSaleAvailabilityLocations = new List<string>()
            {
                "Hat",
                "HairAccessory",
                "FaceAccessory",
                "NeckAccessory",
                "ShoulderAccessory",
                "FrontAccessory",
                "BackAccessory",
                "WaistAccessory",
            },
        };
    }

    [HttpPost("creations/get-asset-details")]
    public async Task<dynamic> MultiGetAssetDetails([Required, FromBody] MultiGetAssetDetailsRequest request)
    {
        return await services.assets.MultiGetAssetDeveloperDetails(request.assetIds);
    }

    [HttpGet("assets/{assetId:long}/get-selling-fee")]
    public dynamic GetSellingFee()
    {
        return new
        {
            price = 0,
        };
    }

    [HttpPost("assets/{assetId:long}/update")]
    public Task UpdateAssetNoOp(long assetId)
    {
        return Task.CompletedTask;
    }

    private readonly Models.Assets.Type[] sellableAssetTypes = {
        Type.Shirt,
        Type.Pants,
        Type.TeeShirt,
    };

    private readonly Type[] freeAssetTypes =
    {
        Type.Animation,
        Type.Image,
        Type.Decal,
        Type.Image,
        Type.Mesh,
        Type.Model,
        Type.SolidModel,
    };

    [HttpPost("assets/{assetId:long}/update-price")]
    public async Task UpdateAssetPrice(long assetId, [Required,FromBody] UpdateAssetPriceRequest request)
    {
        
        if (request.priceInRobux != 0 && (request.priceInRobux is > 1000000 or < 2))
        {
            throw new BadRequestException(0, "Bad robux price");
        }

        if (request.priceInTickets is 0)
        {
            request.priceInTickets = null;
        }

        if (request.priceInTickets is > 1000000 or < 2)
        {
            throw new BadRequestException(0, "Bad ticket price");
        }
        
        var details = await services.assets.GetAssetCatalogInfo(assetId);
        if (!sellableAssetTypes.Contains(details.assetType))
        {
            if (!freeAssetTypes.Contains(details.assetType))
            {
                throw new BadRequestException(0, "Bad asset type");
            }
            if (request.priceInRobux != null)
                request.priceInRobux = 0;
            request.priceInTickets = null;
        }

        // Both null mean not for sale
        var isForSale = request.priceInTickets != null || request.priceInRobux != null;

        await services.assets.ValidatePermissions(assetId, safeUserSession.userId);
        await services.assets.UpdateAssetMarketInfo(assetId, isForSale, false, false, null, null);
        await services.assets.SetItemPrice(assetId, request.priceInRobux, request.priceInTickets);
    }

    [HttpPost("assets/{assetId:long}/release")]
    public async Task ReleaseAsset(long assetId)
    {
        await services.assets.ValidatePermissions(assetId, safeUserSession.userId);
        // Releasing an asset publishes it to the catalog. The item stays purchasable at its
        // current price (a price of 0 means it is free, which is valid).
        var details = await services.assets.GetAssetCatalogInfo(assetId);
        if (details.moderationStatus != Models.Assets.ModerationStatus.ReviewApproved)
            throw new RobloxException(400, 0, "This asset must be approved by moderation before it can be released.");
        await services.assets.UpdateItemIsForSale(assetId, true);
    }

    [HttpGet("creations/get-assets")]
    public async Task<RobloxCollectionPaginated<CreationEntry>> GetUserCreations(Models.Assets.Type assetType, int limit = 10, string? cursor = null, long? groupId = null)
    {
        var offset = int.Parse(cursor ?? "0");
        if (limit is > 100 or < 1) limit = 10;
        List<CreationEntry> result;
        if (groupId == null || groupId.Value == 0)
        {
            result = (await services.assets.GetCreations(CreatorType.User, safeUserSession.userId, assetType, offset, limit)).ToList();
        }
        else
        {
            result = (await services.assets.GetCreations(CreatorType.Group, groupId.Value, assetType, offset, limit)).ToList();
        }
        return new()
        {
            nextPageCursor = result.Count >= limit ? (offset + limit).ToString() : null,
            previousPageCursor = offset >= limit ? (offset - limit).ToString() : null,
            data = result,
        };
    }

    [HttpGet("item-tags/metadata")]
    public dynamic GetItemTagsMetadata()
    {
        return new
        {
            isItemTagsFeatureEnabled = false,
            enabledAssetTypes = new List<string>()
            {
                "Hat", "HairAccessory", "FaceAccessory", "NeckAccessory", "ShoulderAccessory", "BackAccessory",
                "FrotnAccessory", "WaistAccessory",
            },
            maximumItemTagsPerItem = 5,
        };
    }

    // todo: figure out what these are... (some sort of searching metadata?)
    [HttpGet("item-tags")]
    public dynamic GetTagsNoOp(string ItemIds)
    {
        // ItemIds is csv of "AssetId:21070012"
        return new
        {
            data = new List<int>(),
        };
    }
}