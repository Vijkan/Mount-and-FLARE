using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LocalAIChat
{
    /// <summary>
    /// Minimal client for any OpenAI-compatible chat endpoint
    /// (llama.cpp llama-server, KoboldCpp, LM Studio, Ollama's /v1, etc.).
    /// </summary>
    public static class LlmClient
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
        private static HttpClient _http;

        private static HttpClient Http =>
            _http ?? (_http = new HttpClient { Timeout = TimeSpan.FromSeconds(ModConfig.TimeoutSeconds) });

        public static async Task<string> ChatAsync(List<Dictionary<string, string>> messages)
        {
            var body = new Dictionary<string, object>
            {
                ["model"] = ModConfig.Model,
                ["messages"] = messages,
                ["temperature"] = ModConfig.Temperature,
                ["max_tokens"] = ModConfig.MaxTokens,
                ["stream"] = false
            };
            if (ModConfig.DisableThinking)
            {
                body["chat_template_kwargs"] = new Dictionary<string, object> { ["enable_thinking"] = false };
            }

            using (var req = new HttpRequestMessage(HttpMethod.Post, ModConfig.Endpoint))
            {
                req.Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(ModConfig.ApiKey))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ModConfig.ApiKey);

                using (var resp = await Http.SendAsync(req).ConfigureAwait(false))
                {
                    var text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode)
                        throw new Exception($"Server returned {(int)resp.StatusCode}: {Trim(text, 200)}");

                    var root = Json.Deserialize<Dictionary<string, object>>(text);
                    var choice = ((IEnumerable)root["choices"]).Cast<object>().First() as Dictionary<string, object>;
                    var message = choice["message"] as Dictionary<string, object>;
                    var content = message["content"] as string ?? "";

                    // Strip any reasoning the model emitted anyway.
                    content = Regex.Replace(content, @"<think>[\s\S]*?</think>", "");
                    content = Regex.Replace(content, @"^[\s\S]*</think>", "");
                    return content.Trim();
                }
            }
        }

        private static string Trim(string s, int max) =>
            string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
    }
}
