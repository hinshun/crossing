namespace Crossing
{
    using System;
    using System.Diagnostics;
    using Eco.Core.Plugins.Interfaces;
    using Eco.Core.Utils;
    using Eco.Gameplay.GameActions;
    using Eco.Gameplay.Systems.Chat;
    using Eco.Shared.Services;

    public class Crossing : IModKitPlugin, IInitializablePlugin
    {
        private Boolean _started = false;
        public string GetStatus()
        {
            if (!_started) {
                setup();
                _started = true;
            }
            return String.Empty;
        }

        public void Initialize(TimedTask timer)
        {
            ChatManager.ServerMessageToAllLoc($"Crossing is initialized! Version 0.1", DefaultChatTags.General);
        }

        public void setup() {
            var listener = new Listener();
            ActionUtil.AddListener(listener);
        }
    }

    public class Listener : IGameActionAware
    {
        public void ActionPerformed(GameAction action) {
        }

        public Result ShouldOverrideAuth(GameAction action) {
            throw new NotImplementedException();
        }
    }
}
