using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;

namespace Roblox.Website.Controllers
{
    /// <summary>
    /// Forwards a finished thumbnail from RCC to the game server.
    /// RCC's TrustCheck only allows requests to URLs that start with the BaseUrl it was built
    /// against, which is the website (port 5000) - posting straight to the game server's own port
    /// is rejected with "Trust check failed" and the image is lost. RCC posts here instead, and
    /// this hands the body on to the game server unchanged.
    /// </summary>
    [ApiController]
    [Route("/api/upload-thumbnail-v1")]
    public class ThumbnailUploadController : ControllerBase
    {
        private static readonly HttpClient httpClient = new HttpClient();

        [HttpPostBypass("api/upload-thumbnail-v1")]
        public async Task<IActionResult> Upload()
        {
            string body;
            using (var reader = new System.IO.StreamReader(Request.Body, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync();
            }

            var target = Roblox.Configuration.ThumbnailUploadUrl;
            if (string.IsNullOrWhiteSpace(target))
            {
                target = "http://127.0.0.1:5001/api/upload-thumbnail-v1";
            }

            try
            {
                var content = new StringContent(body, Encoding.UTF8, "text/plain");
                var response = await httpClient.PostAsync(target, content);
                var text = await response.Content.ReadAsStringAsync();
                return StatusCode((int)response.StatusCode, text);
            }
            catch (System.Exception e)
            {
                System.Console.Error.WriteLine("[error] could not forward thumbnail to {0}: {1}", target, e.Message);
                return StatusCode(500, "thumbnail forward failed");
            }
        }
    }
}
