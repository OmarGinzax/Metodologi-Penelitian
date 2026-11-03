using UnityEngine;

namespace RPGStrategy.AI.LLM
{
    /// <summary>
    /// Konfigurasi endpoint LLM (OpenAI-compatible). Dapat disimpan sebagai asset
    /// ScriptableObject sehingga peneliti dapat berganti model tanpa mengubah kode.
    ///
    /// Contoh endpoint:
    ///   - Ollama lokal   : http://localhost:11434/v1/chat/completions
    ///   - OpenAI         : https://api.openai.com/v1/chat/completions
    ///   - OpenRouter     : https://openrouter.ai/api/v1/chat/completions
    /// </summary>
    [CreateAssetMenu(fileName = "LLMConfig", menuName = "RPGStrategy/Pure LLM/LLM Config", order = 1)]
    public class LLMConfig : ScriptableObject
    {
        [Header("Endpoint")]
        [SerializeField] private string baseUrl = "http://localhost:11434/v1/chat/completions";
        [SerializeField] private string apiKey = string.Empty;
        [SerializeField] private string modelName = "llama3.2:latest";

        [Header("Generation Parameters")]
        [Range(0f, 2f)]
        [SerializeField] private float temperature = 0.2f;
        [SerializeField] private int maxTokens = 512;
        [Tooltip("Aktifkan hanya untuk provider yang mendukung response_format=json_object (OpenAI).")]
        [SerializeField] private bool forceJsonMode = false;

        [Header("Network")]
        [SerializeField] private int timeoutSeconds = 30;

        public string BaseUrl { get => baseUrl; set => baseUrl = value; }
        public string ApiKey { get => apiKey; set => apiKey = value; }
        public string ModelName { get => modelName; set => modelName = value; }
        public float Temperature { get => temperature; set => temperature = Mathf.Clamp(value, 0f, 2f); }
        public int MaxTokens { get => maxTokens; set => maxTokens = Mathf.Max(1, value); }
        public bool ForceJsonMode { get => forceJsonMode; set => forceJsonMode = value; }
        public int TimeoutSeconds { get => timeoutSeconds; set => timeoutSeconds = Mathf.Max(1, value); }

        /// <summary>
        /// Menormalkan BaseUrl menjadi endpoint /chat/completions.
        /// Mendukung root Ollama, base /v1, atau endpoint penuh.
        /// </summary>
        public string ResolvedEndpoint
        {
            get
            {
                string url = (baseUrl ?? string.Empty).Trim().TrimEnd('/');
                if (string.IsNullOrEmpty(url))
                    return "http://localhost:11434/v1/chat/completions";

                if (url.EndsWith("/chat/completions")) return url;
                if (url.EndsWith("/v1")) return url + "/chat/completions";
                return url + "/v1/chat/completions";
            }
        }

        public static LLMConfig CreateRuntimeDefault()
        {
            return CreateInstance<LLMConfig>();
        }
    }
}
