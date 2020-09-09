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
using Eco.Gameplay.Skills;
using System.Linq;
using System.IO;
using System.Diagnostics;

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

        [Command("Help")]
        public async Task HelpAsync()
        {
            StringBuilder stringBuilder = new StringBuilder();

            stringBuilder.AppendLine("`!item <Item Name>` - Queries the item's crafting requirements, buyers, sellers, and items that uses it.");
            stringBuilder.AppendLine("`!skilltree` - Fetches a visualization of the skill tree.");

            await ReplyAsync(stringBuilder.ToString());
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
                await ReplyAsync($"Your ECO user must be linked to discord to use this command.");
                return;
            }

            Item item = CommandsUtil.ClosestMatchingEntity(user.Player, itemName, Item.AllItems, (Item x) => x.GetType().Name, (Item x) => x.DisplayName);
			if (item == null)
			{
                await ReplyAsync($"Could not find skill tree matching `{itemName}`.");
				return;
			}

            List<TooltipSection> sections = new List<TooltipSection>();

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.AppendLine(item.DisplayDescription);

			TooltipSection section = item.CraftingRequirementsTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("");
                stringBuilder.AppendLine("**Crafting requirements:**");
                stringBuilder.AppendLine(section.Content.ToString().StripTags());
            }

            section = item.UsedInTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("");
                stringBuilder.AppendLine("**Used by:**");
                stringBuilder.AppendLine(section.Content.ToString().StripTags());
            }

            section = item.SellItTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("");
                stringBuilder.AppendLine("**Buyers:**");
                stringBuilder.AppendLine(section.Content.ToString().StripTags());
            }
            
            section = item.BuyItTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("");
                stringBuilder.AppendLine("**Sellers:**");
                stringBuilder.AppendLine(section.Content.ToString().StripTags());
            }

            section = item.SourceSpeciesTooltip(TooltipContext(user.Player));
            if (section != null && section.Content != null && section.Content.ToString().StripTags().Length != 0)
            {
                stringBuilder.AppendLine("");
                stringBuilder.AppendLine("**Dropped from:**");
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

        [Command("skilltree")]
        public async Task SkillTreeAsync()
        {
            string dotPng = SkillTreeVisualizer.BuildAll();
            FileStream dotStream = File.Open(dotPng, FileMode.Open);

            var embed = new EmbedBuilder()
            {
                Title = "Skill Tree",
                ImageUrl = $"attachment://skilltree.png"
            };

            await Context.Channel.SendFileAsync(
                dotStream,
                "skilltree.png",
                "",
                false,
                embed.Build()
            );

            File.Delete(dotPng);
        }
    }

    public class SkillTreeVisualizer
    {
        public static string BuildAll()
		{
			string str = "digraph{\r\nrankdir=LR;\r\n";
            foreach(SkillTree skillTree in SkillTree.RootSkillTrees)
            {
                str += GetSkillGraphEntries(skillTree);
            }
			str += "}\r\n";
			return MakeImage(str);
		}

		private static string GetSkillGraphEntries(SkillTree skill)
		{
			string text = "<<table border=\"0\" cellspacing=\"0\" cellpadding=\"0\">" + $"<tr><td bgcolor=\"Yellow\"><B>{skill.StaticSkill.DisplayName}</B></td></tr>" + "</table>>";
			string empty = string.Empty;
			string text2 = $"\"{skill.StaticSkill.DisplayName}\"";
			empty = empty + text2 + "[label=" + text + " shape=\"record\"]\r\n";
			empty += $"\"{skill.StaticSkill.DisplayName}\" -> {{{Enumerable.Select(skill.Children, (SkillTree c) => $"\"{c.StaticSkill.DisplayName}\"").CommaList()}}};\r\n";
			return empty + Enumerable.Select(skill.Children, (SkillTree child) => GetSkillGraphEntries(child)).TextList();
		}

		private static string MakeImage(string s)
		{
            string dotFile = $"/tmp/{StringUtils.RandomString(10)}.dot";
			File.WriteAllText(dotFile, s);

            string dotPng = $"/tmp/{StringUtils.RandomString(10)}.png";

			Process process = new Process();
			process.StartInfo.UseShellExecute = false;
			process.StartInfo.RedirectStandardOutput = true;
			process.StartInfo.FileName = "/usr/bin/dot";
			process.StartInfo.Arguments = $"{dotFile} -Tpng -o {dotPng}";
			process.Start();

			string text2 = process.StandardOutput.ReadToEnd();
			process.WaitForExit();

            return dotPng;
		}
    }
}