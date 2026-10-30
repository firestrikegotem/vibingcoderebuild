using Microsoft.AspNetCore.Mvc.RazorPages;
using Roblox.Models.Sessions;
using Roblox.Website.Controllers;

namespace Roblox.Website.Pages;

public class RobloxPageModel : PageModel
{
    protected ControllerServices services { get; } = new();
    public Roblox.Models.Sessions.UserSession? userSession
    {
        get
        {
            var dict = HttpContext.Items;
            if (dict.ContainsKey(Roblox.Website.Middleware.SessionMiddleware.CookieName))
                return (UserSession?)dict[Middleware.SessionMiddleware.CookieName];

            return null;
        }
    }

    public bool isAuthenticated => userSession != null;

    /// <summary>
    /// Same as userSession but throws "Unauthorized" instead of returning null when the visitor
    /// is not logged in. Mirrors ControllerBase.safeUserSession.
    /// </summary>
    /// <exception cref="RobloxException"></exception>
    protected UserSession safeUserSession
    {
        get
        {
            if (userSession == null)
                throw new Roblox.Services.Exceptions.RobloxException(401, 0, "Unauthorized");
            return userSession;
        }
    }
    protected string rawIpAddress => Roblox.Website.Controllers.ControllerBase.GetRequesterIpRaw(HttpContext);
    protected string hashedIp => Roblox.Website.Controllers.ControllerBase.GetIP(rawIpAddress);

    protected string GetIpHashWithSalt(string salt)
    {
        return Roblox.Website.Controllers.ControllerBase.GetIP(rawIpAddress, salt);
    }
    public string nonce { get; set; }
}