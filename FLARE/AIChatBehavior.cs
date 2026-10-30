using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.CharacterDevelopment;
using TaleWorlds.Library;

namespace LocalAIChat
{
    public class ChatMsg
    {
        public string Role { get; set; }
        public string Content { get; set; }
    }

    /// <summary>
    /// Adds "I'd like to speak with you freely." to every hero conversation.
    /// Picking it closes the normal dialogue and opens a free-text chat with
    /// that character, answered by the local LLM. History is saved per hero
    /// inside your save file, so characters remember past talks.
    /// </summary>
    public class AIChatBehavior : CampaignBehaviorBase
    {
        private static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        private Dictionary<string, List<ChatMsg>> _history = new Dictionary<string, List<ChatMsg>>();
        private string _savedHistory = "";
        private Hero _pendingHero;
        private bool _busy;

        public override void RegisterEvents()
        {
            CampaignEvents.OnSessionLaunchedEvent.AddNonSerializedListener(this, OnSessionLaunched);
            CampaignEvents.ConversationEnded.AddNonSerializedListener(this, OnConversationEnded);
        }

        public override void SyncData(IDataStore dataStore)
        {
            if (dataStore.IsSaving)
                _savedHistory = Json.Serialize(_history);

            dataStore.SyncData("LocalAIChat_History", ref _savedHistory);

            if (dataStore.IsLoading)
            {
                try
                {
                    _history = string.IsNullOrEmpty(_savedHistory)
                        ? new Dictionary<string, List<ChatMsg>>()
                        : Json.Deserialize<Dictionary<string, List<ChatMsg>>>(_savedHistory);
                }
                catch
                {
                    _history = new Dictionary<string, List<ChatMsg>>();
                }
            }
        }

        // ---------- Dialogue hook ----------

        private void OnSessionLaunched(CampaignGameStarter starter)
        {
            starter.AddPlayerLine(
                "localai_start", "hero_main_options", "localai_ack",
                "I'd like to speak with you freely.",
                () => Hero.OneToOneConversationHero != null
                      && Hero.OneToOneConversationHero != Hero.MainHero
                      && !_busy,
                null, 90);

            starter.AddDialogLine(
                "localai_ack_line", "localai_ack", "close_window",
                "Very well. Speak your mind.",
                null,
                () => _pendingHero = Hero.OneToOneConversationHero,
                100);
        }

        private void OnConversationEnded(IEnumerable<CharacterObject> _)
        {
            if (_pendingHero == null) return;
            var hero = _pendingHero;
            _pendingHero = null;
            // Wait one tick so the conversation screen has fully closed.
            SubModule.RunOnMainThread(() => PromptPlayer(hero));
        }

        // ---------- Chat loop ----------

        private void PromptPlayer(Hero hero)
        {
            InformationManager.ShowTextInquiry(new TextInquiryData(
                hero.Name.ToString(),
                "What do you say?",
                true, true, "Say", "Leave",
                text =>
                {
                    if (!string.IsNullOrWhiteSpace(text)) Send(hero, text.Trim());
                },
                null), true);
        }

        private void Send(Hero hero, string playerText)
        {
            _busy = true;
            InformationManager.DisplayMessage(new InformationMessage(
                $"{hero.Name} is thinking…", new Color(0.7f, 0.7f, 0.7f)));

            // Read game state here, on the game thread, before going async.
            var messages = BuildMessages(hero, playerText);

            Task.Run(async () =>
            {
                try
                {
                    var reply = await LlmClient.ChatAsync(messages).ConfigureAwait(false);
                    SubModule.RunOnMainThread(() => OnReply(hero, playerText, reply));
                }
                catch (Exception e)
                {
                    SubModule.RunOnMainThread(() => OnError(hero, e));
                }
            });
        }

        private void OnReply(Hero hero, string playerText, string reply)
        {
            _busy = false;
            if (string.IsNullOrWhiteSpace(reply)) reply = "…";

            var log = GetLog(hero);
            log.Add(new ChatMsg { Role = "user", Content = playerText });
            log.Add(new ChatMsg { Role = "assistant", Content = reply });
            var max = Math.Max(2, ModConfig.HistoryTurns * 2);
            if (log.Count > max) log.RemoveRange(0, log.Count - max);

            InformationManager.ShowInquiry(new InquiryData(
                hero.Name.ToString(),
                reply,
                true, true, "Reply", "Leave",
                () => PromptPlayer(hero),
                null), true);
        }

        private void OnError(Hero hero, Exception e)
        {
            _busy = false;
            var msg = e is TaskCanceledException ? "The request timed out." : e.GetBaseException().Message;
            InformationManager.ShowInquiry(new InquiryData(
                "Local AI Chat",
                $"Couldn't reach the AI server at {ModConfig.Endpoint}\n\n{msg}\n\nIs llama-server running?",
                true, true, "Try again", "Leave",
                () => PromptPlayer(hero),
                null), true);
        }

        private List<ChatMsg> GetLog(Hero hero)
        {
            if (!_history.TryGetValue(hero.StringId, out var log))
            {
                log = new List<ChatMsg>();
                _history[hero.StringId] = log;
            }
            return log;
        }

        // ---------- Prompt ----------

        private List<Dictionary<string, string>> BuildMessages(Hero hero, string playerText)
        {
            var list = new List<Dictionary<string, string>>
            {
                Msg("system", BuildSystemPrompt(hero))
            };
            foreach (var m in GetLog(hero))
                list.Add(Msg(m.Role, m.Content));
            list.Add(Msg("user", playerText));
            return list;
        }

        private static Dictionary<string, string> Msg(string role, string content) =>
            new Dictionary<string, string> { ["role"] = role, ["content"] = content };

        private static string BuildSystemPrompt(Hero hero)
        {
            var player = Hero.MainHero;
            var sb = new StringBuilder();

            sb.AppendLine($"You are {hero.Name}, a character living in Calradia, the world of Mount & Blade II: Bannerlord.");
            sb.AppendLine("Stay fully in character at all times. Never mention being an AI, a model, or a game.");
            sb.AppendLine($"You are {(hero.IsFemale ? "a woman" : "a man")} of {(int)hero.Age} years, of {hero.Culture?.Name} culture.");

            if (hero.Clan != null)
                sb.AppendLine($"You belong to the {hero.Clan.Name} clan{(hero.Clan.Leader == hero ? ", which you lead" : "")}.");
            if (hero.MapFaction != null && hero.MapFaction != hero.Clan)
                sb.AppendLine($"Your clan serves the {hero.MapFaction.Name}.");
            if (hero.IsPlayerCompanion)
                sb.AppendLine($"You are a companion riding with {player.Name}.");
            if (hero.CurrentSettlement != null)
                sb.AppendLine($"You are currently in {hero.CurrentSettlement.Name}.");

            var traits = DescribeTraits(hero);
            if (traits.Length > 0) sb.AppendLine("Your temperament: " + traits + ".");

            var bio = hero.EncyclopediaText?.ToString();
            if (!string.IsNullOrWhiteSpace(bio)) sb.AppendLine("Your background: " + bio);

            sb.AppendLine();
            sb.Append($"You are speaking with {player.Name} of the {Clan.PlayerClan?.Name} clan");
            if (player.MapFaction != null && player.MapFaction != Clan.PlayerClan)
                sb.Append($", who serves the {player.MapFaction.Name}");
            sb.AppendLine(".");

            if (hero.Spouse == player) sb.AppendLine($"{player.Name} is your spouse.");
            sb.AppendLine($"Your feelings toward them: {DescribeRelation((int)hero.GetRelationWithPlayer())}.");

            if (hero.MapFaction != null && player.MapFaction != null
                && FactionManager.IsAtWarAgainstFaction(hero.MapFaction, player.MapFaction))
                sb.AppendLine("Your factions are at war with each other.");

            sb.AppendLine();
            sb.AppendLine("Reply only with what you say aloud, in 1 to 4 sentences, in a grounded medieval voice. " +
                          "Brief gestures in *asterisks* are allowed. Don't invent game events you couldn't know about.");

            if (!string.IsNullOrWhiteSpace(ModConfig.ExtraSystemPrompt))
                sb.AppendLine(ModConfig.ExtraSystemPrompt);

            return sb.ToString();
        }

        private static string DescribeRelation(int r)
        {
            if (r >= 60) return "deep trust and affection";
            if (r >= 30) return "warm and friendly";
            if (r >= 10) return "mildly positive";
            if (r > -10) return "neutral, you barely know them";
            if (r > -30) return "wary and cool";
            if (r > -60) return "hostile";
            return "bitter hatred";
        }

        private static string DescribeTraits(Hero hero)
        {
            var parts = new List<string>();
            void Add(TraitObject t, string high, string low)
            {
                var lvl = hero.GetTraitLevel(t);
                if (lvl > 0) parts.Add(lvl > 1 ? "very " + high : high);
                else if (lvl < 0) parts.Add(lvl < -1 ? "very " + low : low);
            }
            Add(DefaultTraits.Honor, "honorable", "deceitful");
            Add(DefaultTraits.Mercy, "merciful", "cruel");
            Add(DefaultTraits.Valor, "brave", "cautious");
            Add(DefaultTraits.Generosity, "generous", "greedy");
            Add(DefaultTraits.Calculating, "calculating", "impulsive");
            return string.Join(", ", parts);
        }
    }
}
