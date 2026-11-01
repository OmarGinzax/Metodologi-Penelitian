using System;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;
using RPGStrategy.Core.Data;

namespace RPGStrategy.AI.LLM
{
    [Serializable]
    public class LLMCallResult
    {
        public bool Success;
        public string RawResponse;
        public AgentDecision ParsedDecision;
        public long LatencyMs;
        public int InputTokens;
        public int OutputTokens;
        public string ErrorMessage;
    }

    [Serializable]
    public class OpenAIChatMessage
    {
        public string role;
        public string content;
    }

    [Serializable]
    public class OpenAIChatRequest
    {
        public string model;
        public OpenAIChatMessage[] messages;
        public float temperature;
    }

    [Serializable]
    public class OpenAIChoice
    {
        public OpenAIChatMessage message;
    }

    [Serializable]
    public class OpenAIUsage
    {
        public int prompt_tokens;
        public int completion_tokens;
        public int total_tokens;
    }

    [Serializable]
    public class OpenAIChatResponse
    {
        public OpenAIChoice[] choices;
        public OpenAIUsage usage;
    }

    [Serializable]
    public class RawLLMDecisionJSON
    {
        public string compliance;
        public string radio_chatter;
        public string action_id;
        public string target_id;
        public string reasoning;
    }

    public class LLMService
    {
        private readonly LLMConfig _config;

        public LLMService(LLMConfig config)
        {
            _config = config ?? ScriptableObject.CreateInstance<LLMConfig>();
        }

        public async Task<LLMCallResult> RequestDecisionAsync(
            string systemPrompt, 
            string userPrompt, 
            CombatStateSnapshot snapshot, 
            CancellationToken cancellationToken = default)
        {
            var result = new LLMCallResult();
            var stopwatch = Stopwatch.StartNew();

            // 1. Cek mode offline / mock
            if (_config.UseMockIfOffline && string.IsNullOrEmpty(_config.BaseUrl))
            {
                stopwatch.Stop();
                return GenerateMockDecision(snapshot, stopwatch.ElapsedMilliseconds);
            }

            var requestPayload = new OpenAIChatRequest
            {
                model = _config.ModelName,
                temperature = _config.Temperature,
                messages = new[]
                {
                    new OpenAIChatMessage { role = "system", content = systemPrompt },
                    new OpenAIChatMessage { role = "user", content = userPrompt }
                }
            };

            string jsonBody = JsonUtility.ToJson(requestPayload);
            byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonBody);

            using (var webRequest = new UnityWebRequest(_config.BaseUrl, "POST"))
            {
                webRequest.uploadHandler = new UploadHandlerRaw(bodyRaw);
                webRequest.downloadHandler = new DownloadHandlerBuffer();
                webRequest.SetRequestHeader("Content-Type", "application/json");

                if (!string.IsNullOrEmpty(_config.ApiKey))
                {
                    webRequest.SetRequestHeader("Authorization", "Bearer " + _config.ApiKey);
                }

                webRequest.timeout = _config.TimeoutSeconds;

                try
                {
                    var asyncOp = webRequest.SendWebRequest();
                    while (!asyncOp.isDone)
                    {
                        if (cancellationToken.IsCancellationRequested)
                        {
                            webRequest.Abort();
                            stopwatch.Stop();
                            result.Success = false;
                            result.ErrorMessage = "Operation cancelled.";
                            return result;
                        }
                        await Task.Yield();
                    }

                    stopwatch.Stop();
                    result.LatencyMs = stopwatch.ElapsedMilliseconds;

                    if (webRequest.result == UnityWebRequest.Result.Success)
                    {
                        string respText = webRequest.downloadHandler.text;
                        result.RawResponse = respText;

                        var apiResp = JsonUtility.FromJson<OpenAIChatResponse>(respText);
                        if (apiResp?.choices != null && apiResp.choices.Length > 0)
                        {
                            string content = apiResp.choices[0].message?.content ?? "";
                            result.ParsedDecision = ParseLLMDecision(content, snapshot.CurrentActor.EntityId);
                            result.Success = true;
                            result.InputTokens = apiResp.usage?.prompt_tokens ?? EstimateTokens(systemPrompt + userPrompt);
                            result.OutputTokens = apiResp.usage?.completion_tokens ?? EstimateTokens(content);
                            return result;
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Fallback jika jaringan offline dan mock diizinkan
                    if (_config.UseMockIfOffline)
                    {
                        stopwatch.Stop();
                        return GenerateMockDecision(snapshot, stopwatch.ElapsedMilliseconds);
                    }

                    stopwatch.Stop();
                    result.Success = false;
                    result.LatencyMs = stopwatch.ElapsedMilliseconds;
                    result.ErrorMessage = ex.Message;
                    return result;
                }
            }

            // Jika sampai di sini karena kegagalan jaringan
            if (_config.UseMockIfOffline)
            {
                return GenerateMockDecision(snapshot, stopwatch.ElapsedMilliseconds);
            }

            result.Success = false;
            result.LatencyMs = stopwatch.ElapsedMilliseconds;
            result.ErrorMessage = "Network request failed.";
            return result;
        }

        public AgentDecision ParseLLMDecision(string rawContent, string defaultActorId)
        {
            if (string.IsNullOrEmpty(rawContent))
            {
                return new AgentDecision(defaultActorId, "Action_Defend", defaultActorId, "Empty LLM content", "OBEY");
            }

            // Bersihkan markdown backticks jika LLM membalas dengan ```json ... ```
            string cleaned = rawContent.Trim();
            if (cleaned.StartsWith("```json"))
            {
                cleaned = cleaned.Substring(7);
            }
            if (cleaned.StartsWith("```"))
            {
                cleaned = cleaned.Substring(3);
            }
            if (cleaned.EndsWith("```"))
            {
                cleaned = cleaned.Substring(0, cleaned.Length - 3);
            }
            cleaned = cleaned.Trim();

            try
            {
                var dto = JsonUtility.FromJson<RawLLMDecisionJSON>(cleaned);
                if (dto != null && !string.IsNullOrEmpty(dto.action_id))
                {
                    return new AgentDecision(
                        defaultActorId,
                        dto.action_id,
                        dto.target_id,
                        dto.reasoning,
                        string.IsNullOrEmpty(dto.compliance) ? "OBEY" : dto.compliance.ToUpper(),
                        dto.radio_chatter
                    );
                }
            }
            catch
            {
                // Format parsing rusak (Kategori H5)
            }

            return new AgentDecision(defaultActorId, "INVALID_FORMAT", defaultActorId, 
                $"Failed to parse JSON: {rawContent}", "DEFY", "Static on radio... comms unintelligible.");
        }

        private LLMCallResult GenerateMockDecision(CombatStateSnapshot snapshot, long elapsedMs)
        {
            var actor = snapshot.CurrentActor;
            var directive = snapshot.ActiveDirective;
            string compliance = "OBEY";
            string chatter = "Command acknowledged, executing directive.";
            string chosenAction = "BasicAttack";
            string chosenTarget = snapshot.Enemies.Find(e => e.IsAlive)?.EntityId ?? actor.EntityId;
            string reason = "Mock tactical reasoning aligned with directive.";

            // Logika Mock Realistis untuk testing offline:
            // Jika ada perintah FullTacticalRetreat namun ada rekan SwornBond yang sekarat, Marcus DEFY!
            var dyingSworn = snapshot.Allies.Find(a => a.IsAlive && a.HPPercentage <= 25f);
            if (directive?.Type == RPGStrategy.Core.Command.DirectiveType.FullTacticalRetreat && dyingSworn != null && actor.CurrentTraitId == "LoyalProtector")
            {
                compliance = "DEFY";
                chatter = $"Negative Command! I'm not leaving {dyingSworn.Name} behind!";
                chosenAction = $"Cover_{dyingSworn.EntityId}";
                chosenTarget = dyingSworn.EntityId;
                reason = "Sworn bond emotional priority overrides retreat directive.";
            }
            else if (snapshot.IsActionAvailable("Execute"))
            {
                chosenAction = "Execute";
                chatter = "Finishing wounded target!";
            }

            var decision = new AgentDecision(actor.EntityId, chosenAction, chosenTarget, reason, compliance, chatter);

            return new LLMCallResult
            {
                Success = true,
                RawResponse = JsonUtility.ToJson(decision),
                ParsedDecision = decision,
                LatencyMs = (long)Mathf.Max(120, elapsedMs),
                InputTokens = 350,
                OutputTokens = 65,
                ErrorMessage = string.Empty
            };
        }

        private int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            return Mathf.Max(1, text.Length / 4); // Estimasi standar 1 token ~= 4 karakter
        }
    }
}
