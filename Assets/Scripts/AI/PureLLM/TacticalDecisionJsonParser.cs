using System;
using System.Collections.Generic;
using RPGStrategy.AI.Baseline;

namespace RPGStrategy.AI.PureLLM
{
    /// <summary>Hasil parsing output mentah LLM menjadi parameter aksi terstruktur.</summary>
    public sealed class TacticalDecisionParseResult
    {
        public bool Success;
        public string Error;
        public string RawResponse;
        public string JsonObject;

        public string RawAction;
        public bool ActionRecognized;
        public TacticalActionType MappedAction;

        public string TargetId;
        public string SkillId;
        public string Reasoning;

        public bool HasDestination;
        public bool DestinationFieldsValid;
        public int DestinationX;
        public int DestinationY;
    }

    /// <summary>
    /// Parser JSON yang toleran terhadap output LLM:
    ///   - JSON polos
    ///   - ```json ... ``` markdown fence
    ///   - teks pembuka/penutup yang mengelilingi objek JSON
    /// </summary>
    public static class TacticalDecisionJsonParser
    {
        public static TacticalDecisionParseResult Parse(string rawResponse)
        {
            var result = new TacticalDecisionParseResult
            {
                RawResponse = rawResponse,
                MappedAction = TacticalActionType.Idle
            };

            if (string.IsNullOrWhiteSpace(rawResponse))
            {
                result.Success = false;
                result.Error = "LLM response is empty.";
                return result;
            }

            string json = JsonUtil.ExtractFirstJsonObject(rawResponse);
            if (string.IsNullOrEmpty(json))
            {
                result.Success = false;
                result.Error = "No JSON object found in LLM response.";
                return result;
            }
            result.JsonObject = json;

            Dictionary<string, object> root;
            try
            {
                root = JsonUtil.Parse(json) as Dictionary<string, object>;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = "JSON parse error: " + ex.Message;
                return result;
            }

            if (root == null)
            {
                result.Success = false;
                result.Error = "LLM JSON root is not an object.";
                return result;
            }

            result.RawAction = JsonUtil.GetString(root, "action");
            if (string.IsNullOrWhiteSpace(result.RawAction))
            {
                result.Success = false;
                result.Error = "Missing required field 'action'.";
                return result;
            }

            result.TargetId = ReadOptionalString(root, "target_id");
            result.SkillId = ReadOptionalString(root, "skill_id");
            result.Reasoning = JsonUtil.GetString(root, "reasoning", string.Empty);

            ParseDestination(root, result);
            result.MappedAction = MapAction(result.RawAction, out result.ActionRecognized);
            result.Success = true;
            return result;
        }

        public static TacticalActionType MapAction(string rawAction, out bool recognized)
        {
            recognized = true;
            if (string.IsNullOrWhiteSpace(rawAction))
            {
                recognized = false;
                return TacticalActionType.Idle;
            }

            string key = NormalizeActionKey(rawAction);

            switch (key)
            {
                case "move":
                case "moveto":
                case "movetotile":
                case "movetocover":
                    return TacticalActionType.Move;

                case "shoot":
                case "fire":
                case "attack":
                case "basicattack":
                    return TacticalActionType.Shoot;

                case "moveandshoot":
                case "movethenshoot":
                    return TacticalActionType.MoveAndShoot;

                case "defend":
                case "hunkerdown":
                case "defensive":
                    return TacticalActionType.Defend;

                case "castskill":
                case "skill":
                case "useskill":
                    return TacticalActionType.CastSkill;

                case "moveandcastskill":
                case "movethencastskill":
                case "moveandskill":
                    return TacticalActionType.MoveAndCastSkill;

                case "flee":
                case "retreat":
                case "evacuate":
                    return TacticalActionType.Flee;

                case "idle":
                case "wait":
                case "pass":
                case "hold":
                    return TacticalActionType.Idle;

                default:
                    recognized = false;
                    return TacticalActionType.Idle;
            }
        }

        private static string NormalizeActionKey(string rawAction)
        {
            var chars = new char[rawAction.Length];
            int count = 0;
            for (int i = 0; i < rawAction.Length; i++)
            {
                char c = rawAction[i];
                if (char.IsLetterOrDigit(c)) chars[count++] = char.ToLowerInvariant(c);
            }
            return new string(chars, 0, count);
        }

        private static string ReadOptionalString(Dictionary<string, object> root, string key)
        {
            object value;
            if (!root.TryGetValue(key, out value) || value == null) return null;
            string s = value as string;
            if (s != null) return s.Trim();
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        private static void ParseDestination(Dictionary<string, object> root, TacticalDecisionParseResult result)
        {
            object destinationValue;
            if (!root.TryGetValue("destination", out destinationValue) || destinationValue == null)
                return;

            result.HasDestination = true;
            var destination = destinationValue as Dictionary<string, object>;
            if (destination == null)
            {
                result.DestinationFieldsValid = false;
                return;
            }

            bool hasX = JsonUtil.HasKey(destination, "x");
            bool hasY = JsonUtil.HasKey(destination, "y");
            if (!hasX || !hasY)
            {
                result.DestinationFieldsValid = false;
                return;
            }

            result.DestinationX = JsonUtil.GetInt(destination, "x", int.MinValue);
            result.DestinationY = JsonUtil.GetInt(destination, "y", int.MinValue);
            result.DestinationFieldsValid =
                result.DestinationX != int.MinValue && result.DestinationY != int.MinValue;
        }
    }
}
