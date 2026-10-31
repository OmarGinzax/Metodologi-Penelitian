using System;

namespace RPGStrategy.Core.Command
{
    public enum DirectiveType
    {
        FocusFireTarget,         // "Fokuskan seluruh daya tembak ke Target X!"
        DefensiveHold,           // "Pertahankan barikade, jangan maju, pasang sikap bertahan!"
        FullTacticalRetreat,     // "Batalkan operasi! Seluruh unit segera mundur ke transport!"
        ProtectVulnerableAlly,   // "Lindungi Rekan X, dia dalam bahaya maut!"
        SuppressiveContainment,  // "Bungkam area koridor musuh dengan tembakan bertubi-tubi!"
        FreeEngage               // "Bebas bertindak sesuai inisiatif masing-masing di lapangan!"
    }

    [Serializable]
    public class CommanderDirectiveDTO
    {
        public DirectiveType Type;
        public string TargetEntityId;
        public string DirectiveText;
        public int TurnIssued;

        public CommanderDirectiveDTO() { }

        public CommanderDirectiveDTO(DirectiveType type, string targetId, string text, int turn)
        {
            Type = type;
            TargetEntityId = targetId;
            DirectiveText = text;
            TurnIssued = turn;
        }

        public override string ToString()
        {
            return $"[COMMAND DIRECTIVE] {Type} on '{TargetEntityId}': \"{DirectiveText}\" (Turn {TurnIssued})";
        }
    }
}
