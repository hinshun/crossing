using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Webhook;
using Discord.WebSocket;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Chat;
using Eco.Shared.Localization;
using Eco.Shared.Services;
using Eco.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using Crossing.Services;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Crossing
{
    public class Crossing : IModKitPlugin, IInitializablePlugin
    {
        private static Guild Guild => AutoSingleton<Guild>.Obj;

        public string GetStatus()
        {
            return String.Empty;
        }

        public void Initialize(TimedTask timer)
        {
            DiscordSocketClient blatherDiscord = new DiscordSocketClient();
            DiscordSocketClient isabelleDiscord = new DiscordSocketClient();
            var discord = new DiscordSDK.Discord(Guild.IsabelleClientId, (UInt64)DiscordSDK.CreateFlags.Default);

            using (var services = ConfigureServices())
            {
                services.GetRequiredService<IdentityManager>().Initialize();
                services.GetRequiredService<CommandHandlingService>().InitializeAsync(blatherDiscord).Wait();
                services.GetRequiredService<Blathers>().InitializeAsync(blatherDiscord).Wait();
                services.GetRequiredService<Isabelle>().InitializeAsync(isabelleDiscord).Wait();
                EcoCommands.Initialize(services);

                var relay = new ChatRelay(services);
                ActionUtil.AddListener(relay);
            }

            Log.WriteLine(Localizer.Do($"Crossing is initialized!"));
            ChatManager.ServerMessageToAllLoc($"Crossing is initialized! Version 0.1", DefaultChatTags.General);
        }

        private ServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddSingleton<CommandService>()
                .AddSingleton<CommandHandlingService>()
                .AddSingleton<IdentityManager>()
                .AddSingleton<Blathers>()
                .AddSingleton<Isabelle>()
                .BuildServiceProvider();
        }
    }

    public class ChatRelay : IGameActionAware
    {
        private static Guild Guild => AutoSingleton<Guild>.Obj;
        private readonly IdentityManager _identity;
        private readonly Blathers _blathers;
        private DiscordWebhookClient _webhookClient;

        public ChatRelay(IServiceProvider services)
        {
            _identity = services.GetRequiredService<IdentityManager>();
            _blathers = services.GetRequiredService<Blathers>();
            _webhookClient = new DiscordWebhookClient(
                $"https://discordapp.com/api/webhooks/{Guild.Application}/{Environment.GetEnvironmentVariable("WEBHOOK_TOKEN")}"
            );
        }

        public void ActionPerformed(GameAction action)
        {
            switch (action)
            {
                case ChatSent chat:
                    HandleChatSent(chat).Wait();
                    break;
                default:
                    break;
            }
        }

        private async Task HandleChatSent(ChatSent chat)
        {
            string discordId = _identity.SteamToDiscord.GetOrDefault(chat.Citizen.SteamId);
            if (discordId == "")
            {
                return;
            }

            ulong id = Convert.ToUInt64(discordId);
            SocketUser discordUser = _blathers.SocketGuild().GetUser(id);
            if (discordUser == null)
            {
                return;
            }

            Log.WriteLine(Localizer.Do($"ECO->Discord {discordUser.Username}: {chat.Message}"));
            await _webhookClient.SendMessageAsync($"{chat.Message}", false, null, discordUser.Username, Guild.EcoGlobeAvatar);
        }

        public Result ShouldOverrideAuth(GameAction action)
        {
            return Result.FailedNoMessage;
        }
    }

    public class Guild : AutoSingleton<Guild>
    {
        public ulong Id
        {
            get;
            set;
        } = 750916474930987083;

        public ulong Application
        {
            get;
            set;
        } = 751573692974366762;

        public ulong GeneralChannel
        {
            get;
            set;
        } = 750916474930987086;

        public ulong DebugChannel
        {
            get;
            set;
        } = 751532932828496033;

        public string EcoGlobeAvatar
        {
            get;
            set;
        } = "https://i.imgur.com/qwPbkaW.png";

        public long IsabelleClientId
        {
            get;
            set;
        } = 751263733220900904;
    }

    public class EcoCommands : IChatCommandHandler
    {
        private static IdentityManager _identity;

        private static Blathers _blathers;

        public static void Initialize(IServiceProvider services)
        {
            _identity = services.GetRequiredService<IdentityManager>();
            _blathers = services.GetRequiredService<Blathers>();
        }

        [ChatCommand("Link your discord account.")]
        public static void LinkDiscord(User user, string discordId)
        {
            ulong id = Convert.ToUInt64(discordId);
            SocketUser discordUser = _blathers.SocketGuild().GetUser(id);
            if (discordUser == null)
            {
                user.Player.MsgLoc($"We couldn't find a discord user with id {id}");
                return;
            }

            _identity.SteamToDiscord[user.SteamId] = discordId;
            _identity.DiscordToSteam[discordId] = user.SteamId;
            _identity.Save();

            Notify(user, discordUser).Wait();

            user.Player.MsgLoc($"You have been successfully linked with {discordUser.Username}!");
        }

        private static async Task Notify(User user, SocketUser discordUser)
        {
            IDMChannel channel = await discordUser.GetOrCreateDMChannelAsync();
            await channel.SendMessageAsync($"You have been successfully linked with {user.Name}!");
        }
    }

    public class IdentityManager
    {
        public Dictionary<string, string> SteamToDiscord { get; set; }

        public Dictionary<string, string> DiscordToSteam { get; set; }

        public IdentityManager()
        {
            SteamToDiscord = new Dictionary<string, string>();
            DiscordToSteam = new Dictionary<string, string>();
        }

        public void Initialize()
        {
            string steam2discord = File.ReadAllText("/app/Mods/Crossing/steam2discord.json");
            SteamToDiscord = JsonConvert.DeserializeObject<Dictionary<string, string>>(steam2discord);
            Log.WriteLine(Localizer.Do($"Found {SteamToDiscord.Count} in SteamToDiscord"));

            string discord2steam = File.ReadAllText("/app/Mods/Crossing/discord2steam.json");
            DiscordToSteam = JsonConvert.DeserializeObject<Dictionary<string, string>>(discord2steam);
            Log.WriteLine(Localizer.Do($"Found {DiscordToSteam.Count} in DiscordToSteam"));
        }

        public void Save()
        {
            string steam2discord = JsonConvert.SerializeObject(SteamToDiscord, Formatting.Indented);
            File.WriteAllText("/app/Mods/Crossing/steam2discord.json", steam2discord); 

            string discord2steam = JsonConvert.SerializeObject(DiscordToSteam, Formatting.Indented);
            File.WriteAllText("/app/Mods/Crossing/discord2steam.json", discord2steam); 
        }
    }
}