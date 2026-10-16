using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TruckMate.Services;

namespace TruckMate.Controllers;

[ApiController]
[Route("api/weight")]
public class WeightApiController : ControllerBase
{
    private readonly WeightCache _cache;
    private readonly IOptions<ScaleOptions> _opt;

    public WeightApiController(WeightCache cache, IOptions<ScaleOptions> opt)
    {
        _cache = cache;
        _opt = opt;
    }

    public record WeightPost(string ScaleId, decimal WeightKg, bool Stable, DateTime Timestamp);

    // Called by the edge agent. It cannot log in with a cookie, so the API key is checked instead.
    [AllowAnonymous]
    [HttpPost]
    public IActionResult Post([FromBody] WeightPost body)
    {
        var o = _opt.Value;
        if (!KeyMatches(Request.Headers["X-Api-Key"].ToString(), o.AgentApiKey))
            return Unauthorized();

        if (string.IsNullOrWhiteSpace(body.ScaleId) || Math.Abs(body.WeightKg) > o.MaxWeightKg)
            return BadRequest();

        _cache.Update(new WeightReading(body.ScaleId.Trim(), body.WeightKg, body.Stable, body.Timestamp));
        return NoContent();
    }

    // Constant-time comparison. Fails closed: no key configured = nobody gets in.
    private static bool KeyMatches(string supplied, string expected)
    {
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(supplied)) return false;
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected));
    }
}
