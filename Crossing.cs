using System;
using System.IO;
using System.Threading.Tasks;
using Discord;
using Discord.Commands;
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
using Crossing.Modules;
using Eco.Gameplay.Skills;
using Eco.Gameplay.Utils;
using Eco.Gameplay.Items;
using System.ComponentModel;
using Eco.Core.Controller;
using Eco.Shared.Math;
using Eco.Gameplay.Plants;
using Eco.Simulation.Types;
using System.Linq;
using Eco.Simulation;
using Eco.Simulation.Agents;
using Eco.Simulation.WorldLayers;
using System.Text;
using Eco.Gameplay.Systems.Tooltip;
using Eco.Gameplay.Systems.TextLinks;
using Eco.Shared;
using Eco.Simulation.WorldLayers.Layers;
using Eco.Gameplay.Economy;
using Eco.Gameplay.Components;

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
            DiscordSocketClient blatherDiscord = new DiscordSocketClient();
            DiscordSocketClient isabelleDiscord = new DiscordSocketClient();

            using (var services = ConfigureServices())
            {
                services.GetRequiredService<IdentityManager>().Initialize();
                services.GetRequiredService<CommandHandlingService>().InitializeAsync(isabelleDiscord).Wait();
                services.GetRequiredService<Blathers>().InitializeAsync(blatherDiscord).Wait();
                services.GetRequiredService<Isabelle>().InitializeAsync(isabelleDiscord).Wait();
                PublicModule.Initialize(services);
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
    }

    public class EcoCommands : IChatCommandHandler
    {
        private static BankAccountManager BankAccountManager => Singleton<BankAccountManager>.Obj;
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

            user.Player?.MsgLoc($"You have been successfully linked with {discordUser.Username}!");
        }

        private static async Task Notify(User user, SocketUser discordUser)
        {
            IDMChannel channel = await discordUser.GetOrCreateDMChannelAsync();
            await channel.SendMessageAsync($"You have been successfully linked with {user.Name}!");
        }

        [ChatCommand("Refunds a skill point.")]
        public static void RefundSkill(User user, string skillName)
        {
            Skill skill = CommandsUtil.ClosestMatchingEntity(user.Player, skillName, user.Skillset.Skills, (Skill x) => x.Type.Name, (Skill x) => x.DisplayName);
			if (skill == null)
			{
				user.Player?.ErrorLoc($"{skillName} skill not found.");
			}
			else
			{
				Refund(user, skill.Type);
                user.Player?.MsgLoc($"{skillName} has been refunded.");
			}
        }

		public static void Refund(User user, Type skillType)
		{
            
            Skill skill = user.Skillset.GetSkill(skillType);
            skill.AbandonSpecialty(user.Player);
            ControllerExtensions.Changed(user.Skillset, new PropertyChangedEventArgs("Skills"));
		}

        [ChatCommand("Prints bells and adds it to the Treasury.", ChatAuthorizationLevel.Admin)]
        public static void PrintBells(User user, float amount)
        {
            Currency bells = null;
            foreach (Currency currency in CurrencyManager.Currencies)
            {
                if (currency.Name == "Bells")
                {
                    bells = currency;
                }
            }
            if (bells == null)
            {
                user.Player?.ErrorLoc($"Bells could not be found.");
            }

            BankAccount treasury = BankAccountManager.Treasury();
            treasury.AddCurrency(bells, amount);
            user.Player?.MsgLoc($"You successfully printed {bells.UILink(amount)}, the treasury now has {treasury.DisplayAmount(bells)} in account {treasury.UILink()}.");
			ChatManager.ServerMessageToAll(Localizer.Do($"{user.UILink()} printed {bells.UILink(amount)} for the treasury."), DefaultChatTags.Trades, MessageCategory.Info, new User[1]
			{
				user
			});
        }

        [ChatCommand("Shows useful farming information about a plant.")]
        public static void ShowPlant(User user)
        {
            WorldPosition3i worldPos = new WorldPosition3i(Eco.World.World.GetTopGroundPos(user.Position.XZi));
            Vector3i plantPos = Eco.World.World.GetTopGroundPos(worldPos.XZ) + Vector3i.Up;
            Plant plant = PlantBlock.GetPlant(plantPos);
            if (plant == null)
            {
                user.Player?.ErrorLoc($"No plant found beneath the player.");
                return;
            }
            PlantSpecies species = plant.Species;

            Vector2i xZi = user.Player.Position.XZi;

            float limitingYield = 1.0f;
            string limitingYieldFactor = "";

            float limitingGrowthRate = 1.0f;
            string limitingCapacityFactor = "";
            int maxNumPlant = 0;

            WorldLayer popLayer = WorldLayerManager.GetLayer(species.PopulationLayer);
            float popValue = popLayer.AverageOverBoundaryAlignedWorldPos(xZi);
            WorldArea area = popLayer.LayerPosToWorldArea(popLayer.WorldPosToLayerPos(xZi));
            IEnumerable<Plant> plants = (from p in EcoSim.PlantSim.PlantsInArea(area)
				where p.Alive && p.Species == species
				select p);
            int numPlant = plants.Count();

            WorldLayer plantLayer = (from layer in WorldLayerManager.Layers.OfType<PlantLayer>()
				where layer.VoxelsPerEntry == 5 && layer.Species == species
				select layer).First();

            float plantValue = plantLayer.AverageOverBoundaryAlignedWorldPos(xZi);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Showing farming information for a plant.");
            sb.AppendLine($"Species: {species.UILink()}");
            sb.AppendLine($"Maturity time: {species.MaturityAgeDays} days");
            sb.AppendLine($"Max yield: {species.ResourceRange.Max}");
            sb.AppendLine($"Plant layer: {plantValue:P}");

            sb.AppendLine("");
            sb.AppendLine("Current plant:");
            sb.AppendLine($"Growth percent: {plant.GrowthPercent:P}");
            sb.AppendLine($"Yield if harvested now: {CalculateResourceYield(plant)}");
            sb.AppendLine($"Population in plot: {numPlant} (Pop average: {popValue:P})");

            sb.AppendLine(""); 
            sb.AppendLine("Habitat:");

            float temperature = WorldLayerManager.GetLayer("Temperature").EntryWorldPos(xZi);
			float rainfall = WorldLayerManager.GetLayer("Rainfall").EntryWorldPos(xZi);
			float pollution = WorldLayerManager.GetLayer("GroundPollutionSpread").EntryWorldPos(xZi);
			float saltwater = WorldLayerManager.GetLayer("SaltWater").EntryWorldPos(xZi);

            float tempModifier = PlantSpecies.RangesToModifier(species.TemperatureExtremes, species.IdealTemperatureRange, temperature);
            if (tempModifier < limitingYield)
            {
                limitingYield = tempModifier;
                limitingYieldFactor = "Temperature";
            }
            sb.AppendLine($"- Habitat 'Temperature' ({temperature:P}) is constraining yield to = {tempModifier:P}.");

			float rainfallModifier = PlantSpecies.RangesToModifier(species.MoistureExtremes, species.IdealMoistureRange, rainfall);
            if (rainfallModifier < limitingYield)
            {
                limitingYield = rainfallModifier;
                limitingYieldFactor = "Rainfall";
            }
            sb.AppendLine($"- Habitat 'Rainfall' ({rainfall:P}) is constraining yield to = {rainfallModifier:P}.");

			float pollutionModifier = PlantSpecies.RangesToModifier(new Eco.Shared.Math.Range(0f, species.MaxPollutionDensity), new Eco.Shared.Math.Range(0f, species.PollutionDensityTolerance), pollution);
            if (pollutionModifier < limitingYield)
            {
                limitingYield = pollutionModifier;
                limitingYieldFactor = "Pollution";
            }
            sb.AppendLine($"- Habitat 'Pollution' ({pollution:P}) is constraining yield to = {pollutionModifier:P}.");

			float saltwaterModifier = PlantSpecies.RangesToModifier(species.WaterExtremes, species.IdealWaterRange, saltwater);
            if (saltwaterModifier < limitingYield)
            {
                limitingYield = saltwaterModifier;
                limitingYieldFactor = "Salt Water";
            }
            sb.AppendLine($"- Habitat 'Salt Water' ({saltwater:P}) is constraining yield to = {saltwaterModifier:P}.");
            sb.AppendLine(":");

            sb.AppendLine("Resource Constraints:");
            foreach (System.Collections.Generic.KeyValuePair<string, PlantSpecies.ResourceConstraint> item in species.ResourceConstraintsByLayer.Value)
			{
                item.Deconstruct(out string _, out PlantSpecies.ResourceConstraint value);
                float resourceConcentration = WorldLayerManager.GetLayer(value.LayerName).AverageOverBoundaryAlignedWorldPos(xZi);
                float modifier = value.Habitability(resourceConcentration);
                if (modifier < limitingYield)
                {
                    limitingYield = modifier;
                    limitingYieldFactor = value.LayerName;
                }
                sb.AppendLine($"- Resource '{value.LayerName}' ({resourceConcentration:P}) is constraining yield to = {modifier:P}.");
            }
            sb.AppendLine("");

            sb.AppendLine("Capacity Constraints:");
            foreach (System.Collections.Generic.KeyValuePair<string, PlantSpecies.CapacityConstraint> item in species.CapacityConstraintsByLayer.Value)
			{
				item.Deconstruct(out string _, out PlantSpecies.CapacityConstraint value);
                float capacity = WorldLayerManager.GetLayer(value.CapacityLayerName).AverageOverBoundaryAlignedWorldPos(xZi);
                float consumedCapacity = WorldLayerManager.GetLayer(value.ConsumedCapacityLayerName).AverageOverBoundaryAlignedWorldPos(xZi);
				float modifier = PlantSpecies.CapacityConstraint.GrowthRate(consumedCapacity, capacity) ;
				if (modifier < limitingGrowthRate)
                {
                    limitingGrowthRate = modifier;
                    limitingCapacityFactor = value.CapacityLayerName;
                    maxNumPlant = Mathf.FloorToInt(area.Area * capacity * (1 - species.MaxDeathRate) / value.ConsumedCapacityPerPop);
                }
               
                sb.AppendLine($"- Capacity '{value.CapacityLayerName}' ({consumedCapacity:P} / {capacity:P}) is constraining birth to {modifier:P}.");
			}
            sb.AppendLine("");

            float yieldPerDay = (int)(Mathf.RoundUp(plant.Species.ResourceRange.Diff * limitingYield) + plant.Species.ResourceRange.Min) * (species.MaturityAgeDays / limitingGrowthRate);
            sb.AppendLine($"Current yield per day: {yieldPerDay}");
            sb.AppendLine($"The limiting factor for yield is '{limitingYieldFactor}', constraining to {limitingYield:P} of max yield");
            sb.AppendLine($"In this plot overcrowding is affected most by '{limitingCapacityFactor}', have at most {maxNumPlant} plants (Current {numPlant}) or risk plants dying to overcrowding.");
        
            user.Player?.MsgLoc($"{sb.ToString()}");
        }

        private static int CalculateResourceYield(Plant plant, float bonusMultiplier = 1f)
		{
			float num = plant.GrowthPercent * plant.GrowthPercent;
			return (int)((Mathf.RoundUp(plant.Species.ResourceRange.Diff * plant.YieldPercent) + plant.Species.ResourceRange.Min) * num * bonusMultiplier);
		}

        private static PlantSpecies GetPlantSpecies(User user, string speciesName)
        {
            PlantSpecies species = null;
			if (speciesName == null)
			{
				WorldPosition3i worldPos = new WorldPosition3i(Eco.World.World.GetTopGroundPos(user.Position.XZi));
                Vector3i plantPos = Eco.World.World.GetTopGroundPos(worldPos.XZ) + Vector3i.Up;
                Plant plant = PlantBlock.GetPlant(plantPos);
                if (plant == null)
                {
                    user.Player?.ErrorLoc($"No plant found beneath the player.");
                    return null;
                }
                species = plant.Species;
			}
			else
			{
				species = Enumerable.Where(EcoSim.AllSpecies.OfType<PlantSpecies>(), (PlantSpecies s) => s.Name.ContainsCaseInsensitive(speciesName)).First();
                if (species == null)
                {
                    user.Player?.ErrorLoc($"No plant species found matching '{speciesName}'");
                    return null;
                }
            }
            return species;
        }

        [ChatCommand("Shows current values of temperature, rainfall, pollution and saltwater in this plot.")]
        public static void ShowHabitat(User user, string speciesName = null)
        {
            PlantSpecies species = GetPlantSpecies(user, speciesName);

            Vector2i xZi = user.Player.Position.XZi;
            float temperature = WorldLayerManager.GetLayer("Temperature").AverageOverBoundaryAlignedWorldPos(xZi);
			float rainfall = WorldLayerManager.GetLayer("Rainfall").AverageOverBoundaryAlignedWorldPos(xZi);
			float pollution = WorldLayerManager.GetLayer("GroundPollutionSpread").AverageOverBoundaryAlignedWorldPos(xZi);
			float saltwater = WorldLayerManager.GetLayer("SaltWater").AverageOverBoundaryAlignedWorldPos(xZi);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"Showing habitat information.");
            sb.AppendLine($"Species: {species.UILink()}");
            if (species == null)
            {
                sb.AppendLine($"Temperature: {temperature:P}");
                sb.AppendLine($"Rainfall: {rainfall:P}");
                sb.AppendLine($"Pollution: {pollution:P}");
                sb.AppendLine($"Salt water: {saltwater:P}");
            }
            else
            {
                sb.AppendLine($"Temperature: {temperature} (Ideal {species.IdealTemperatureRange.Min} - {species.IdealTemperatureRange.Max})");
                sb.AppendLine($"Rainfall: {rainfall} (Ideal {species.IdealMoistureRange.Min} - {species.IdealMoistureRange.Max})");
                sb.AppendLine($"Pollution: {pollution} (Ideal below {species.MaxPollutionDensity})");
                sb.AppendLine($"Salt water: {saltwater} (Ideal {species.IdealWaterRange.Min} - {species.IdealWaterRange.Max})");   
            }

            user.Player?.MsgLoc($"{sb.ToString()}");
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