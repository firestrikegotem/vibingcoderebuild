using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Roblox.Dto.Users;
using Roblox.Website.WebsiteModels.Users;

namespace Roblox.Website.Controllers;

[ApiController]
[Route("/apisite/presence/v1")]
public class PresenceControllerV1 : ControllerBase
{
    [HttpPost("presence/register-app-presence")]
    public Task RegisterPresence()
    {
        // TODO: Impersonating: Don't update online status if requester is impersonating
        return Task.CompletedTask;
    }

    [HttpPost("presence/users")]
    public async Task<GetPresenceResponse> MultiGetOnlineStatus([Required,FromBody] PresenceRequest req)
    {
        var result = await services.users.MultiGetPresence(req.userIds);
        return new()
        {
            userPresences = result,
        };
    }
}