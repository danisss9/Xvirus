using BaseLibrary.Serializers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Xvirus.Model;

namespace Xvirus
{
    public class Rules
    {
        private static readonly ReaderWriterLock rwl = new ReaderWriterLock();
        private Dictionary<string, Rule> _rules = new Dictionary<string, Rule>();

        public Rules(string rulesFilePath = "rules.json")
        {
            _rules = GetRules(rulesFilePath);
        }

        public Rule AddAllowRule(string path, string rulesFilePath = "rules.json")
        {
            return AddRule(path, RuleType.Allow, rulesFilePath);
        }

        public Rule AddBlockRule(string path, string rulesFilePath = "rules.json")
        {
            return AddRule(path, RuleType.Block, rulesFilePath);
        }

        public Rule AddRule(string path, RuleType type, string rulesFilePath = "rules.json")
        {
            var rule = new Rule { Path = path, Type = type };
            var rulesPath = Utils.RelativeToFullPath(rulesFilePath);

            rwl.AcquireWriterLock(2000);
            try
            {
                _rules.Add(rule.Id, rule);
                var json = JsonSerializer.Serialize(rule, SourceGenerationContext.Default.Rule);
                File.AppendAllText(rulesPath, json + Environment.NewLine);
            }
            finally
            {
                rwl.ReleaseWriterLock();
            }
            return rule;
        }

        public void RemoveRule(string id, string rulesFilePath = "rules.json")
        {
            var rulesPath = Utils.RelativeToFullPath(rulesFilePath);
            rwl.AcquireWriterLock(2000);
            try
            {
                if (_rules.Remove(id))
                {
                    // Rewrite the entire file without the removed rule
                    var allRulesJson = string.Join(Environment.NewLine, _rules.Values.Select(r => JsonSerializer.Serialize(r, SourceGenerationContext.Default.Rule)));
                    File.WriteAllText(rulesPath, allRulesJson + Environment.NewLine);
                }
            }
            finally
            {
                rwl.ReleaseWriterLock();
            }
        }


        public RuleType? GetRuleType(string path)
        {
            return _rules.GetValueOrDefault(path)?.Type;
        }

        public List<Rule> GetAllRules()
        {
            return [.. _rules.Values];
        }

        private static Dictionary<string, Rule> GetRules(string rulesFilePath = "rules.json")
        {
            var result = new Dictionary<string, Rule>();
            var path = Utils.RelativeToFullPath(rulesFilePath);
            try
            {
                if (File.Exists(path))
                {
                    foreach (var line in File.ReadAllLines(path))
                    {
                        try
                        {
                            var e = JsonSerializer.Deserialize(line, SourceGenerationContext.Default.Rule);
                            if (e != null)
                                result.Add(e.Id, e);
                        }
                        catch { /* ignore parse errors */ }
                    }
                }
            }
            catch { /* swallow any I/O errors */ }
            return result;
        }
    }
}
