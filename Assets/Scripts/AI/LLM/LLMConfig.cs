using UnityEngine;

namespace RPGStrategy.AI.LLM
{
    [CreateAssetMenu(fileName = "NewLLMConfig", menuName = "RPG AI/LLM Config")]
    public class LLMConfig : ScriptableObject
    {
        [Header("API Connection")]
        [SerializeField] private string baseUrl = "http://localhost:11434/v1/chat/completions";
        [SerializeField] private string apiKey = "";
        [SerializeField] private string modelName = "llama-3.2-3b-instruct";
        [Range(0f, 1f)]
        [SerializeField] private float temperature = 0.2f; // Rendah untuk evaluasi taktis deterministik relatif
        [SerializeField] private int timeoutSeconds = 15;

        [Header("Testing / Offline Mode")]
        [SerializeField] private bool useMockIfOffline = true;

        public string BaseUrl { get => baseUrl; set => baseUrl = value; }
        public string ApiKey { get => apiKey; set => apiKey = value; }
        public string ModelName { get => modelName; set => modelName = value; }
        public float Temperature { get => temperature; set => temperature = value; }
        public int TimeoutSeconds { get => timeoutSeconds; set => timeoutSeconds = value; }
        public bool UseMockIfOffline { get => useMockIfOffline; set => useMockIfOffline = value; }
    }
}
