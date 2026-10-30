using System;
using System.Collections.Concurrent;
using TaleWorlds.CampaignSystem;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace LocalAIChat
{
    public class SubModule : MBSubModuleBase
    {
        // The game's UI is not thread-safe. HTTP replies arrive on a background
        // thread, so they are queued here and run on the game thread each tick.
        private static readonly ConcurrentQueue<Action> MainThreadQueue = new ConcurrentQueue<Action>();

        public static void RunOnMainThread(Action action) => MainThreadQueue.Enqueue(action);

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();
            ModConfig.Load();
        }

        protected override void OnBeforeInitialModuleScreenSetAsRoot()
        {
            base.OnBeforeInitialModuleScreenSetAsRoot();
            InformationManager.DisplayMessage(new InformationMessage(
                "[Local AI Chat] loaded. Endpoint: " + ModConfig.Endpoint, new Color(0.5f, 0.8f, 1f)));
        }

        protected override void OnGameStart(Game game, IGameStarter gameStarterObject)
        {
            base.OnGameStart(game, gameStarterObject);
            if (game.GameType is Campaign && gameStarterObject is CampaignGameStarter starter)
            {
                starter.AddBehavior(new AIChatBehavior());
            }
        }

        protected override void OnApplicationTick(float dt)
        {
            base.OnApplicationTick(dt);
            while (MainThreadQueue.TryDequeue(out var action))
            {
                try
                {
                    action();
                }
                catch (Exception e)
                {
                    InformationManager.DisplayMessage(new InformationMessage(
                        "[Local AI Chat] " + e.Message, new Color(1f, 0.3f, 0.3f)));
                }
            }
        }
    }
}
