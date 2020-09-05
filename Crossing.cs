using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
using Discord.Webhook;
using Discord.WebSocket;
using Eco.Core.IoC;
using Eco.Core.Plugins.Interfaces;
using Eco.Core.Utils;
using Eco.Gameplay.GameActions;
using Eco.Gameplay.Players;
using Eco.Gameplay.Systems.Chat;
using Eco.Gameplay.Systems.TextLinks;
using Eco.Shared.Localization;
using Eco.Shared.Services;
using Eco.Shared.Utils;
using Eco.Simulation.Time;
using Microsoft.Extensions.DependencyInjection;
using Crossing.Services;
using Eco.Gameplay.Items;
using Eco.Gameplay.Systems.Tooltip;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Crossing
{
    public class Crossing : IModKitPlugin, IInitializablePlugin
    {
        public string GetStatus()
        {
            return String.Empty;
        }

        public void Initialize(TimedTask timer)
        {
            var relay = new ChatRelay();
            ActionUtil.AddListener(relay);

            using (var services = ConfigureServices())
            {
                services.GetRequiredService<CommandHandlingService>().InitializeAsync().Wait();
                services.GetRequiredService<IdentityManager>().InitializeAsync().Wait();
                services.GetRequiredService<Blathers>().InitializeAsync().Wait();
                EcoCommands.Initialize(services);
            }

            ChatManager.ServerMessageToAllLoc($"Crossing is initialized! Version 0.1", DefaultChatTags.General);
        }

        private ServiceProvider ConfigureServices()
        {
            return new ServiceCollection()
                .AddSingleton<DiscordSocketClient>()
                .AddSingleton<CommandService>()
                .AddSingleton<CommandHandlingService>()
                .AddSingleton<IdentityManager>()
                .AddSingleton<Blathers>()
                .BuildServiceProvider();
        }
    }

    public class ChatRelay : IGameActionAware
    {
        private static Guild Guild => AutoSingleton<Guild>.Obj;
        private DiscordWebhookClient _webhookClient;

        public ChatRelay()
        {
            _webhookClient = new DiscordWebhookClient(
                $"https://discordapp.com/api/webhooks/{Guild.Application}/{Environment.GetEnvironmentVariable("WEBHOOK_TOKEN")}"
            );
        }

        public void ActionPerformed(GameAction action)
        {
            switch (action)
            {
                case ChatSent chat:
                    if (chat.Tag == "discord")
                    {
                        break;
                    }
                    Log.WriteLine(Localizer.Do($"ECO->Discord {chat.Citizen.Name}: {chat.Message}"));
                    _webhookClient.SendMessageAsync($"{chat.Message}", false, null, chat.Citizen.Name, Guild.EcoGlobeAvatar).Wait();
                    break;
                default:
                    break;
            }
        }

        public Result ShouldOverrideAuth(GameAction action)
        {
            return Result.FailedNoMessage;
        }
    }

    public class Guild : AutoSingleton<Guild>
    {
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

        public ulong BlathersChannel
        {
            get;
            set;
        } = 751532932828496033;

        public string EcoGlobeAvatar
        {
            get;
            set;
        } = "https://i.imgur.com/qwPbkaW.png";
    }

    public class Blathers
    {
        private static Guild Guild => AutoSingleton<Guild>.Obj;

        private static IdentityManager _identity;

        private static DiscordSocketClient _client;

        private static IMessageChannel _debug;

        private static bool _ready;
        public bool Ready()
        {
            return _ready;
        }

        public Blathers(IServiceProvider services)
        {
            _identity = services.GetRequiredService<IdentityManager>();
            _client = services.GetRequiredService<DiscordSocketClient>();
        }

        public async Task InitializeAsync()
        {
            await _client.LoginAsync(TokenType.Bot, Environment.GetEnvironmentVariable("BLATHERS_TOKEN"));
            await _client.StartAsync();

            _client.Ready += ClientReady;
            _client.MessageReceived += MessageReceivedAsync;
        }

        private async Task ClientReady()
        {
            _debug = (IMessageChannel)_client.GetChannel(Guild.BlathersChannel);
            await _debug.SendMessageAsync("Blathers connected.");
            _ready = true;
        }

        private async Task MessageReceivedAsync(SocketMessage message)
        {
            if (message.Author.IsBot || message.Author.IsWebhook)
            {
                return;
            }

            switch (message.Channel.Name)
            {
                case "general":
                    HandleGeneral(message);
                    break;
            }
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
        }

        public void Debug(string msg)
        {
            if (!_ready)
            {
                return;
            }
            Log.WriteLine(Localizer.Do($"[Blathers] ${msg}"));
        }
    }

    public class EcoCommands : IChatCommandHandler
    {
        private static IdentityManager _identity;

        private static DiscordSocketClient _client;

        public static void Initialize(IServiceProvider services)
        {
            _identity = services.GetRequiredService<IdentityManager>();
            _client = services.GetRequiredService<DiscordSocketClient>();
        }

        [ChatCommand("Link your discord account.")]
        public static void LinkDiscord(User user, string discordId)
        {
            ulong id = Convert.ToUInt64(discordId);
            SocketUser discordUser = _client.GetUser(id);
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

        public async Task InitializeAsync()
        {
            string steam2discord = File.ReadAllText("/app/Mods/Crossing/steam2discord.json");
            SteamToDiscord = JsonConvert.DeserializeObject<Dictionary<string, string>>(steam2discord);

            string discord2steam = File.ReadAllText("/app/Mods/Crossing/discord2steam.json");
            DiscordToSteam = JsonConvert.DeserializeObject<Dictionary<string, string>>(steam2discord);
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