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
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Oxide.Plugins
{
    [Info("TC Ban", "VisEntities", "1.2.0")]
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

            [JsonProperty("Delete Owned Entities After Punishment")]
            public bool DeleteOwnedEntitiesAfterPunishment { get; set; }

            [JsonProperty("Enable Cupboard Friendly Fire")]
            public bool EnableCupboardFriendlyFire { get; set; }
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

            if (string.Compare(_config.Version, "1.1.0") < 0)
            {
                _config.DeleteOwnedEntitiesAfterPunishment = defaultConfig.DeleteOwnedEntitiesAfterPunishment;
            }

            if (string.Compare(_config.Version, "1.2.0") < 0)
            {
                _config.EnableCupboardFriendlyFire = defaultConfig.EnableCupboardFriendlyFire;
            }

            PrintWarning("Config update complete! Updated from version " + _config.Version + " to " + Version.ToString());
            _config.Version = Version.ToString();
        }

        private Configuration GetDefaultConfig()
        {
            return new Configuration
            {
                Version = Version.ToString(),
                PunishmentType = PunishmentType.Kick,
                BroadcastPunishment = true,
                DeleteOwnedEntitiesAfterPunishment = false,
                EnableCupboardFriendlyFire = false
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
            CoroutineUtil.StopAllCoroutines();
            _config = null;
            _plugin = null;
        }

        private void OnEntityDeath(BuildingPrivlidge buildingPrivilege, HitInfo deathInfo)
        {
            if (buildingPrivilege == null || deathInfo == null)
                return;

            BasePlayer attacker = deathInfo.InitiatorPlayer;
            if (attacker == null || PlayerUtil.IsNPC(attacker))
                return;

            bool attackerIsAuthorized = buildingPrivilege.authorizedPlayers
                .Any(a => a.userid == attacker.userID);

            if (attackerIsAuthorized)
                return;

            bool ownerStillAuthed = buildingPrivilege.authorizedPlayers
                .Any(a => a.userid == buildingPrivilege.OwnerID);

            if (!ownerStillAuthed)
                return;

            PunishAuthorizedPlayers(buildingPrivilege.authorizedPlayers);
        }

        private void OnEntityTakeDamage(BuildingPrivlidge buildingPrivilege, HitInfo hitInfo)
        {
            if (!_config.EnableCupboardFriendlyFire)
                return;

            if (buildingPrivilege == null || hitInfo == null)
                return;

            BasePlayer attacker = hitInfo.InitiatorPlayer;
            if (attacker == null || PlayerUtil.IsNPC(attacker))
                return;

            ulong attackerId = attacker.userID;
            ulong ownerId = buildingPrivilege.OwnerID;

            if (attackerId == ownerId)
            {
                hitInfo.damageTypes.Clear();
                MessagePlayer(attacker, Lang.FriendlyFireBlocked);
                return;
            }

            if (PlayerUtil.AreTeammates(attackerId, ownerId))
            {
                hitInfo.damageTypes.Clear();
                MessagePlayer(attacker, Lang.FriendlyFireBlocked);
            }
        }

        #endregion Oxide Hooks

        #region Punishment

        private void PunishAuthorizedPlayers(HashSet<PlayerNameID> authorizedPlayers)
        {
            bool doBan = _config.PunishmentType == PunishmentType.Ban;
            bool doKick = _config.PunishmentType == PunishmentType.Kick;

            List<ulong> punishedPlayerIds = new List<ulong>();

            foreach (PlayerNameID authEntry in authorizedPlayers)
            {
                ulong targetId = authEntry.userid;
                BasePlayer maybeOnlinePlayer = PlayerUtil.FindById(targetId);

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

                punishedPlayerIds.Add(targetId);
            }

            if (_config.BroadcastPunishment)
            {
                string globalMsg = null;
                if (doBan)
                    globalMsg = lang.GetMessage(Lang.BanBroadcast, this);
                else if (doKick)
                    globalMsg = lang.GetMessage(Lang.KickBroadcast, this);

                if (!string.IsNullOrEmpty(globalMsg))
                {
                    foreach (BasePlayer activePlayer in BasePlayer.activePlayerList)
                    {
                        MessagePlayer(activePlayer, globalMsg);
                    }
                }
            }

            if (_config.DeleteOwnedEntitiesAfterPunishment && punishedPlayerIds.Count > 0)
            {
                CoroutineUtil.StartCoroutine("DeleteEntitiesByCoroutine", DeleteEntitiesByCoroutine(punishedPlayerIds));
            }
        }

        private IEnumerator DeleteEntitiesByCoroutine(List<ulong> punishedPlayerIds)
        {
            var allEntities = BaseNetworkable.serverEntities.ToList();

            int count = 0;
            foreach (BaseNetworkable networkable in allEntities)
            {
                BaseEntity entity = networkable as BaseEntity;
                if (entity == null)
                    continue;

                if (punishedPlayerIds.Contains(entity.OwnerID))
                {
                    entity.Kill();

                    count++;
                    if (count % 20 == 0)
                    {
                        yield return null;
                    }
                }
            }

            Puts($"Deleted {count} entities owned by punished players.");
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

        public static bool PluginLoaded(Plugin plugin)
        {
            if (plugin != null && plugin.IsLoaded)
                return true;
            else
                return false;
        }

        #endregion Helper Functions

        #region Helper Classes

        public static class PlayerUtil
        {
            public static BasePlayer FindById(ulong playerId)
            {
                return RelationshipManager.FindByID(playerId);
            }

            public static bool IsNPC(BasePlayer player)
            {
                return player.IsNpc || !player.userID.IsSteamId();
            }

            public static RelationshipManager.PlayerTeam GetTeam(ulong playerId)
            {
                if (RelationshipManager.ServerInstance == null)
                    return null;

                return RelationshipManager.ServerInstance.FindPlayersTeam(playerId);
            }

            public static bool AreTeammates(ulong firstPlayerId, ulong secondPlayerId)
            {
                var team = GetTeam(firstPlayerId);
                if (team != null && team.members.Contains(secondPlayerId))
                    return true;

                return false;
            }
        }

        public static class CoroutineUtil
        {
            private static readonly Dictionary<string, Coroutine> _activeCoroutines = new Dictionary<string, Coroutine>();

            public static Coroutine StartCoroutine(string baseCoroutineName, IEnumerator coroutineFunction, string uniqueSuffix = null)
            {
                string coroutineName;

                if (uniqueSuffix != null)
                    coroutineName = baseCoroutineName + "_" + uniqueSuffix;
                else
                    coroutineName = baseCoroutineName;

                StopCoroutine(coroutineName);

                Coroutine coroutine = ServerMgr.Instance.StartCoroutine(coroutineFunction);
                _activeCoroutines[coroutineName] = coroutine;
                return coroutine;
            }

            public static void StopCoroutine(string baseCoroutineName, string uniqueSuffix = null)
            {
                string coroutineName;

                if (uniqueSuffix != null)
                    coroutineName = baseCoroutineName + "_" + uniqueSuffix;
                else
                    coroutineName = baseCoroutineName;

                if (_activeCoroutines.TryGetValue(coroutineName, out Coroutine coroutine))
                {
                    if (coroutine != null)
                        ServerMgr.Instance.StopCoroutine(coroutine);

                    _activeCoroutines.Remove(coroutineName);
                }
            }

            public static void StopAllCoroutines()
            {
                foreach (string coroutineName in _activeCoroutines.Keys.ToArray())
                {
                    StopCoroutine(coroutineName);
                }
            }
        }

        #endregion Helper Classes

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
            public const string FriendlyFireBlocked = "FriendlyFireBlocked";
        }

        protected override void LoadDefaultMessages()
        {
            lang.RegisterMessages(new Dictionary<string, string>
            {
                [Lang.BanReason] = "You were banned because your tool cupboard was destroyed by an outsider.",
                [Lang.KickReason] = "You were kicked because your tool cupboard was destroyed by an outsider.",
                [Lang.BanBroadcast] = "All players authorized on the destroyed tool cupboard have been smacked with a BAN!",
                [Lang.KickBroadcast] = "All players authorized on the destroyed tool cupboard got KICKED off the server!",
                [Lang.FriendlyFireBlocked] = "You cannot damage your own (or your team's) cupboard!"

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