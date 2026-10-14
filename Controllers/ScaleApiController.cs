using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TruckMate.Services;

namespace TruckMate.Controllers;

[Authorize]
[Route("api/scale")]
public class ScaleApiController : Controller
{
    private readonly IScaleService _scale;

    public ScaleApiController(IScaleService scale) => _scale = scale;

    [HttpGet("snapshot")]
    public IActionResult Snapshot() => Json(_scale.GetSnapshot());

    public class SimulateRequest
    {
        public decimal? Weight { get; set; }
        public bool? Online { get; set; }
    }

    [HttpPost("simulate")]
    [ValidateAntiForgeryToken]
    public IActionResult Simulate([FromForm] SimulateRequest req)
    {
        if (req.Online.HasValue) _scale.SetOnline(req.Online.Value);
        _scale.SimulateWeight(req.Weight);
        return Json(_scale.GetSnapshot());
    }

    [HttpPost("hardware")]
    [ValidateAntiForgeryToken]
    public IActionResult Hardware([FromForm] string device, [FromForm] bool ready)
    {
        _scale.SetHardware(device, ready);
        return Json(_scale.GetSnapshot());
    }
}
