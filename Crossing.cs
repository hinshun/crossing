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
            DiscordSocketClient discord = new DiscordSocketClient();

            using (var services = ConfigureServices())
            {
                services.GetRequiredService<IdentityManager>().Initialize();
                services.GetRequiredService<CommandHandlingService>().InitializeAsync(discord).Wait();
                services.GetRequiredService<Blathers>().InitializeAsync(discord).Wait();
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
                    string discordId = _identity.SteamToDiscord.GetOrDefault(chat.Citizen.SteamId);
                    if (discordId == "")
                    {
                        break;
                    }

                    ulong id = Convert.ToUInt64(discordId);
                    SocketUser discordUser = _blathers.SocketGuild().GetUser(id);
                    if (discordUser == null)
                    {
                        break;
                    }

                    Log.WriteLine(Localizer.Do($"ECO->Discord {discordUser.Username}: {chat.Message}"));
                    _webhookClient.SendMessageAsync($"{chat.Message}", false, null, discordUser.Username, Guild.EcoGlobeAvatar).Wait();
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
                Log.WriteLine(Localizer.Do($"Succesfully logged in to Discord"));
            } catch (Exception e)
            {
                Log.WriteLine(Localizer.Do($"Error logging in to Discord {e.ToString()}"));
            }
        }

        private Task LogAsync(LogMessage msg)
        {
            Log.WriteLine(Localizer.Do($"[DISCORD] {msg.ToString()}"));
            return Task.CompletedTask;
        }

        private async Task ReadyAsync()
        {
            Log.WriteLine(Localizer.Do($"Blathers ready."));

            _debug = (IMessageChannel)_client.GetChannel(Guild.BlathersChannel);
            if (_debug == null)
            {
                throw new Exception("Could not find blathers channel");
            }
            
            _guild = _client.GetGuild(Guild.Id);
            if (_guild == null)
            {
                throw new Exception("Could not find guild");
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
        }
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
            if (_blathers == null)
            {
                user.Player.MsgLoc($"blathers is null");
                return;
            }
            if (_blathers.SocketGuild() == null)
            {
                user.Player.MsgLoc($"socket guild is null");
                return;
            }

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