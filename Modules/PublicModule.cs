using System;
using System.Threading.Tasks;
using Discord;
using Eco.Core.Utils;
using Eco.Gameplay.Players;
using Eco.Shared.Utils;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using Discord.Commands;
using Eco.Gameplay.Items;
using Eco.Gameplay.Systems.Tooltip;
using Eco.Shared.Items;
using System.Text;
using Eco.Shared.Localization;
using Eco.Gameplay.Utils;

namespace Crossing.Modules
{
    // Modules must be public and inherit from an IModuleBase
    public class PublicModule : ModuleBase<SocketCommandContext>
    {
        private static IdentityManager _identity;
        
        public static void Initialize(IServiceProvider services)
        {
            _identity = services.GetRequiredService<IdentityManager>();
        }

        [Command("item")]
        public async Task ItemAsync(
            [Remainder]
            string itemName
        )
        {
            string steamId = _identity.DiscordToSteam.GetOrDefault($"{Context.User.Id}");
            User user = UserManager.FindUserBySteamId(steamId);
            if (user == null)
            {
                await ReplyAsync($"User is not linked");
                return;
            }

            Item item = CommandsUtil.ClosestMatchingEntity(user.Player, itemName, Item.AllItems, (Item x) => x.GetType().Name, (Item x) => x.DisplayName);
			if (item == null)
			{
                await ReplyAsync($"No item found matching `{itemName}`");
				return;
			}

            List<TooltipSection> sections = new List<TooltipSection>();
            AddItemTooltipSections(sections, item, user.Player);

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(item.DisplayDescription);
            stringBuilder.AppendLine("");

			TooltipSection section = item.CraftingRequirementsTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("**Crafting requirements**");
                stringBuilder.AppendLine(section.Content.ToString().StripTags());
            }

            section = item.UsedInTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("**Used by**");
                stringBuilder.AppendLine(section.Content.ToString().StripTags());
            }

            section = item.SellItTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("**Buyers**");
                stringBuilder.AppendLine(section.Content.ToString().StripTags());
            }
            
            section = item.BuyItTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("**Sellers**");
                stringBuilder.AppendLine(section.Content.ToString().StripTags());
            }

            section = item.SourceSpeciesTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("**Dropped from**");
                stringBuilder.AppendLine(section.Content.ToString().StripTags());
            }

            var embed = new EmbedBuilder
            {
                Title = item.DisplayName,
                Description = stringBuilder.ToString()
            };

            await ReplyAsync(
                "",
                false,
                embed.Build()
            );
        }

        private TooltipContext TooltipContext(Player player)
        {
            return new TooltipContext
            {
                Player = player,
                Origin = TooltipOrigin.None
            };
        }

        private void AddItemTooltipSections(System.Collections.Generic.ICollection<TooltipSection> sections, Item item, Player player)
        {
            /*
			sections.Add(item.SourceSpeciesTooltip(context));
			sections.Add(item.BuyItTooltip(context));
			sections.Add(item.SellItTooltip(context));
			sections.Add(item.UsedInTooltip(context));
            */
        }
    }
}