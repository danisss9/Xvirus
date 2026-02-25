using XvirusService.Model;
using XvirusService.Services;

namespace XvirusService.Api;

public static class ActionsApi
{
    public static void MapActionsEndpoints(this WebApplication app)
    {
        // Return all threats waiting for a user decision (no action taken yet)
        app.MapGet("/actions/pending", (ThreatAlertService alerts) =>
            Results.Ok(alerts.GetPending()));

        // Submit a user action for a specific pending threat
        app.MapPost("/actions/{id}", (string id, ThreatActionDTO body, ThreatAlertService alerts) =>
        {
            if (body.Action is not ("quarantine" or "allow"))
                return Results.BadRequest("Action must be 'quarantine' or 'allow'.");

            return alerts.RespondToAlert(id, body.Action, body.RememberDecision)
                ? Results.Ok()
                : Results.NotFound();
        });
    }
}
