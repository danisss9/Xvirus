using Xvirus;
using Xvirus.Model;
using XvirusService.Model;

namespace XvirusService.Api
{
    public static class RulesApi
    {
        public static void MapRulesEndpoints(this WebApplication app)
        {
            // GET /rules — return all rules
            app.MapGet("/rules", (Rules rules) =>
            {
                return Results.Ok(rules.GetAllRules());
            });

            // POST /rules/allow — add an allow rule  { "path": "..." }
            app.MapPost("/rules/allow", (RulePathDTO body, Rules rules) =>
            {
                if (string.IsNullOrWhiteSpace(body?.Path))
                    return Results.BadRequest(new ErrorResponseDTO { Error = "Path must not be empty." });

                var rule = rules.AddAllowRule(body.Path);
                return Results.Ok(rule);
            });

            // POST /rules/block — add a block rule  { "path": "..." }
            app.MapPost("/rules/block", (RulePathDTO body, Rules rules) =>
            {
                if (string.IsNullOrWhiteSpace(body?.Path))
                    return Results.BadRequest(new ErrorResponseDTO { Error = "Path must not be empty." });

                var rule = rules.AddBlockRule(body.Path);
                return Results.Ok(rule);
            });

            // DELETE /rules/{id} — remove a rule by ID
            app.MapDelete("/rules/{id}", (string id, Rules rules) =>
            {
                rules.RemoveRule(id);
                return Results.Ok();
            });

        }
    }
}
