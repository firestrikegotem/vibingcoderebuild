using Roblox.Dto.Users;
using Roblox.Libraries.RobloxApi;
using Roblox.Logging;

namespace Roblox.Services;

public class ApplicationProcessorService : ServiceBase, IService
// autoaccept
{
    public async Task AttemptBackgroundApplicationProcess(UserApplicationEntry entry, AppSocialMedia media)
    {
        if (entry == null || media == null || string.IsNullOrWhiteSpace(entry.id))
            return;
        
        // FORCE ACCEPT EVERYTHING
        using var us = ServiceProvider.GetOrCreate<UsersService>();
        await us.AcquireApplicationLocks(1, new[] {entry.id});
        
        Writer.Info(LogGroup.AbuseDetection, "application {0} is being force-accepted", entry.id);
        await us.ProcessApplication(entry.id, 1, UserApplicationStatus.Approved);
    }
    
    public bool IsThreadSafe()
    {
        return false;
    }

    public bool IsReusable()
    {
        return false;
    }
}