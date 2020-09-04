namespace Blathers
{
    using System;
    using Eco.Core.Plugins.Interfaces;
    using Eco.Core.Utils;
    using Eco.Gameplay.Systems.Chat;
    using Eco.Shared.Services;

    public class Blathers : IModKitPlugin, IInitializablePlugin
    {
        public string GetStatus()
        {
            return String.Empty;
        }

        public void Initialize(TimedTask timer)
        {
            ChatManager.ServerMessageToAllLoc($"Woo hoo! Blathers is here. Version 0.1, I see.", DefaultChatTags.General);
        }
    }
}
