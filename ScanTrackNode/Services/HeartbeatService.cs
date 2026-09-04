using Microsoft.Extensions.Hosting;

namespace ScanTrackNode.Services;

public class HeartbeatService : BackgroundService
{
    private readonly NodeRegistry _registry;
    private readonly ILogger<HeartbeatService> _logger;

    public HeartbeatService(
        NodeRegistry registry,
        ILogger<HeartbeatService> logger)
    {
        _registry = registry;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await _registry.RegisterSelfAsync();

                _logger.LogInformation(
                    "Heartbeat sent successfully.");
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Failed to send heartbeat.");
            }

            await Task.Delay(
                TimeSpan.FromHours(1),
                ct);
        }
    }
}