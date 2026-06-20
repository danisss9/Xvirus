namespace Xvirus.Model
{
    /// <summary>Response from the Xvirus cloud hash-reputation endpoint.</summary>
    public class CloudReputationInfo
    {
        /// <summary>True when the cloud classifies the hash as malware.</summary>
        public bool Malware { get; set; }
        /// <summary>Optional detection/threat name.</summary>
        public string? Name { get; set; }
        /// <summary>Optional score from 0.0 (clean) to 1.0 (malware).</summary>
        public double? Score { get; set; }
    }
}
