using XvirusService.Model;
using XvirusService.Services;

namespace XvirusService.Api
{
    public static class QuarantineApi
    {
        public static void MapQuarantineEndpoints(this WebApplication app)
        {
            // list all quarantined files
            app.MapGet("/quarantine", (Quarantine quarantine) =>
            {
                return Results.Ok(quarantine.GetFiles());
            });

            // delete entry permanently
            app.MapDelete("/quarantine/{id}", (string id, Quarantine quarantine) =>
            {
                try
                {
                    quarantine.DeleteFile(id);
                    return Results.Ok();
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new ErrorResponseDTO { Error = ex.Message });
                }
            });

            // restore quarantined file; optional overwrite query parameter
            app.MapPost("/quarantine/{id}/restore", (string id, Quarantine quarantine) =>
            {
                try
                {
                    quarantine.RestoreFile(id);
                    return Results.Ok();
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new ErrorResponseDTO { Error = ex.Message });
                }
            });
        }
    }
}