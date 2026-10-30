namespace Roblox.Dto.Users;

public class DiscordAccountEntry
{
    public long userId { get; set; }
    public string discordId { get; set; } = "";
    public string discordUsername { get; set; } = "";
    public bool isGuildMember { get; set; }
    public DateTime linkedAt { get; set; }
}
