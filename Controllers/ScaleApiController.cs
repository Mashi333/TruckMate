using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TruckMate.Services;

namespace TruckMate.Controllers;

[Authorize]
[Route("api/scale")]
public class ScaleApiController : Controller
{
    private readonly IScaleService _scale;
    private readonly IWebHostEnvironment _env;

    public ScaleApiController(IScaleService scale, IWebHostEnvironment env)
    {
        _scale = scale;
        _env = env;
    }

    [HttpGet("snapshot")]
    public IActionResult Snapshot() => Json(_scale.GetSnapshot());

    public class SimulateRequest
    {
        public decimal? Weight { get; set; }
        public bool? Online { get; set; }
    }

    // Null outside Development, or when the real scale service is in use.
    private IScaleSimulator? Simulator =>
        _env.IsDevelopment() ? HttpContext.RequestServices.GetService<IScaleSimulator>() : null;

    [HttpPost("simulate")]
    [ValidateAntiForgeryToken]
    public IActionResult Simulate([FromForm] SimulateRequest req)
    {
        var sim = Simulator;
        if (sim is null) return NotFound();

        if (req.Online.HasValue) sim.SetOnline(req.Online.Value);
        // Only change the weight when one was sent, so Online/Offline no longer clears it.
        if (req.Weight.HasValue || !req.Online.HasValue) sim.SimulateWeight(req.Weight);

        return Json(_scale.GetSnapshot());
    }

    [HttpPost("hardware")]
    [ValidateAntiForgeryToken]
    public IActionResult Hardware([FromForm] string device, [FromForm] bool ready)
    {
        var sim = Simulator;
        if (sim is null) return NotFound();

        sim.SetHardware(device, ready);
        return Json(_scale.GetSnapshot());
    }
}
