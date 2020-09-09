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
using System.Text.RegularExpressions;
using System.Text;

namespace Crossing
{
    public class Blathers
    {
        private static Guild Guild => AutoSingleton<Guild>.Obj;

        private readonly IdentityManager _identity;

        private readonly Regex _mentionRegex;

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
            _mentionRegex = new Regex(@"<[@#!&]*(:[^:]*:)?([^>]*)>");
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

            if (Environment.GetEnvironmentVariable("STAGING") == "1")
            {
                if (message.Channel.Name == "staging")
                {
                    HandleGeneral(message);
                }
            } 
            else {
                if (message.Channel.Name == "general")
                {
                    HandleGeneral(message);
                }
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
                StringBuilder content = new StringBuilder();
                int curr = 0;
                foreach (Match match in _mentionRegex.Matches(message.Content))
                {
                    content.Append(message.Content.Substring(curr, match.Index - curr));
                    curr = match.Index + match.Length;

                    // Handle emotes
                    if (match.Groups[1].Value.Length > 2)
                    {
                        content.Append(match.Groups[1].Value);
                        continue;
                    }

                    // Handle mentions
                    string value = match.Groups[2].Value;
                    ulong snowflakeId = Convert.ToUInt64(value);

                    bool matched = false;
                    foreach (SocketUser mention in message.MentionedUsers)
                    {
                        if (mention.Id == snowflakeId)
                        {
                            string mentionSteamId = _identity.DiscordToSteam.GetOrDefault(value);
                            User ecoMention = UserManager.FindUserBySteamId(mentionSteamId);
                            if (ecoMention == null)
                            {
                                content.Append($"@{mention.Username}");
                            }
                            else
                            {
                                content.Append($"{ecoMention.UILink()}");
                            }

                            matched = true;
                            break;
                        }
                    }

                    if (!matched)
                    {
                        foreach (SocketRole mention in message.MentionedRoles)
                        {
                            if (mention.Id == snowflakeId)
                            {
                                content.Append($"@{mention.Name}");
                                matched = true;
                                break;
                            }
                        }
                    }

                    if (!matched)
                    {
                        foreach (SocketGuildChannel mention in message.MentionedChannels)
                        {
                            if (mention.Id == snowflakeId)
                            {
                                content.Append($"#{mention.Name}");
                                matched = true;
                                break;
                            }
                        }
                    }
                }
                content.Append(message.Content.Substring(curr, message.Content.Length - curr));

                Log.WriteLine(Localizer.Do($"Discord->ECO {message.Author.Username}: {content.ToString()}"));
                ChatMessage msg = new ChatMessage
                {
                    Tag = DefaultChatTags.General.TagName(),
                    Sender = user.Name,
                    SenderUILink = user.UILink(),
                    Text = content.ToString(),
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