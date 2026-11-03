using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading.Tasks;
using RPGStrategy.AI.PureLLM;
using UnityEngine;
using UnityEngine.Networking;

namespace RPGStrategy.AI.LLM
{
    /// <summary>DTO respons LLM beserta telemetri token & latensi.</summary>
    [Serializable]
    public class LLMResponseDTO
    {
        public string RawContent;
        public int PromptTokens;
        public int CompletionTokens;
        public int TotalTokens;
        public long LatencyMs;
        public bool IsSuccess;
        public bool RequestSucceeded;
        public string ErrorMessage;
        public string ModelName;
        public long HttpStatusCode;
    }

    /// <summary>Antarmuka layanan LLM; memungkinkan mock untuk pengujian offline.</summary>
    public interface ILLMService2D
    {
        Task<LLMResponseDTO> SendChatCompletionAsync(string systemPrompt, string userPrompt);
    }

    /// <summary>
    /// Klien HTTP LLM berbasis UnityWebRequest (OpenAI-compatible Chat Completions).
    /// Mengukur latensi dengan Stopwatch (Y7) dan mengekstrak usage token (Y6).
    ///
    /// Implementasi memakai coroutine + TaskCompletionSource agar tidak memblokir
    /// main thread Unity dan tetap kompatibel dengan async/await di BattleManager2D.
    /// </summary>
    public class LLMService2D : MonoBehaviour, ILLMService2D
    {
        [Header("LLM Configuration")]
        [SerializeField] private LLMConfig config;

        [Header("Debug")]
        [SerializeField] private bool logRequests = false;
        [SerializeField] private bool logResponses = false;

        public LLMConfig Config => config;

        private void Awake()
        {
            if (config == null) config = LLMConfig.CreateRuntimeDefault();
        }

        public void Configure(LLMConfig newConfig)
        {
            if (newConfig != null) config = newConfig;
        }

        public Task<LLMResponseDTO> SendChatCompletionAsync(string systemPrompt, string userPrompt)
        {
            var completion = new TaskCompletionSource<LLMResponseDTO>();
            StartCoroutine(SendRoutine(systemPrompt, userPrompt, completion));
            return completion.Task;
        }

        private IEnumerator SendRoutine(
            string systemPrompt,
            string userPrompt,
            TaskCompletionSource<LLMResponseDTO> completion)
        {
            if (config == null) config = LLMConfig.CreateRuntimeDefault();

            string endpoint = config.ResolvedEndpoint;
            string payload = BuildPayload(systemPrompt, userPrompt);

            if (logRequests)
            {
                UnityEngine.Debug.Log("[LLMService2D] POST " + endpoint + "\n" + payload);
            }

            var response = new LLMResponseDTO { ModelName = config.ModelName };
            var stopwatch = Stopwatch.StartNew();
            UnityWebRequest request = null;

            try
            {
                request = new UnityWebRequest(endpoint, UnityWebRequest.kHttpVerbPOST);
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(payload));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                if (!string.IsNullOrEmpty(config.ApiKey))
                {
                    request.SetRequestHeader("Authorization", "Bearer " + config.ApiKey);
                }
                request.timeout = config.TimeoutSeconds;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                response.LatencyMs = stopwatch.ElapsedMilliseconds;
                response.IsSuccess = false;
                response.RequestSucceeded = false;
                response.ErrorMessage = "Failed to create UnityWebRequest: " + ex.Message;
                UnityEngine.Debug.LogWarning("[LLMService2D] " + response.ErrorMessage);
                TrySetResult(completion, response);
                yield break;
            }

            yield return request.SendWebRequest();

            stopwatch.Stop();
            response.LatencyMs = stopwatch.ElapsedMilliseconds;
            response.HttpStatusCode = request.responseCode;
            response.RequestSucceeded = request.result == UnityWebRequest.Result.Success;

            string body = request.downloadHandler != null ? request.downloadHandler.text : null;

            if (!response.RequestSucceeded)
            {
                response.IsSuccess = false;
                response.ErrorMessage = "HTTP " + request.responseCode + " (" + request.result + "): " + request.error;
                if (!string.IsNullOrEmpty(body))
                {
                    response.ErrorMessage += " | body: " + Truncate(body, 600);
                }

                // Always surface transport/HTTP failures. Without this the agent silently
                // degrades to an H5 no-op and the cause is only visible in the CSV export.
                UnityEngine.Debug.LogWarning("[LLMService2D] Model '" + config.ModelName +
                                              "' @ " + endpoint + " -> " + response.ErrorMessage);
                request.Dispose();
                TrySetResult(completion, response);
                yield break;
            }

            try
            {
                ParseResponseBody(body, response);
            }
            catch (Exception ex)
            {
                response.IsSuccess = false;
                response.ErrorMessage = "Failed to parse LLM response: " + ex.Message;
            }

            if (!response.IsSuccess)
            {
                UnityEngine.Debug.LogWarning("[LLMService2D] Model '" + config.ModelName +
                                              "' @ " + endpoint + " -> " + response.ErrorMessage);
            }
            else if (logResponses)
            {
                UnityEngine.Debug.Log("[LLMService2D] " + response.LatencyMs + " ms | tokens " +
                                      response.PromptTokens + "/" + response.CompletionTokens +
                                      " | " + response.RawContent);
            }

            request.Dispose();
            TrySetResult(completion, response);
        }

        private string BuildPayload(string systemPrompt, string userPrompt)
        {
            var sb = new StringBuilder(1024);
            sb.Append("{\"model\":\"").Append(JsonUtil.Escape(config.ModelName)).Append("\",");
            sb.Append("\"temperature\":").Append(config.Temperature.ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"max_tokens\":").Append(config.MaxTokens).Append(',');

            if (config.ForceJsonMode)
                sb.Append("\"response_format\":{\"type\":\"json_object\"},");

            sb.Append("\"messages\":[");
            sb.Append("{\"role\":\"system\",\"content\":\"").Append(JsonUtil.Escape(systemPrompt)).Append("\"},");
            sb.Append("{\"role\":\"user\",\"content\":\"").Append(JsonUtil.Escape(userPrompt)).Append("\"}]}");
            return sb.ToString();
        }

        private static void ParseResponseBody(string body, LLMResponseDTO response)
        {
            if (string.IsNullOrEmpty(body))
            {
                response.IsSuccess = false;
                response.ErrorMessage = "LLM response body is empty.";
                return;
            }

            var root = JsonUtil.Parse(body) as Dictionary<string, object>;
            if (root == null)
            {
                response.IsSuccess = false;
                response.ErrorMessage = "LLM response is not a JSON object.";
                return;
            }

            string model = JsonUtil.GetString(root, "model");
            if (!string.IsNullOrEmpty(model)) response.ModelName = model;

            var usage = JsonUtil.AsObject(GetValue(root, "usage"));
            if (usage != null)
            {
                response.PromptTokens = JsonUtil.GetInt(usage, "prompt_tokens");
                response.CompletionTokens = JsonUtil.GetInt(usage, "completion_tokens");
                response.TotalTokens = JsonUtil.GetInt(usage, "total_tokens", response.PromptTokens + response.CompletionTokens);
            }

            string content = null;

            var choices = JsonUtil.AsArray(GetValue(root, "choices"));
            if (choices != null && choices.Count > 0)
            {
                var choice = JsonUtil.AsObject(choices[0]);
                var message = choice != null ? JsonUtil.AsObject(GetValue(choice, "message")) : null;
                if (message != null) content = JsonUtil.GetString(message, "content");
                if (string.IsNullOrEmpty(content) && choice != null) content = JsonUtil.GetString(choice, "text");
            }

            // Fallback untuk endpoint native Ollama (/api/chat atau /api/generate).
            if (string.IsNullOrEmpty(content))
            {
                var message = JsonUtil.AsObject(GetValue(root, "message"));
                if (message != null) content = JsonUtil.GetString(message, "content");
            }
            if (string.IsNullOrEmpty(content)) content = JsonUtil.GetString(root, "response");
            if (string.IsNullOrEmpty(content)) content = JsonUtil.GetString(root, "content");

            response.RawContent = content;
            response.IsSuccess = !string.IsNullOrWhiteSpace(content);
            if (!response.IsSuccess)
            {
                response.ErrorMessage = "LLM response did not contain message content.";
            }
        }

        private static object GetValue(Dictionary<string, object> obj, string key)
        {
            if (obj == null || key == null) return null;
            object value;
            return obj.TryGetValue(key, out value) ? value : null;
        }

        private static void TrySetResult(TaskCompletionSource<LLMResponseDTO> completion, LLMResponseDTO response)
        {
            // Tanpa RunContinuationsAsynchronously: SetResult dipanggil dari main thread
            // (coroutine Unity), sehingga continuation async/await agen juga berjalan di main thread.
            completion.TrySetResult(response);
        }

        private static string Truncate(string value, int max)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= max) return value;
            return value.Substring(0, max) + "...";
        }
    }
}
