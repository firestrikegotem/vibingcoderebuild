using System.Net;
using System.Text.Json;
using Roblox.Logging;

namespace Roblox.Libraries.Captcha;

internal class HCaptchaJsonResponse
{
    public bool success { get; set; }
}

public class HCaptcha
{
    private static HttpClient client { get; } = new();
    /// <summary>
    /// hCaptcha's public documented test pair. It renders a working widget but is not a real
    /// account secret, so treating it as "configured" leaves a fresh deployment unable to log in
    /// until the operator replaces it. We treat the test pair the same as empty and warn instead.
    /// </summary>
    private const string TestSiteKey = "10000000-ffff-ffff-ffff-000000000001";
    private const string TestSecretKey = "0x0000000000000000000000000000000000000000";

    private static bool usingTestKeys =>
        Roblox.Configuration.HCaptchaPublicKey == TestSiteKey ||
        Roblox.Configuration.HCaptchaPrivateKey == TestSecretKey;

    public static bool IsEnabled =>
        !string.IsNullOrEmpty(Roblox.Configuration.HCaptchaPublicKey) &&
        !string.IsNullOrEmpty(Roblox.Configuration.HCaptchaPrivateKey) &&
        !usingTestKeys;

    /// <summary>
    /// True when the widget should be rendered at all.
    /// </summary>
    public static bool ShouldRenderWidget => IsEnabled;

    public static void LogConfigurationState()
    {
        if (IsEnabled)
            return;
        if (usingTestKeys)
            Writer.Info(LogGroup.Captcha, "hCaptcha is using the public test keys, so captcha checks are skipped. Replace HCaptcha:Public and HCaptcha:Private with your own keys to enforce captcha.");
        else
            Writer.Info(LogGroup.Captcha, "hCaptcha is not configured, so captcha checks are skipped. Set HCaptcha:Public and HCaptcha:Private to enforce captcha.");
    }

    public static async Task<bool> IsValid(string rawIpAddress, string captchaResponse)
    {
        if (!IsEnabled)
        {
            Writer.Info(LogGroup.Captcha, "hCaptcha is not configured (HCaptcha:Public / HCaptcha:Private are empty), skipping verification");
            return true;
        }

        try
        {
            var cont = new FormUrlEncodedContent(new Dictionary<string,string>
            {
                {"response", captchaResponse},
                {"secret", Roblox.Configuration.HCaptchaPrivateKey},
                // {"remoteip", rawIpAddress}, // Adding the IP causes all captcha to fail. It's also probably not great for privacy either. Makes it easier to bot though, since you wouldn't need a proxy you can share with your captcha solvers...
                {"sitekey", Roblox.Configuration.HCaptchaPublicKey},
            });

            var result = await client.PostAsync("https://hcaptcha.com/siteverify", cont);
            var str = await result.Content.ReadAsStringAsync();
            if (result.StatusCode != HttpStatusCode.OK)
            {
                Writer.Info(LogGroup.Captcha, "Unsuccessful captcha - Bad Status. Status = {0} Full body = {1}", result.StatusCode, str);
                return false; // todo: should probably log this
            }
            
            var decoded = JsonSerializer.Deserialize<HCaptchaJsonResponse>(str);
            if (decoded is not {success: true})
            {
                Writer.Info(LogGroup.Captcha, "Unsuccessful captcha. Full body = {0}", str);
                return false;
            }

            return true;
        }
        catch (Exception e)
        {
            Writer.Info(LogGroup.Captcha, "Captcha failure {0}", e.Message);
            // todo: should probably log
            return false;
        }
    }
}
