using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Web.Script.Serialization;

namespace LocalAIChat
{
    /// <summary>Settings read from Modules/LocalAIChat/config.json.</summary>
    public static class ModConfig
    {
        public static string Endpoint = "http://127.0.0.1:8080/v1/chat/completions";
        public static string Model = "local";
        public static string ApiKey = "";
        public static double Temperature = 0.8;
        public static int MaxTokens = 300;
        public static int HistoryTurns = 20;      // exchanges remembered per character
        public static int TimeoutSeconds = 120;
        public static bool DisableThinking = true; // turns off Qwen3-style <think> output
        public static string ExtraSystemPrompt = "";

        // DLL lives in Modules/LocalAIChat/bin/Win64_Shipping_Client → go up two levels.
        public static string ModuleDir =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(ModConfig).Assembly.Location), "..", ".."));

        public static void Load()
        {
            try
            {
                var path = Path.Combine(ModuleDir, "config.json");
                if (!File.Exists(path)) return;

                var d = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
                if (d == null) return;

                Endpoint = Str(d, "Endpoint", Endpoint);
                Model = Str(d, "Model", Model);
                ApiKey = Str(d, "ApiKey", ApiKey);
                ExtraSystemPrompt = Str(d, "ExtraSystemPrompt", ExtraSystemPrompt);
                Temperature = Num(d, "Temperature", Temperature);
                MaxTokens = (int)Num(d, "MaxTokens", MaxTokens);
                HistoryTurns = (int)Num(d, "HistoryTurns", HistoryTurns);
                TimeoutSeconds = (int)Num(d, "TimeoutSeconds", TimeoutSeconds);
                if (d.TryGetValue("DisableThinking", out var t) && t is bool b) DisableThinking = b;
            }
            catch
            {
                // Bad config → keep defaults rather than crash the game.
            }
        }

        private static string Str(Dictionary<string, object> d, string key, string fallback) =>
            d.TryGetValue(key, out var v) && v != null ? v.ToString() : fallback;

        private static double Num(Dictionary<string, object> d, string key, double fallback) =>
            d.TryGetValue(key, out var v) && v != null
                ? Convert.ToDouble(v, CultureInfo.InvariantCulture)
                : fallback;
    }
}
