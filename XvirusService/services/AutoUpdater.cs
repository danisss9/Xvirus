using Xvirus;
using System.Text.Json;
using XvirusService.Model;

namespace XvirusService.Services;

/// <summary>
/// Hosted service that runs an update check at application start-up when
/// <see cref="Xvirus.Model.SettingsDTO.CheckSDKUpdates"/> is enabled, and
/// broadcasts the progress to the front-end via <see cref="ServerEventService"/>.
/// </summary>
public class AutoUpdater : IHostedService
{
    private readonly SettingsService _settings;
    private readonly ServerEventService _events;

    public AutoUpdater(SettingsService settings, ServerEventService events)
    {
        _settings = settings;
        _events = events;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_settings.Settings.CheckSDKUpdates)
        {
            Console.WriteLine("AutoUpdater: automatic updates disabled, skipping.");
            return;
        }

        // Give SSE clients a few seconds to connect before sending the first event.
        await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
        if (cancellationToken.IsCancellationRequested) return;

        Console.WriteLine("AutoUpdater: starting update check...");

        await _events.SendAsync("updating",
            JsonSerializer.Serialize(
                new SseMessageDTO { Message = "Checking for updates..." },
                AppJsonSerializerContext.Default.SseMessageDTO));

        try
        {
            var message = await Task.Run(
                () => Updater.CheckUpdates(_settings.Settings),
                cancellationToken);

            _settings.Reload();

            Console.WriteLine($"AutoUpdater: complete – {message}");

            await _events.SendAsync("update-complete",
                JsonSerializer.Serialize(
                    new SseMessageDTO { Message = message },
                    AppJsonSerializerContext.Default.SseMessageDTO));
        }
        catch (OperationCanceledException)
        {
            // Shutting down – nothing to do.
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AutoUpdater: update check failed – {ex.Message}");

            await _events.SendAsync("update-complete",
                JsonSerializer.Serialize(
                    new SseMessageDTO { Message = "Update check failed." },
                    AppJsonSerializerContext.Default.SseMessageDTO));
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
