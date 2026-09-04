using Microsoft.AspNetCore.Mvc;
using ScanTrackNode.Services;

namespace ScanTrackNode.Controllers;

// POST /forceheartbeat — tvingar fram en omedelbar omregistrering mot registret.
// Spärrad till max en gång per 10 minuter så att registret inte spammas.
[ApiController]
public class HeartbeatController : ControllerBase
{
    private static readonly TimeSpan MinInterval = TimeSpan.FromMinutes(10);
    private static DateTime _lastForcedUtc = DateTime.MinValue;

    private readonly NodeRegistry _registry;
    private readonly ILogger<HeartbeatController> _logger;

    public HeartbeatController(NodeRegistry registry, ILogger<HeartbeatController> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    [HttpPost("/forceheartbeat")]
    public async Task<IActionResult> Force()
    {
        var now = DateTime.UtcNow;
        var sedanSenast = now - _lastForcedUtc;

        if (sedanSenast < MinInterval)
        {
            var kvar = MinInterval - sedanSenast;
            _logger.LogWarning("Forcerat heartbeat nekat — {Sekunder}s kvar till nästa tillåtna", (int)kvar.TotalSeconds);

            return StatusCode(429, new
            {
                fel = "För tätt — max ett forcerat heartbeat per 10 minuter",
                sekunderKvar = (int)kvar.TotalSeconds
            });
        }

        _lastForcedUtc = now;
        _logger.LogInformation("Forcerat heartbeat begärt");
        await _registry.RegisterSelfAsync();

        return Ok(new { status = "heartbeat skickat", tidUtc = now });
    }
}
