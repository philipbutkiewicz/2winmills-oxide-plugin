using Oxide.Core;
using Rust;

namespace Oxide.Plugins
{
    [Info("2WinMills", "bagno", "1.0.0")]
    [Description("2WinMills Server Plugin")]
    class TwinMills : CovalencePlugin
    {
        private class TwinMillsConfig
        {
            /// <summary>
            /// PlayerDeath webhook URL.
            /// </summary>
            public string PlayerDeathWebHookURL;

            /// <summary>
            /// PlayerKill webhook URL.
            /// </summary>
            public string PlayerKillWebHookURL;
            
            /// <summary>
            /// PlayerLoot webhook URL.
            /// </summary>
            public string PlayerLootWebHookURL;

            /// <summary>
            /// PlayerLogin webhook URL.
            /// </summary>
            public string PlayerLogInWebHookURL;

            /// <summary>
            /// EntityDestroyed webhook URL.
            /// </summary>
            public string EntityDestroyedWebHookURL;

            /// <summary>
            /// API key.
            /// </summary>
            public string ApiKey;
        }

        /// <summary>
        /// Plugin configuration.
        /// </summary>
        private TwinMillsConfig config;

        /// <summary>
        /// Initialize the plugin.
        /// </summary>
        private void Init()
        {
            config = Config.ReadObject<PluginConfig>();
        }

        /// <summary>
        /// Default config initialization override.
        /// </summary>
        protected override void LoadDefaultConfig()
        {
            Config.WriteObject(new TwinMillsConfig
            {
                PlayerDeathWebHookURL = null,
                PlayerKillWebHookURL = null,
                PlayerLootWebHookURL = null,
                PlayerLogInWebHookURL = null,
                EntityDestroyedWebHookURL = null,
                ApiKey = null,
            }, true);
        }

        /// <summary>
        /// Queues a webhook.
        /// </summary>
        /// <param name="name"></param>
        /// <param name="webHookUrl"></param>
        /// <param name="param"></param>
        private void QueueWebHook(string name, string webHookUrl, Dictionary<string, string> param)
        {
            if (!config.ApiKey)
            {
                Puts("ApiKey is not configured");
                return;
            }

            Dictionary<string, string> headers = new Dictionary<string, string>();
            headers.Add(new KeyValuePair<string, string>('Authorization', config.ApiKey));

            webrequest.Enqueue(webHookUrl, GetRequestBody(param), (code, response) =>
            {
                Puts($"{name}(Status:{code})");
            }, this, RequestMethod.POST, headers);
        }

        /// <summary>
        /// Prepares request body from a dict.
        /// </summary>
        /// <param name="dict"></param>
        /// <returns></returns>
        private string[] GetRequestBody(Dictionary<string, string> dict)
        {
            return string.Join("&", dict.Cast<string>().Select(key => string.Format("{0}={1}", key, dict[key])));
        }

        /// <summary>
        /// Gets a list of authorized players to a specific entity.
        /// </summary>
        /// <param name="entity"></param>
        /// <returns></returns>
        private List<ulong> GetAuthorized(BaseEntity entity)
        {
            List<ulong> authorized = new List<ulong>();
            AutoTurret turret = entity as AutoTurret;
            BuildingPrivlidge priv = entity as BuildingPrivlidge;

            foreach (var user in (turret ? turret.authorizedPlayers : priv.authorizedPlayers))
            {
                authorized.Add(user.userid);
            }

            return authorized;
        }

        /// <summary>
        /// Event fired when a player connects.
        /// </summary>
        /// <param name="player"></param>
        private void OnPlayerConnected(BasePlayer player)
        {
            // Is the player an admin, server, banned or NOT connected?
            if (player.IsAdmin || player.IsServer || player.IsBanned || !player.IsConnected) return;

            if (!config.PlayerLogInWebHookURL)
            {
                Puts("PlayerLogInWebHookURL is not configured");
                return;
            }

            Dictionary<string, string> param = new Dictionary<string, string>();
            param.Add("name", player.Name);
            param.Add("id", player.Id);
            param.Add("address", player.Address);

            QueueWebHook("PlayerLogInWebHook", config.PlayerLogInWebHookURL, param);
        }

        /// <summary>
        /// Event fired when a player dies.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        private object OnPlayerDeath(BasePlayer player, HitInfo info)
        {
            // Is the initiator NOT a player?
            if (!hitInfo.Initiator is Player) return;

            // Is the initiator an admin?
            if (hitInfo.Initiator.ToPlayer().IsAdmin) return;

            if (!config.PlayerDeathWebHookURL)
            {
                Puts("PlayerDeathWebHookURL is not configured");
                return null;
            }

            Dictionary<string, string> param = new Dictionary<string, string>();
            param.Add("name", player.Name);
            param.Add("id", player.Id);
            param.Add("killer_name", hitInfo.Initiator.ToPlayer().Name);
            param.Add("killer_id", hitInfo.Initiator.ToPlayer().Id);
            param.Add("hit_position", hitInfo.HitPositionWorld.ToString());
 
            QueueWebHook("PlayerDeathWebHookURL", config.PlayerKillWebHookURL, param);

            return null;
        }

        /// <summary>
        /// Event fires when an entity is destroyed.
        /// </summary>
        /// <param name="victimEntity"></param>
        /// <param name="hitInfo"></param>
        private void OnEntityDeath(BaseCombatEntity victimEntity, HitInfo hitInfo)
        {
            // Prevent weird error conditions
            if (victimEntity == null) return;

            // Is the initiator NOT a player?
            if (!hitInfo.Initiator is Player) return;

            // Is the initiator an admin?
            if (hitInfo.Initiator.ToPlayer().IsAdmin) return;

            // Does the victim have a creator and is the creator a player?
            if (victimEntity.creatorEntity && victimEntity.creatorEntity is Player)
            {
                // Is the creator the same as offending player?
                if (victimEntity.creatorEntity.ToPlayer().userID == hitInfo.Initiator.ToPlayer().userID) return;

                // Is the offending player authorized?
                if (GetAuthorized(victimEntity).Contains(hitInfo.Initiator.ToPlayer().userID)) return;

                // Is the offending player on the same team as the creator?
                if (victimEntity.creatorEntity.ToPlayer().currentTeam == hitInfo.Initiator.ToPlayer().currentTeam); return;
            }

            // Is the victim NOT a player? If so, we will fire off the "EntityDestroyed" webhook. Otherwise, we'll fire off the "PlayerKill" webhook.
            if (!victimEntity is Player)
            {
                if (!config.EntityDestroyedWebHookURL)
                {
                    Puts("EntityDestroyedWebHookURL is not configured");
                    return null;
                }

                Dictionary<string, string> param = new Dictionary<string, string>();
                param.Add("name", hitInfo.Initiator.ToPlayer().Name);
                param.Add("id", hitInfo.Initiator.ToPlayer().Id);
                param.Add("owner_name", victimEntity.creatorEntity.ToPlayer().Name);
                param.Add("owner_id", victimEntity.creatorEntity.ToPlayer().Id);
                param.Add("entity_name", victimEntity._name);
                param.Add("hit_position", hitInfo.HitPositionWorld.ToString());
    
                QueueWebHook("EntityDestroyedWebHook", config.EntityDestroyedWebHookURL, param);
            }
            else
            {
                if (!config.PlayerKillWebHookURL)
                {
                    Puts("PlayerKillWebHookURL is not configured");
                    return null;
                }

                Dictionary<string, string> param = new Dictionary<string, string>();
                param.Add("name", hitInfo.Initiator.ToPlayer().Name);
                param.Add("id", hitInfo.Initiator.ToPlayer().Id);
                param.Add("target_name", victimEntity.ToPlayer().Name);
                param.Add("target_id", victimEntity.ToPlayer().Id);
                param.Add("hit_position", hitInfo.HitPositionWorld.ToString());
    
                QueueWebHook("PlayerKillWebHook", config.PlayerKillWebHookURL, param);
            }
        }

        /// <summary>
        /// Event fires when a player is looted.
        /// </summary>
        /// <param name="player"></param>
        /// <param name="target"></param>
        private void OnLootPlayer(BasePlayer player, BasePlayer target)
        {
            // Is the player an admin?
            if (player.IsAdmin) return;

            // Is the player on the same team as target player?
            if (player.currentTeam == target.currentTeam) return;

            if (!config.PlayerLootWebHookURL)
            {
                Puts("PlayerLootWebHookURL is not configured");
                return null;
            }

            Dictionary<string, string> param = new Dictionary<string, string>();
            param.Add("name", player.Name);
            param.Add("id", player.Id);
            param.Add("target_name", target.Name);
            param.Add("target_id", target.Id);

            QueueWebHook("PlayerLootWebHook", config.PlayerLootWebHookURL, param);
        }
    }
}