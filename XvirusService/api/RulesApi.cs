using System.Text.RegularExpressions;
using Xvirus;
using Xvirus.Model;
using XvirusService.Model;
using XvirusService.Services;

namespace XvirusService.Api
{
    public static class RulesApi
    {
        private static readonly Regex DomainRegex =
            new(@"^(?=.{1,253}$)(?!-)[a-zA-Z0-9-]+(\.[a-zA-Z0-9-]+)+$", RegexOptions.Compiled);

        public static void MapRulesEndpoints(this WebApplication app)
        {
            // GET /rules — return all rules
            app.MapGet("/rules", (Rules rules) =>
            {
                return Results.Ok(rules.GetAllRules());
            });

            // POST /rules — add a rule of any kind (program, ip, port, domain)
            app.MapPost("/rules", (CreateRuleRequestDTO body, Rules rules, EtwDnsListener dns) =>
            {
                var target = (body?.Path ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(target))
                    return Results.BadRequest(new ErrorResponseDTO { Error = "Target path/value must not be empty." });

                var kind = (body!.Kind ?? "program").Trim().ToLowerInvariant() switch
                {
                    "ip" => RuleKind.Ip,
                    "port" => RuleKind.Port,
                    "domain" => RuleKind.Domain,
                    _ => RuleKind.Program
                };

                if (kind == RuleKind.Ip && !Firewall.IsValidIpOrCidr(target))
                    return Results.BadRequest(new ErrorResponseDTO { Error = $"Invalid IP address or CIDR: '{target}'." });

                if (kind == RuleKind.Port && !Firewall.IsValidPortSpec(target))
                    return Results.BadRequest(new ErrorResponseDTO { Error = $"Invalid port or range (1-65535): '{target}'." });

                if (kind == RuleKind.Domain)
                {
                    target = target.TrimEnd('.').ToLowerInvariant();
                    if (!DomainRegex.IsMatch(target))
                        return Results.BadRequest(new ErrorResponseDTO { Error = $"Invalid domain name: '{target}'." });
                }

                var ruleType = string.Equals(body.Type, "allow", StringComparison.OrdinalIgnoreCase)
                    ? RuleType.Allow
                    : RuleType.Block;

                var rule = rules.AddRule(new Rule
                {
                    Kind = kind,
                    Path = target,
                    Type = ruleType,
                    Protocol = body.Protocol,
                    Direction = body.Direction,
                    Profile = body.Profile
                });

                if (kind == RuleKind.Domain && ruleType == RuleType.Block && AppInfo.IsFirewall)
                    _ = dns.EnforceDomainNowAsync(target);

                return Results.Ok(rule);
            });

            // POST /rules/allow — add a program allow rule  { "path": "..." }
            app.MapPost("/rules/allow", (RulePathDTO body, Rules rules) =>
            {
                if (string.IsNullOrWhiteSpace(body?.Path))
                    return Results.BadRequest(new ErrorResponseDTO { Error = "Path must not be empty." });

                var rule = rules.AddAllowRule(body.Path);
                return Results.Ok(rule);
            });

            // POST /rules/block — add a program block rule  { "path": "..." }
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
