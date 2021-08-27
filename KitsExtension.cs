using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using Facepunch;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using Oxide.Game.Rust.Cui;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Oxide.Plugins
{
	[Info("KitsExtension", "Airathias", "1.0.20")]
	[Description("Extends Kits functionality with player-specific reusable kits")]

    public class KitsExtension : RustPlugin
	{

        [PluginReference] private Plugin Kits, ServerRewards;

        #region Fields

        /// <summary>
        /// Filled with Kits Plugin data and then manipulated on-demand and saved
        /// </summary>
        private Kits_PluginData _kitsdata;

        /// <summary>
        /// Will cripple the plugin, this is also set if there's something wrong with loading Kits
        /// </summary>
        private bool _enabled;

        #endregion

        #region Localization

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>()
            {
                { "KitGiven", "A '{0}' kit was added to your account!" },
                { "KitGiftGiven", "{0} just gifted you a '{1}' kit, wow!" },
                { "KitGiftGiver", "You just gifted {0} a '{1}' kit!" },
                { "KitGiftGiver_Reward", "You just gifted {0} a '{1}' kit! Please take this {2} for your generosity <3" }
            }, this);
        }

        /// <summary>
        /// Returns a formatted lang entry based on key and userId
        /// </summary>
        /// <param name="key">Multilingual identifier key</param>
        /// <param name="id">Id of the user</param>
        /// <param name="args">List of arguments to format the string with; can exceed amount of variables in string, cannot be less</param>
        /// <returns>Formatted multilingual string</returns>
        string Lang(string key, string id = null, params object[] args) => string.Format(lang.GetMessage(key, this, id), args);

        /// <summary>
        /// Sends a player an in-game chat message with configurable prefix, if player is online and message is not empty
        /// </summary>
        /// <param name="player">Recipient of the chatmessage</param>
        /// <param name="msg">The actual formatted message</param>
        private void MessagePlayer(BasePlayer player, string msg)
		{
			if (player?.net?.connection == null || String.IsNullOrWhiteSpace(msg))
				return;

			SendReply(player, $"<color={Conf.ChatPrefixColor}>{Conf.ChatPrefix}</color>: {msg}");
		}

        /// <summary>
        /// Just used for development, will print some probably useless things that I found useful
        /// </summary>
        /// <param name="message">Message to print to console</param>
        private void VerbosePut(string message) {
            if(Conf.VerboseLogging) Puts(message);
        }

        #endregion

        #region Configuration

        private ConfigurationFile Conf;

        public class ConfigurationFile
        {
            [JsonProperty(PropertyName = "Kit extension is enabled (true/false)")]
            public bool Enabled = true;     
            
            [JsonProperty(PropertyName = "Chat Prefix")]
            public string ChatPrefix = "Kits";    
            
            [JsonProperty(PropertyName = "Chat Prefix Color")]
            public string ChatPrefixColor = "#787FFF";    
            
            [JsonProperty(PropertyName = "Kit Name Color")]
            public string KitNameColor = "#A3F551";    
            
            [JsonProperty(PropertyName = "Gift Giver Name Color")]
            public string GiftGiverNameColor = "#A3F551";      
            
            [JsonProperty(PropertyName = "Gift Giver RP Reward")]
            public int GiftGiverReward = 1000;   
            
            [JsonProperty(PropertyName = "Gift Giver Reward Color")]
            public string GiftRewardColor = "#FFEC50";      

            [JsonProperty(PropertyName = "Enable Verbose Logging (true/false)")]
            public bool VerboseLogging = true;      
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Creating a new configuration file");
            Conf = new ConfigurationFile();
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Conf = Config.ReadObject<ConfigurationFile>();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(Conf);

        #endregion


        #region Kits plugin classes
		private class Kits_PluginData
		{
			[JsonProperty(PropertyName = "Kits", ObjectCreationHandling = ObjectCreationHandling.Replace)]
			public List<Kits_Kit> Kits = new List<Kits_Kit>();
		}

        private class Kits_Kit : ICloneable
		{
            [JsonIgnore] public int ID;

			[JsonProperty(PropertyName = "Name")] public string Name;

			[JsonProperty(PropertyName = "Display Name")]
			public string DisplayName;

			[JsonProperty(PropertyName = "Color")] public string Color;

			[JsonProperty(PropertyName = "Permission")]
			public string Permission;

			[JsonProperty(PropertyName = "Description")]
			public string Description;

			[JsonProperty(PropertyName = "Image")] public string Image;

			[JsonProperty(PropertyName = "Hide")] public bool Hide;

			[JsonProperty(PropertyName = "Amount")]
			public int Amount;

			[JsonProperty(PropertyName = "Cooldown")]
			public double Cooldown;

			[JsonProperty(PropertyName = "Wipe Block")]
			public double CooldownAfterWipe;

			[JsonProperty(PropertyName = "Building")]
			public string Building;

			[JsonProperty(PropertyName = "Items", ObjectCreationHandling = ObjectCreationHandling.Replace)]
			public List<Kits_KitItem> Items;

            /// <summary>
            /// Clone (shallow) the current object, effectively duplicating the data
            /// </summary>
            /// <returns>A clone of the Kit object</returns>
            public object Clone() {
                return this.MemberwiseClone();
            }
		}

        private class Kits_KitItem
		{
            [JsonProperty(PropertyName = "Type")]
			public string Type;

			[JsonProperty(PropertyName = "Command")]
			public string Command;

			[JsonProperty(PropertyName = "ShortName")]
			public string ShortName;

			[JsonProperty(PropertyName = "Amount")]
			public int Amount;

			[JsonProperty(PropertyName = "Blueprint")]
			public int Blueprint;

			[JsonProperty(PropertyName = "SkinID")]
			public ulong SkinID;

			[JsonProperty(PropertyName = "Container")]
			public string Container;

			[JsonProperty(PropertyName = "Condition")]
			public float Condition;

			[JsonProperty(PropertyName = "Chance")]
			public int Chance;

			[JsonProperty(PropertyName = "Position")]
			public int Position;

			[JsonProperty(PropertyName = "Image")] 
            public string Image;

			[JsonProperty(PropertyName = "Weapon")]
			public object Weapon;

			[JsonProperty(PropertyName = "Content")]
			public object Content;
		}
        #endregion

        private void OnServerInitialized()
		{
            _enabled = true;

            if(!Conf.Enabled) {
                _enabled = false;
            }

            // Look, if the Kits plugin isn't there, there's not much to do. Makes sense?
			if(!Kits) {
                PrintError("Kits plugin was not found, KitsExtension can't function without it!");
                _enabled = false;
            }

            // This doesn't do much because I thought creating a new boolean to store what's basically already a
            // pretty self explanatory boolean was bullshit, so we just use the config boolean to check later on
            if(_enabled && (int)Conf.GiftGiverReward > 0 && !ServerRewards)
                PrintError("ServerRewards not installed, disabling gift rewards");
            
            // Same goes for here
            if(!_enabled)
                PrintError("KitsExtension plugin disabled");
		}

        /// <summary>
        /// Load the Kits Plugin's data json file and parse active kits for later use
        /// </summary>
        private void LoadKitsData()
		{
			try
			{
				_kitsdata = Interface.Oxide.DataFileSystem.ReadObject<Kits_PluginData>($"Kits/Kits");

                if(Conf.VerboseLogging)
                    foreach (Kits_Kit item in _kitsdata.Kits) { Puts("Found kit '" + item.Name + "' with " + item.Items.Count() + " items"); }
			}
			catch (Exception e)
			{
				PrintError(e.ToString());
			}

			if (_kitsdata == null) _kitsdata = new Kits_PluginData();
		}

        /// <summary>
        /// Saves modified kits in the Kits' data json file
        /// </summary>
        private void SaveKits()
		{
			Interface.Oxide.DataFileSystem.WriteObject($"Kits/Kits", _kitsdata);
            VerbosePut("Saved kits");

            // So, we do this because the actual Kits Plugin's exposed method of LoadData seems to fuck it up
            // and breaks the data. So, let's just have this minor inconvenience of having to reopen the UI
            // in-game if someone happens to buy a pack/kit right when someone else is looking at their kits
            this.Server.Command("oxide.reload Kits");
		}

        /// <summary>
        /// Searches and returns a kit currently in use by the Kits plugin
        /// </summary>
        /// <param name="kitName">Name of the kit to search for</param>
        /// <returns>A Kit object, if any, of the kit that was found based on name query</returns>
        private Kits_Kit FindKit(string kitName) {
            Kits_Kit existingKit = _kitsdata.Kits.Find(kit => kit.Name == kitName);
            return existingKit;
        }

        /// <summary>
        /// Searches and returns a kit based on the unique identifier the player would have for a player-specific kit
        /// </summary>
        /// <param name="kitName">Name of the kit (without steamID appendix) to search for</param>
        /// <param name="player">Player to consider as owner of the kit</param>
        /// <returns>A kit object as it's used in the Kits collection. Usually inaccessible to the player, used for duplication.</returns>
        private Kits_Kit FindKitForPlayer(string kitName, BasePlayer player) {
            // SteamId appendix should be enough, right? If not, just append something here.
            string playerSpecificKitname = kitName + "_" + player.UserIDString;
            Kits_Kit playerKit = _kitsdata.Kits.Find(kit => kit.Name == playerSpecificKitname);
            return playerKit;
        }

        /// <summary>
        /// Will decide whether to create a new duplicate, or to renew an existing one
        /// </summary>
        /// <param name="kitToGive">Base inaccessible kit to use as a base to duplicate</param>
        /// <param name="player">Player to either have the kit renewed for, or to grant access to a duplicate</param>
        /// <returns>A boolean to indicate if kit is valid. If not, returns false. Will return the result of one of the two submethods.</returns>
        private bool GiveKitToPlayer(Kits_Kit kitToGive, BasePlayer player, int amount = 0) {
            if(kitToGive == null) {
                PrintError("Kit with name '" + kitToGive.DisplayName + "' was not found!");
                return false;
            }

            // If a kit already exists, we won't have to create a new one so we'll just renew the existing one.
            Kits_Kit existingPlayerKit = FindKitForPlayer(kitToGive.Name, player);
            if(existingPlayerKit != null) {
                return RenewPlayerKit(existingPlayerKit, player, amount);
            } else {
                return CreateNewPlayerKit(kitToGive, player, amount);
            }
        }

        /// <summary>
        /// Clones an Kit object and modifies the clone to reflect the player-specific data of the recipient.
        /// Will also register a permission unique for the user and grants it to allow immediate access.
        /// </summary>
        /// <param name="baseKit">Base inaccessible kit to use as a base to duplicate</param>
        /// <param name="player">Player to receive the duplicated kit</param>
        /// <returns>A boolean to indicate if kit and player are valid. If not, returns false. Defaults to true.</returns>
        private bool CreateNewPlayerKit(Kits_Kit baseKit, BasePlayer player, int amount = 0) {
            
            if(baseKit == null || player == null)
                return false;

            // SteamId appendix should be enough, right? If not, just append something here.
            string playerSpecificKitname = baseKit.Name + "_" + player.UserIDString;

            // So, this is basically the whole reason this plugin exists; this is not possible with the object
            // in the Kits plugin. So here we are. This creates a shallow clone, so we're no longer editting
            // a reference object but a brand-spanking new one.
            Kits_Kit newKit = (Kits_Kit)baseKit.Clone();
            newKit.Name = playerSpecificKitname;

            // The kits plugin won't like this, but it doesn't break it. It'll try to register this, but since it doesn't
            // have a 'kits.' prefix Oxide will cry about it. It's fine to just ignore that.
            newKit.Permission = "kitsextension.playerkit." + newKit.Name;
            newKit.Amount = amount > 0 ? baseKit.Amount * amount : baseKit.Amount;
            
            // A nice blue, ain't that purdy? I don't include this as a config option because it'll create a legitimate
            // mess if you're not able to differentiate between BASE kits and actual PLAYER kits.
            newKit.Color = "#0059FF"; 
            
            _kitsdata.Kits.Add(newKit);
            
            // Registers the permission under this extension plugin rather than the kits plugin to avoid headaches
            // and console spammage
            permission.RegisterPermission(newKit.Permission, this);
            permission.GrantUserPermission(player.UserIDString, newKit.Permission, this);

            // This only saves the data file, we still need to reload Kits but we'll do that later on.
            SaveKits();
            
            return true;
        }

        /// <summary>
        /// Adds a single use to an existing player-specific kit to re-enable the kit
        /// </summary>
        /// <param name="playerKit">Existing kit that belongs to the player</param>
        /// <param name="player">Player that owns the existing kit</param>
        /// <returns>A boolean to indicate if kit and player are valid. If not, returns false. Defaults to true.</returns>
        private bool RenewPlayerKit(Kits_Kit playerKit, BasePlayer player, int amount = 0) {
            
            if(playerKit == null || player == null)
                return false;

            // We do this instead of resetting playerdata in case someone buys multiple at once    
            playerKit.Amount = amount > 0  ? playerKit.Amount + amount : playerKit.Amount + 1;

            SaveKits();
            
            return true;
        }

        /// <summary>
        /// Used to grant the sender of a gifted kit an optional monetary reward in the form of ServerRewards' Reward Points,
        /// and to send an alert to the recipient and a thank you to the sender
        /// </summary>
        /// <param name="sourcePlayer">BasePlayer object for the sending party, will default to 'An unknown player' if not found</param>
        /// <param name="targetPlayer">BasePlayer object for the recipient</param>
        /// <param name="givenKit">Kit that was distributed to the recipient</param>
        private void RewardGiftingPlayer(BasePlayer sourcePlayer, BasePlayer targetPlayer, Kits_Kit givenKit) {
            
            string msgTargetPlayer = $"<color={Conf.GiftGiverNameColor}>{targetPlayer.displayName}</color>";
            string msgKit = $"<color={Conf.KitNameColor}>{givenKit.DisplayName}</color>";

            // The SourcePlayer should ever only be null if they're offline and dead, so they're not sleepers. 
            // If someone gifts a player a kit while offline and dead we can't really do much so they'll be a secret admirer.
            string sourcePlayerDisplayName = (sourcePlayer == null) ? "An unknown player" : sourcePlayer.displayName;
            string msgSourcePlayer = $"<color={Conf.GiftGiverNameColor}>{sourcePlayerDisplayName}</color>";

            if(sourcePlayer != null) {
                if((int)Conf.GiftGiverReward > 0 && ServerRewards) {
                    
                    int giftGiverReward = (int)Conf.GiftGiverReward;
                    ServerRewards?.Call("AddPoints", sourcePlayer.UserIDString, giftGiverReward);

                    string msgReward = $"<color={Conf.GiftRewardColor}>{giftGiverReward.ToString()} RP</color>";
                    
                    VerbosePut("Rewarded " + giftGiverReward + "RP to '" + sourcePlayer.displayName + "' for gifting a kit");
                    MessagePlayer(sourcePlayer, Lang("KitGiftGiver_Reward", sourcePlayer.UserIDString, msgTargetPlayer, msgKit, msgReward));
                } else {
                    
                    VerbosePut("Kit gifter reward is disabled, skipping reward");
                    MessagePlayer(sourcePlayer, Lang("KitGiftGiver", sourcePlayer.UserIDString, msgTargetPlayer, msgKit));
                }
            }

            MessagePlayer(targetPlayer, Lang("KitGiftGiven", targetPlayer.UserIDString, msgSourcePlayer, msgKit));
        }

        /// <summary>
        /// Basic command to give players a kit. Will duplicate an existing kit by name and append a steamID to the identifier
        /// and permission so the item is specifically and only for a single player. Due to this, we can just edit the amount
        /// of the kit to the appropriate amount as purchased by the user and renew the kit if purchased again after usage.
        /// </summary>
        /// <param name="arg">A collection of arguments to parse later, must contain [steamID] and [kit name]</param>
        [ConsoleCommand("kitsextension.give")]
        private void CmdGiveKitToPlayer(ConsoleSystem.Arg arg) {
            var player = arg?.Player();
            if(player != null) return; // We want to have this command only available to server console or RCON

            if(!_enabled) return;

            if (arg.Args.Length < 2)
            {
                PrintError("Invalid arguments passed to method 'CmdGiveKitToPlayer'. Usage: 'kitsextension.give [steamId] [kitName] [optional amount]'");
                return;
            }

            string CmdSteamID = arg.Args[0];
            string CmdKitName = arg.Args[1];
            int CmdAmount = (arg.Args.Length > 2) ? Int32.Parse(arg.Args[2]) : 0;
            
            // We do it here instead of onload because we need to most up to date info to write back
            LoadKitsData();

            BasePlayer targetPlayer = BasePlayer.FindByID(Convert.ToUInt64(CmdSteamID));
            Kits_Kit kitToGive = FindKit(CmdKitName);

            if(!GiveKitToPlayer(kitToGive, targetPlayer, CmdAmount)) {
                PrintError("Something went wrong while giving a player a kit; see above errors for more information");
            } else {
                Puts("Successfully given '" + targetPlayer.displayName + "' " + CmdAmount + "x '" + CmdKitName + "' kit");

                string msgDisplayname = $"<color={Conf.KitNameColor}>{kitToGive.DisplayName}</color>";
                MessagePlayer(targetPlayer, Lang("KitGiven", targetPlayer.UserIDString, msgDisplayname));
            }
        }

        /// <summary>
        /// Gifts a player a kit by simply giving the kit to the recipient and then granting an optional reward to the sender.
        /// Will also send chat notifications to both parties, alerting the recipient and thanking the sender
        /// </summary>
        /// <param name="arg">A collection of arguments to parse later, must contain [steamID of the sender], [steamID of recipient] and [kit name]</param>
        [ConsoleCommand("kitsextension.gift")]
        private void CmdGiftKitToPlayer(ConsoleSystem.Arg arg) {
            var player = arg?.Player();
            if(player != null) return; // We want to have this command only available to server console or RCON

            if(!_enabled) return;

            if (arg.Args.Length < 3)
            {
                PrintError("Invalid arguments passed to method 'CmdGiveKitToPlayer'. Usage: 'kitsextension.gift [fromSteamId] [toSteamId] [kitName]'");
                return;
            }

            string CmdFromSteamID = arg.Args[0];
            string CmdToSteamID = arg.Args[1];
            string CmdKitName = arg.Args[2];

            // We do it here instead of onload because we need to most up to date info to write back
            LoadKitsData();

            BasePlayer sourcePlayer = BasePlayer.FindByID(Convert.ToUInt64(CmdFromSteamID));
            BasePlayer targetPlayer = BasePlayer.FindByID(Convert.ToUInt64(CmdToSteamID));
            Kits_Kit kitToGive = FindKit(CmdKitName);

            if(sourcePlayer == null) {
                // Scratched my head on this one, so you'll want to keep this here. A player that's offline and dead (so no sleeper)
                // will return null, so this will be tricky. I play it off as "a unknown player", but you may want to expand on that
                PrintError("Invalid source baseplayer from SteamID '" + CmdFromSteamID + "'");
            }

            if(!GiveKitToPlayer(kitToGive, targetPlayer)) {
                PrintError("Something went wrong while giving a player a kit; see above errors for more information");
            } else {
                Puts("Successfully given '" + targetPlayer.displayName + "' a '" + kitToGive.DisplayName + "' kit");
                RewardGiftingPlayer(sourcePlayer, targetPlayer, kitToGive);
            }
        }

    }
}