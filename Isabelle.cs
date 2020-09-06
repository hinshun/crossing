using System;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Eco.Core.Utils;
using Eco.Gameplay.Players;
using Eco.Shared.Localization;
using Eco.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;

namespace Crossing
{
    public class Isabelle
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

        public Isabelle(IServiceProvider services)
        {
            _identity = services.GetRequiredService<IdentityManager>();
        }

        public async Task InitializeAsync(DiscordSocketClient discord)
        {
            _client = discord;
            _client.Log += LogAsync;
            _client.Ready += ReadyAsync;
            _client.MessageReceived += MessageReceivedAsync;

            try
            {
                await _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("ISABELLE_TOKEN"));
                await _client.StartAsync();
                Log.WriteLine(Localizer.Do($"[ISABELLE] Succesfully logged in to Discord"));
            } catch (Exception e)
            {
                Log.WriteLine(Localizer.Do($"[ISABELLE] Error logging in to Discord {e.ToString()}"));
            }
        }

        private Task LogAsync(LogMessage msg)
        {
            Log.WriteLine(Localizer.Do($"[ISABELLE] {msg.ToString()}"));
            return Task.CompletedTask;
        }

        private async Task ReadyAsync()
        {
            _debug = (IMessageChannel)_client.GetChannel(Guild.DebugChannel);
            if (_debug == null)
            {
                throw new Exception("[ISABELLE] Could not find channel");
            }
            
            _guild = _client.GetGuild(Guild.Id);
            if (_guild == null)
            {
                throw new Exception("[ISABELLE] Could not find guild");
            }

            _ready = true;
            await _debug.SendMessageAsync("Isabelle connected.");
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot || message.Author.IsWebhook)
            {
                return;
            }
            
            await HandleThankMention(message);
        }

        private async Task HandleThankMention(SocketMessage message)
        {
            IReadOnlyCollection<SocketUser> mentions = message.MentionedUsers;
            if (mentions.Count == 0) { return; }

            string[] thankMatches = {"thank", "ty", "thx", "appreciate", "cheers"};
            if (!message.Content.ToLower().ContainsAny(thankMatches)) { return; }

            string steamId = _identity.DiscordToSteam.GetOrDefault($"{message.Author.Id}");
            if (steamId == "") { return; }

            var user = UserManager.FindUserBySteamId(steamId);
            if (user == null || user.Player == null) { return; }

            foreach(SocketUser mention in mentions)
            {
                if (mention.Id == message.Author.Id)
                {
                    continue;
                }

                string mentionSteamId = _identity.DiscordToSteam.GetOrDefault($"{mention.Id}");
                if (mentionSteamId == "") { continue; }

                var mentionUser = UserManager.FindUserBySteamId(mentionSteamId);
                if (mentionUser == null) { continue; }
                
                user.Player.GiveReputationTo(mentionUser.Name, 1, message.Content);
            }

            await message.AddReactionAsync(new Emoji("🏅"));
        }
    }
}