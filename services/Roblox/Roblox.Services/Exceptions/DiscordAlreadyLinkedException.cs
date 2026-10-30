namespace Roblox.Services.Exceptions;

public class DiscordAlreadyLinkedException : RobloxException
{
    public DiscordAlreadyLinkedException() : base(400, 0,
        "That Discord account is already linked to another account.")
    {
    }
}
