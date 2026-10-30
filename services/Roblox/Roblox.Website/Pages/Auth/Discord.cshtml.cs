using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Roblox.Dto.Users;
using Roblox.Libraries.Discord;

namespace Roblox.Website.Pages.Auth;

public class Discord : RobloxPageModel
{
    public DiscordAccountEntry? discordAccount { get; set; }
    public string inviteUrl => DiscordApi.inviteUrl;
    public bool discordConfigured => DiscordApi.IsConfigured;

    public async Task<IActionResult> OnGet()
    {
        if (userSession != null)
            discordAccount = await services.users.GetDiscordAccount(userSession.userId);
        return new PageResult();
    }
}
