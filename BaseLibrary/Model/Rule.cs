using System;

namespace Xvirus.Model
{
    public enum RuleType
    {
        Allow,
        Block
    }

    public class Rule
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Path { get; set; } = string.Empty;
        public RuleType? Type { get; set; }
    }
}
