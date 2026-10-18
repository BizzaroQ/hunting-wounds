using System;
using Vintagestory.API.Common;

namespace HuntingWounds
{
    /// <summary>
    /// Configuration settings for the Hunting Wounds mod.
    /// Loaded from assets/huntingwounds/config/huntingwounds.json and overridden by VintagestoryData/ModConfig/huntingwounds.json.
    /// </summary>
    public class HuntingWoundsConfig
    {
        /// <summary>
        /// In-game days before a placed blood trail disappears completely.
        /// Transitions from fresh red (day0) to dried brown (day3) halfway through this duration.
        /// </summary>
        public double BloodTrailLifespanDays { get; set; } = 1.0;

        /// <summary>
        /// In-game hours without taking damage before an escaped animal recovers from its wounds.
        /// Set to 0 or less to disable wound recovery.
        /// </summary>
        public double WoundRecoveryHours { get; set; } = 24.0;

        /// <summary>
        /// Movement speed penalty applied per hit when an animal is slowed by an injury (-0.15 = -15% speed).
        /// </summary>
        public float WalkSpeedPenaltyPerHit { get; set; } = -0.15f;

        /// <summary>
        /// Movement speed boost applied per hit when an animal gets an adrenaline rush (0.20 = +20% speed).
        /// </summary>
        public float AdrenalineSpeedBoostPerHit { get; set; } = 0.20f;

        /// <summary>
        /// Probability (0.0 to 1.0) that a hit slows the animal down instead of triggering an adrenaline boost.
        /// </summary>
        public double SlowdownChancePerHit { get; set; } = 0.80;

        /// <summary>
        /// Real-time seconds an animal must remain calm (neither fleeing nor fighting) before it stops actively bleeding.
        /// </summary>
        public float StopBleedingAfterCalmSeconds { get; set; } = 2.5f;

        /// <summary>
        /// If true, prevents animals from placing blood trail blocks inside player-claimed land.
        /// </summary>
        public bool RespectLandClaims { get; set; } = false;
    }

    /// <summary>
    /// Entry point for the Hunting Wounds mod. Registers blocks, block entities, and entity behaviors,
    /// and manages loading/saving the server configuration.
    /// </summary>
    public class HuntingWoundsModSystem : ModSystem
    {
        public const string ModId = "huntingwounds";
        private const string ConfigFileName = "huntingwounds.json";

        public static HuntingWoundsConfig Config { get; private set; } = new HuntingWoundsConfig();

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterEntityBehaviorClass(EntityBehaviorHuntingWounds.BehaviorName, typeof(EntityBehaviorHuntingWounds));
            api.RegisterBlockClass("BlockBloodTrail", typeof(BlockBloodTrail));
            api.RegisterBlockEntityClass("BloodTrail", typeof(BlockEntityBloodTrail));
        }

        public override void AssetsLoaded(ICoreAPI api)
        {
            base.AssetsLoaded(api);

            if (api.Side == EnumAppSide.Server)
            {
                LoadConfig(api);
            }
        }

        private void LoadConfig(ICoreAPI api)
        {
            try
            {
                // 1. Load defaults from bundled assets/huntingwounds/config/huntingwounds.json
                IAsset bundledAsset = api.Assets.TryGet(new AssetLocation(ModId, $"config/{ConfigFileName}"));
                if (bundledAsset != null)
                {
                    Config = bundledAsset.ToObject<HuntingWoundsConfig>() ?? new HuntingWoundsConfig();
                }

                // 2. Load user overrides from VintagestoryData/ModConfig/huntingwounds.json if present
                var userConfig = api.LoadModConfig<HuntingWoundsConfig>(ConfigFileName);
                if (userConfig != null)
                {
                    Config = userConfig;
                }

                // Save to VintagestoryData/ModConfig/huntingwounds.json so users/server admins can edit it
                api.StoreModConfig(Config, ConfigFileName);
            }
            catch (Exception e)
            {
                api.Logger.Warning("[HuntingWounds] Failed to load {0}, using defaults: {1}", ConfigFileName, e.Message);
                Config = new HuntingWoundsConfig();
            }
        }
    }
}