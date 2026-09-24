namespace Godsbound.Core.Combat
{
    /// <summary>Combat flags and elapsed-time deadlines. Match/power ticks own expiry effects.</summary>
    public sealed class CombatStatus
    {
        public float BuffDamage { get; set; } = 1f;
        public float InvulnerableUntil { get; set; }
        public float AegisUntil { get; set; }
        public float BloodlustUntil { get; set; }
        public float WeakenUntil { get; set; }
        public float FrenzyUntil { get; set; }
        public float PoisonUntil { get; set; }
        public float PoisonDps { get; set; }
        public float LastStandEndsAt { get; set; }
        public float RetreatDeadline { get; set; }
        public bool AmbushUsed { get; set; }
        public bool Invisible { get; set; }
        public bool HasRevived { get; set; }
        public bool LastStandUsed { get; set; }
        public bool Retreating { get; set; }
    }
}
