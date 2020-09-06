using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Eco.Core.IoC;
using Eco.Core.Utils;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Chat;
using Eco.Gameplay.Systems.TextLinks;
using Eco.Shared.Localization;
using Eco.Shared.Services;
using Eco.Shared.Utils;
using Eco.Simulation.Time;
using Microsoft.Extensions.DependencyInjection;
using Eco.Gameplay.Items;
using Eco.Gameplay.Systems.Tooltip;
using System.Collections.Generic;

namespace Crossing
{
    public class Blathers
    {
        private static Guild Guild => AutoSingleton<Guild>.Obj;

        private readonly IdentityManager _identity;

        private DiscordSocketClient _client;

        private IMessageChannel _debug;

        private SocketGuild _guild;

        public SocketGuild SocketGuild()
        {
            return _guild;
        }

        private static bool _ready;
        public bool Ready()
        {
            return _ready;
        }

        public Blathers(IServiceProvider services)
        {
            _identity = services.GetRequiredService<IdentityManager>();
        }

        public async Task InitializeAsync(DiscordSocketClient discord)
        {
            Log.WriteLine(Localizer.Do($"Blathers initializing."));

            _client = discord;
            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.MessageReceived += MessageReceivedAsync;

            try
            {
                await _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BLATHERS_TOKEN"));
                await _client.StartAsync();
                Log.WriteLine(Localizer.Do($"[BLATHERS] Succesfully logged in to Discord"));
            } catch (Exception e)
            {
                Log.WriteLine(Localizer.Do($"[BLATHERS] Error logging in to Discord {e.ToString()}"));
            }
        }

        private Task LogAsync(LogMessage msg)
        {
            Log.WriteLine(Localizer.Do($"[BLATHERS] {msg.ToString()}"));
            return Task.CompletedTask;
        }

        private async Task ReadyAsync()
        {
            _debug = (IMessageChannel)_client.GetChannel(Guild.DebugChannel);
            if (_debug == null)
            {
                throw new Exception("[BLATHERS] Could not find channel");
            }
            
            _guild = _client.GetGuild(Guild.Id);
            if (_guild == null)
            {
                throw new Exception("[BLATHERS] Could not find guild");
            }

            _ready = true;
            await _debug.SendMessageAsync("Blathers connected.");
        }

        private Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot || message.Author.IsWebhook)
            {
                return Task.CompletedTask;
            }

            switch (message.Channel.Name)
            {
                case "general":
                    HandleGeneral(message);
                    break;
            }

            return Task.CompletedTask;
        }

        private void HandleGeneral(SocketMessage message)
        {
            string discordId = $"{message.Author.Id}";
            string steamId = _identity.DiscordToSteam.GetOrDefault(discordId);

            var user = UserManager.FindUserBySteamId(steamId);
            if (user != null)
            {
                Log.WriteLine(Localizer.Do($"Discord->ECO {message.Author.Username}: {message.Content}"));
                ChatMessage msg = new ChatMessage
                {
                    Tag = DefaultChatTags.General.TagName(),
                    Sender = user.Name,
                    SenderUILink = user.UILink(),
                    Text = message.Content,
                    TimeSeconds = WorldTime.Seconds,
                    Category = MessageCategory.Chat
                };
                ServiceHolder<IChatManager>.Obj.ProcessMessage(msg);
            }
            else
            {
                Log.WriteLine(Localizer.Do($"[BLATHERS] Did not find user with SteamId {steamId}"));
            }
        }
    }
}