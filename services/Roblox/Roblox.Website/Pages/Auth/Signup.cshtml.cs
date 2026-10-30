using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Roblox.AbuseDetection.Report;
using Roblox.Dto.Users;
using Roblox.Exceptions;
using Roblox.Libraries.Discord;
using Roblox.Logging;
using Roblox.Models.Sessions;
using Roblox.Models.Users;
using Roblox.Services;
using Roblox.Services.App.FeatureFlags;
using Roblox.Services.Exceptions;
using Roblox.Website.Controllers;
using Roblox.Website.WebsiteModels;
using ControllerBase = Roblox.Website.Controllers.ControllerBase;

namespace Roblox.Website.Pages.Auth;

public enum SignupMethod
{
    Application = 1,
    InviteUrl
}

public class Signup : RobloxPageModel
{
    [BindProperty]
    public string username { get; set; }
    [BindProperty]
    public string password { get; set; }
    [BindProperty]
    public string? gender { get; set; }
    [BindProperty]
    public int? birthMonth { get; set; }
    [BindProperty]
    public int? birthDay { get; set; }
    [BindProperty]
    public int? birthYear { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? applicationId { get; set; }
    [BindProperty(SupportsGet = true)]
    public string? inviteId { get; set; }
    public UserInviteEntry? invite { get; set; }
    public string? inviterUsername { get; set; }
    public string? errorMessage { get; set; }
    public bool signupDisabled { get; set; }
    // Discord gate: a player must prove they are in the Bloxyn Discord server before signing up.
    public DiscordVerification? discordVerification { get; set; }
    public bool discordRequired { get; set; }
    public string discordInviteUrl => DiscordApi.inviteUrl;
    public string discordOAuthUrl =>
        "/auth/discord/oauth?returnUrl=" + Uri.EscapeDataString(Request.Path + Request.QueryString);

    /// <summary>
    /// Reads the signed Discord verification cookie. The gate is skipped when Discord is not
    /// configured so a missing client secret can never lock everyone out of signup.
    /// </summary>
    private void LoadDiscordState()
    {
        discordVerification = DiscordApi.ReadVerificationToken(Request.Cookies[DiscordApi.VerificationCookieName]);
        var gateConfigured = DiscordApi.IsConfigured;
        discordRequired = Roblox.Configuration.DiscordSignupGateEnabled && gateConfigured && discordVerification == null;
        if (Roblox.Configuration.DiscordSignupGateEnabled && !gateConfigured)
            Writer.Info(LogGroup.SignUp, "Discord signup gate is enabled but Discord is not configured - allowing signup");
    }

    private void FeatureCheck()
    {
        try
        {
            FeatureFlags.FeatureCheck(FeatureFlag.SignupEnabled);
        }
        catch (RobloxException)
        {
            errorMessage = "Signup is disabled at this time. Try again later.";
            signupDisabled = true;
        }
    }
    
    private async Task SetupInvite()
    {
        if (inviteId != null)
        {
            invite = await services.users.GetInviteById(inviteId);
            if (invite == null)
                return;
            inviterUsername = (await services.users.GetUserById(invite.authorId)).username;
            if (invite.userId != null)
            {
                errorMessage = "This invite has already been used.";
                signupDisabled = true;
            }
        }
    }
    public async Task<IActionResult> OnGet()
    {
        FeatureCheck();
        
        if (string.IsNullOrEmpty(applicationId) && string.IsNullOrEmpty(inviteId))
        {
            return new RedirectResult("/");
        }

        if (userSession != null && applicationId is {Length: <= 128 and > 1})
        {
            var alreadyApproved = await services.users.IsUserApproved(userSession.userId);
            if (!alreadyApproved)
                await services.users.SetApplicationUserIdByJoinId(applicationId, userSession.userId);
            return new RedirectResult("/");
        }

        await SetupInvite();
        LoadDiscordState();

        return new PageResult();
    }

    private bool IsGuidValid(string guid)
    {
        return Guid.TryParse(guid, out _);
    }

    private const string InvalidIdMessage = "Invalid application or invite ID. Please confirm the URL was copy and pasted correctly, then try again.";
    private const string ExpiredApplicationMessage = "For security reasons, this application has been expired. Please create a new application and try again.";
    
    public async Task<IActionResult> OnPost()
    {
        // Error messages are intentionally vague. Let's keep it that way.
        
        if (string.IsNullOrEmpty(applicationId) && string.IsNullOrEmpty(inviteId))
        {
            Writer.Info(LogGroup.SignUp, "Sign up failed, empty applicationId and inviteId");
            errorMessage = InvalidIdMessage;
            return new PageResult();
        }

        await SetupInvite();
        LoadDiscordState();

        FeatureFlags.FeatureCheck(FeatureFlag.SignupEnabled);

        if (discordRequired)
        {
            Writer.Info(LogGroup.SignUp, "Sign up failed, player has not linked a Discord account");
            errorMessage = "You must join the Bloxyn Discord server and link your Discord account before you can sign up.";
            return new PageResult();
        }

        if (discordVerification != null)
        {
            var existingDiscordAccount = await services.users.GetDiscordAccountByDiscordId(
                discordVerification.discordId.ToString());
            if (existingDiscordAccount != null)
            {
                Writer.Info(LogGroup.SignUp, "Sign up failed, discord account already used. userId={0}",
                    existingDiscordAccount.userId);
                errorMessage = "That Discord account is already linked to another account.";
                discordRequired = true;
                return new PageResult();
            }
        }
        var ip = ControllerBase.GetIP(ControllerBase.GetRequesterIpRaw(HttpContext));

        // Initial cooldown check - to prevent people spamming attempts
        if (!await services.cooldown.TryCooldownCheck($"signup:step1:" + ip, TimeSpan.FromSeconds(5)))
        {
            Writer.Info(LogGroup.SignUp, "Sign up failed, cooldown step 1");
            errorMessage = "Too many attempts. Try again in about 5 seconds.";
            return new PageResult();
        }

        var redlockKey = "";
        SignupMethod method;
        if (applicationId != null)
        {
            Writer.Info(LogGroup.SignUp, "Sign up has application id");
            FeatureFlags.FeatureCheck(FeatureFlag.ApplicationsEnabled);
            method = SignupMethod.Application;
            // validate id
            if (!IsGuidValid(applicationId))
            {
                Writer.Info(LogGroup.SignUp, "Invalid application guid");
                errorMessage = InvalidIdMessage;
                return new PageResult();
            }
            // validate app
            var redeemable = await services.users.CanRedeemApplication(applicationId);
            if (redeemable != ApplicationRedemptionFailureReason.Ok)
            {
                Writer.Info(LogGroup.SignUp, "Cannot redeem app: {0}", redeemable);
                errorMessage = redeemable == ApplicationRedemptionFailureReason.Expired ? ExpiredApplicationMessage : InvalidIdMessage;
                return new PageResult();
            }

            redlockKey = "SignUpWithApplicationId:v1:" + applicationId;
        }
        else if (inviteId != null)
        {
            Writer.Info(LogGroup.SignUp, "Sign up with invite id");
            FeatureFlags.FeatureCheck(FeatureFlag.InvitesEnabled);
            method = SignupMethod.InviteUrl;
            // validate id
            if (!IsGuidValid(inviteId))
            {
                Writer.Info(LogGroup.SignUp, "Invalid invite guid");
                errorMessage = InvalidIdMessage;
                return new PageResult();
            }
            // validate invite
            var invite = await services.users.GetInviteById(inviteId);
            if (invite == null || invite.userId != null)
            {
                Writer.Info(LogGroup.SignUp, "Invite is null or already used");
                errorMessage = InvalidIdMessage;
                return new PageResult();
            }
            // confirm author wasn't banned
            var authorInfo = await services.users.GetUserById(invite.authorId);
            if (authorInfo.accountStatus != AccountStatus.Ok)
            {
                Writer.Info(LogGroup.SignUp, "Inviter was deleted or banned");
                errorMessage = InvalidIdMessage;
                return new PageResult();
            }
            redlockKey = "SignUpWithInviteId:v1:" + inviteId;
        }
        else
        {
            errorMessage = InvalidIdMessage;
            return new PageResult();
        }

        await using var redLock = await Roblox.Services.Cache.redLock.CreateLockAsync(redlockKey, TimeSpan.FromMinutes(5));
        if (!redLock.IsAcquired)
        {
            Writer.Info(LogGroup.SignUp, "Sign up attempt with app or invite failed - redlock");
            errorMessage = "There was a recent attempt to sign up using this key. Try again in a minute.";
            return new PageResult();
        }

        // Birthday is required by the sign up form and stored on the user row (see the
        // addUserBirthday migration). It stays out of the public user API.
        if (birthMonth == null || birthDay == null || birthYear == null)
        {
            errorMessage = "Please enter your birthday.";
            return Page();
        }

        var birthdayDate = DateTime.MinValue;
        var birthdayValid = birthMonth is >= 1 and <= 12 && birthYear is >= 1900 and <= 2100 && birthDay is >= 1 and <= 31;
        if (birthdayValid)
        {
            try
            {
                birthdayDate = new DateTime(birthYear!.Value, birthMonth!.Value, birthDay!.Value);
            }
            catch (ArgumentOutOfRangeException)
            {
                birthdayValid = false;
            }
        }

        if (!birthdayValid)
        {
            errorMessage = "Please enter a valid birthday.";
            return Page();
        }

        var usernameValid = await services.users.IsUsernameValid(username);
        if (!usernameValid)
        {
            errorMessage = "Invalid Username. It must start and end with an alpha-numeric character, be between 3 and 21 characters, and contain at most one special character (space, period, or underscore). There are also some words that cannot be used in usernames.";
            return Page();
        }

        var nameAvailable = await services.users.IsNameAvailableForSignup(username);
        if (!nameAvailable)
        {
            errorMessage = "Username is already taken";
            return Page();
        }

        var passwordValid = services.users.IsPasswordValid(password);
        if (!passwordValid)
        {
            errorMessage = "Password is too simple";
            return Page();
        }

        if (!await UsersAbuse.ShouldAllowCreation(new (ip)))
        {
            errorMessage = "Registration is not available at this time. Try again in a few hours, or contact a staff member.";
            return new PageResult();
        }
        
        // Created user, so add final cooldown
        var signupFinalKey = "signup:step2:" + ip;
        if (!await services.cooldown.TryCooldownCheck(signupFinalKey, TimeSpan.FromMinutes(5)))
        {
            errorMessage = "Too many attempts. Try again in about 5 minutes.";
            return new PageResult();
        }

        // Optional gender selection from the sign up form
        var selectedGender = Gender.Unknown;
        if (!string.IsNullOrWhiteSpace(gender))
        {
            if (gender.Equals("male", StringComparison.OrdinalIgnoreCase))
                selectedGender = Gender.Male;
            else if (gender.Equals("female", StringComparison.OrdinalIgnoreCase))
                selectedGender = Gender.Female;
            else if (!gender.Equals("unknown", StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Invalid gender selection.";
                return Page();
            }
        }

        // Now make the account
        UserId createdUser;
        try
        {
            createdUser =
                await services.users.CreateUser(username, password, selectedGender, birthday: birthdayDate);
        }
        catch (Exception)
        {
            await services.cooldown.ResetCooldown(signupFinalKey);
            throw;
        }

        if (method == SignupMethod.Application)
        {
            // Application
            await services.users.SetApplicationUserIdByJoinId(applicationId!, createdUser.userId);
            Roblox.Metrics.UserMetrics.ReportUserSignUpFromApplication();
        }
        else if (method == SignupMethod.InviteUrl)
        {
            // Invite
            await services.users.SetUserInviteId(createdUser.userId, inviteId!);
            Roblox.Metrics.UserMetrics.ReportUserSignUpFromInvite();
        }

        var sess = await services.users.CreateSession(createdUser.userId);
        var sessionCookie = Roblox.Website.Middleware.SessionMiddleware.CreateJwt(new Middleware.JwtEntry()
        {
            sessionId = sess,
            createdAt = DateTimeOffset.Now.ToUnixTimeSeconds(),
        });
        // Keep the Discord link on the new account, then clear the one-shot verification cookie.
        if (discordVerification != null)
        {
            try
            {
                await services.users.LinkDiscordAccount(createdUser.userId,
                    discordVerification.discordId.ToString(), discordVerification.username, true);
            }
            catch (DiscordAlreadyLinkedException)
            {
                Writer.Info(LogGroup.SignUp, "Discord link race for userId={0}", createdUser.userId);
            }

            HttpContext.Response.Cookies.Delete(DiscordApi.VerificationCookieName, new CookieOptions { Path = "/" });
        }

        HttpContext.Response.Cookies.Append(Middleware.SessionMiddleware.CookieName, sessionCookie, new CookieOptions()
        {
            Secure = true,
            Expires = DateTimeOffset.Now.Add(TimeSpan.FromDays(364)),
            IsEssential = true,
            Path = "/",
            SameSite = SameSiteMode.Lax,
        });
        return Redirect("/home");
    }
}