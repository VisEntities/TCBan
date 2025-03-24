/*
 * Copyright (C) 2024 Game4Freak.io
 * This mod is provided under the Game4Freak EULA.
 * Full legal terms can be found at https://game4freak.io/eula/
 */

using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;
using ProtoBuf;
using System.Collections.Generic;
using System.Linq;

namespace Oxide.Plugins
{
    [Info("TC Ban", "VisEntities", "1.0.0")]
    [Description("Bans or kicks players when their cupboard gets destroyed.")]
    public class TCBan : RustPlugin
    {
        #region 3rd Party Dependencies

        [PluginReference]
        private readonly Plugin Clans;

        #endregion 3rd Party Dependencies

        #region Fields

        private static TCBan _plugin;
        private static Configuration _config;

        #endregion Fields

        #region Configuration

        private class Configuration
        {
            [JsonProperty("Version")]
            public string Version { get; set; }

            [JsonProperty("Punishment Type (Ban or Kick)")]
            [JsonConverter(typeof(StringEnumConverter))]
            public PunishmentType PunishmentType { get; set; }

            [JsonProperty("Broadcast Punishment")]
            public bool BroadcastPunishment { get; set; }
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            _config = Config.ReadObject<Configuration>();

            if (string.Compare(_config.Version, Version.ToString()) < 0)
                UpdateConfig();

            SaveConfig();
        }

        protected override void LoadDefaultConfig()
        {
            _config = GetDefaultConfig();
        }

        protected override void SaveConfig()
        {
            Config.WriteObject(_config, true);
        }

        private void UpdateConfig()
        {
            PrintWarning("Config changes detected! Updating...");

            Configuration defaultConfig = GetDefaultConfig();

            if (string.Compare(_config.Version, "1.0.0") < 0)
                _config = defaultConfig;

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                PunishmentType = PunishmentType.Kick,
                BroadcastPunishment = true
            };
        }

        #endregion Configuration

        #region Oxide Hooks

        private void Init()
        {
            _plugin = this;
            PermissionUtil.RegisterPermissions();
        }

        private void Unload()
        {
            _config = null;
            _plugin = null;
        }

        private void OnEntityDeath(BuildingPrivlidge buildingPrivilege, HitInfo deathInfo)
        {
            if (buildingPrivilege == null || deathInfo == null)
                return;

            BasePlayer attacker = deathInfo.InitiatorPlayer;
            if (attacker == null || IsNPC(attacker))
                return;

            bool attackerIsAuthorized = buildingPrivilege.authorizedPlayers
                .Any(a => a.userid == attacker.userID);

            if (attackerIsAuthorized)
                return;

            PunishAuthorizedPlayers(buildingPrivilege.authorizedPlayers);
        }

        #endregion Oxide Hooks

        #region Punishment

        private void PunishAuthorizedPlayers(HashSet<PlayerNameID> authorizedPlayers)
        {
            bool doBan = _config.PunishmentType == PunishmentType.Ban;
            bool doKick = _config.PunishmentType == PunishmentType.Kick;

            foreach (PlayerNameID authEntry in authorizedPlayers)
            {
                ulong targetId = authEntry.userid;

                BasePlayer maybeOnlinePlayer = FindById(targetId);
                if (maybeOnlinePlayer == null)
                    continue;

                if (PermissionUtil.HasPermission(maybeOnlinePlayer, PermissionUtil.IGNORE))
                    continue;

                IPlayer iPlayer = maybeOnlinePlayer.IPlayer;
                if (iPlayer == null)
                    continue;

                if (doBan)
                {
                    string banReason = lang.GetMessage(Lang.BanReason, this, iPlayer.Id);
                    iPlayer.Ban(banReason);
                }
                else if (doKick)
                {
                    string kickReason = lang.GetMessage(Lang.KickReason, this, iPlayer.Id);
                    iPlayer.Kick(kickReason);
                }
            }

            string globalMsg = null;
            if (doBan)
                globalMsg = lang.GetMessage(Lang.BanBroadcast, this);
            else if (doKick)
                globalMsg = lang.GetMessage(Lang.KickBroadcast, this);

            if (!string.IsNullOrEmpty(globalMsg) && _config.BroadcastPunishment)
            {
                foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
                {
                    if (activePlayer != null)
                    {
                        MessagePlayer(activePlayer, globalMsg);
                    }
                }
            }
        }

        #endregion Punishment

        #region Enums

        public enum PunishmentType
        {
            Ban,
            Kick
        }

        #endregion Enums

        #region 3rd Party Integration

        public static class ClansUtil
        {
            public static bool ClanExists(string clanTag)
            {
                if (!PluginLoaded(_plugin.Clans))
                {
                    return false;
                }

                JObject clan = _plugin.Clans.Call<JObject>("GetClan", clanTag);
                if (clan == null)
                {
                    return false;
                }

                return true;
            }

            public static JObject GetClan(string tag)
            {
                if (!PluginLoaded(_plugin.Clans))
                {
                    return null;
                }

                return _plugin.Clans.Call<JObject>("GetClan", tag);
            }

            public static string GetClanOf(ulong playerId)
            {
                if (!PluginLoaded(_plugin.Clans))
                {
                    return null;
                }

                return _plugin.Clans.Call<string>("GetClanOf", playerId);
            }
        }

        #endregion 3rd Party Integration

        #region Helper Functions

        public static BasePlayer FindById(ulong playerId)
        {
            return RelationshipManager.FindByID(playerId);
        }

        public static bool IsNPC(BasePlayer player)
        {
            return player.IsNpc || !player.userID.IsSteamId();
        }

        public static bool PluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.IsLoaded)
                return true;
            else
                return false;
        }

        #endregion Helper Functions

        #region Permissions

        private static class PermissionUtil
        {
            public const string IGNORE = "tcban.ignore";
            private static readonly List<string> _permissions = new List<string>
            {
                IGNORE,
            };

            public static void RegisterPermissions()
            {
                foreach (var permission in _permissions)
                {
                    _plugin.permission.RegisterPermission(permission, _plugin);
                }
            }

            public static bool HasPermission(BasePlayer player, string permissionName)
            {
                return _plugin.permission.UserHasPermission(player.UserIDString, permissionName);
            }
        }

        #endregion Permissions

        #region Localization

        private class Lang
        {
            public const string BanReason = "BanReason";
            public const string KickReason = "KickReason";
            public const string BanBroadcast = "BanBroadcast";
            public const string KickBroadcast = "KickBroadcast";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.BanReason] = "You were banned because your tool cupboard was destroyed by an outsider.",
                [Lang.KickReason] = "You were kicked because your tool cupboard was destroyed by an outsider.",
                [Lang.BanBroadcast] = "All players authorized on the destroyed tool cupboard have been smacked with a BAN!",
                [Lang.KickBroadcast] = "All players authorized on the destroyed tool cupboard got KICKED off the server!",

            }, this, "en");
        }

        private static string GetMessage(BasePlayer player, string messageKey, params object[] args)
        {
            string message = _plugin.lang.GetMessage(messageKey, _plugin, player.UserIDString);

            if (args.Length > 0)
                message = string.Format(message, args);

            return message;
        }

        public static void MessagePlayer(BasePlayer player, string messageKey, params object[] args)
        {
            string message = GetMessage(player, messageKey, args);
            _plugin.SendReply(player, message);
        }

        #endregion Localization
    }
}