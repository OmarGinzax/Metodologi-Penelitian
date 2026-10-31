using System;

namespace RPGStrategy.Core.Data
{
    [Serializable]
    public class ActionExecutionResult
    {
        public bool Success;
        public string ActorId;
        public string ActionId;
        public string TargetId;
        public int DamageDealt;
        public int HealingRestored;
        public string EventDescription;
        public string ErrorMessage;

        public ActionExecutionResult() { }

        public static ActionExecutionResult CreateSuccess(string actorId, string actionId, string targetId, int damage, int heal, string desc)
        {
            return new ActionExecutionResult
            {
                Success = true,
                ActorId = actorId,
                ActionId = actionId,
                TargetId = targetId,
                DamageDealt = damage,
                HealingRestored = heal,
                EventDescription = desc,
                ErrorMessage = string.Empty
            };
        }

        public static ActionExecutionResult CreateFailure(string actorId, string actionId, string targetId, string error)
        {
            return new ActionExecutionResult
            {
                Success = false,
                ActorId = actorId,
                ActionId = actionId,
                TargetId = targetId,
                DamageDealt = 0,
                HealingRestored = 0,
                EventDescription = string.Empty,
                ErrorMessage = error
            };
        }
    }
}
