using System;

namespace RPGStrategy.Core.Data
{
    public enum ComplianceStatus
    {
        OBEY,   // Patuh penuh terhadap instruksi Komandan
        ADAPT,  // Menyesuaikan instruksi Komandan dengan kondisi lapangan
        DEFY    // Membangkang secara terbuka terhadap instruksi Komandan
    }

    [Serializable]
    public class AgentDecision
    {
        public string ActorId;
        public string ActionId;
        public string TargetId;
        public string Reasoning;
        public string Compliance;     // "OBEY", "ADAPT", "DEFY"
        public string RadioChatter;   // Dialog suara transmisi radio ke Komandan di benteng berjalan

        public AgentDecision()
        {
            Compliance = "OBEY";
            RadioChatter = string.Empty;
        }

        public AgentDecision(
            string actorId, 
            string actionId, 
            string targetId, 
            string reasoning = "", 
            string compliance = "OBEY", 
            string radioChatter = "")
        {
            ActorId = actorId;
            ActionId = actionId;
            TargetId = targetId;
            Reasoning = reasoning;
            Compliance = compliance;
            RadioChatter = radioChatter;
        }

        public override string ToString()
        {
            return $"[{Compliance}] {ActorId} -> {ActionId} on '{TargetId}' | Comms: \"{RadioChatter}\" | Reason: {Reasoning}";
        }
    }
}
