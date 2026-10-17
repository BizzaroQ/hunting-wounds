using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HuntingWounds
{
    public class HuntingWoundsConfig
    {
        public double TotalLifespanDays { get; set; } = 1.0;
        public double WoundRecoveryHours { get; set; } = 24.0;
        public float SpeedPenaltyPerHit { get; set; } = -0.15f;
        public float SpeedBoostPerHit { get; set; } = 0.20f;
        public double SlowdownChance { get; set; } = 0.80;
        public float CalmGraceSeconds { get; set; } = 2.5f;
        public bool RespectLandClaims { get; set; } = false;
    }

    public class HuntingWoundsModSystem : ModSystem
    {
        private const string ConfigFileName = "huntingwounds.json";
        public static HuntingWoundsConfig Config { get; private set; } = new HuntingWoundsConfig();

        public override void Start(ICoreAPI api)
        {
            base.Start(api);

            api.RegisterEntityBehaviorClass("huntingwounds", typeof(EntityBehaviorHuntingWounds));
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
                var bundledAsset = api.Assets.TryGet(new AssetLocation("huntingwounds", "config/huntingwounds.json"));
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

    public class BlockBloodTrail : Block
    {
        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            // If the supporting ground block underneath is removed, remove the blood trail
            Block belowBlock = world.BlockAccessor.GetBlock(pos.DownCopy());
            if (!belowBlock.SideSolid[BlockFacing.UP.Index])
            {
                world.BlockAccessor.SetBlock(0, pos);
                world.BlockAccessor.MarkBlockDirty(pos);
            }
        }
    }

    public class BlockEntityBloodTrail : BlockEntity
    {
        private double createdTotalDays = -1;
        private int replacedBlockId = 0;
        private string replacedBlockCode = "";

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api.Side == EnumAppSide.Server)
            {
                if (createdTotalDays < 0)
                {
                    createdTotalDays = api.World.Calendar.TotalDays;
                }

                // Check age every 30 seconds on the server to minimize tick overhead in multiplayer
                RegisterGameTickListener(OnServerTick, 30000);
            }
        }

        public void RefreshTrail(Block freshBlock, double currentTotalDays, Block previousBlock)
        {
            this.Block = freshBlock;
            createdTotalDays = currentTotalDays;
            if (replacedBlockId == 0 && string.IsNullOrEmpty(replacedBlockCode) && previousBlock != null && previousBlock.Id != 0)
            {
                replacedBlockId = previousBlock.Id;
                replacedBlockCode = previousBlock.Code?.ToString() ?? "";
            }
            // Only mark chunk dirty for saving on server; clients don't need BE attributes
            MarkDirty(false);
        }

        private void OnServerTick(float dt)
        {
            Block currentBlock = Api.World.BlockAccessor.GetBlock(Pos);
            if (currentBlock?.Code == null || !currentBlock.Code.Path.StartsWith("bloodtrail-"))
            {
                return;
            }

            if (createdTotalDays < 0)
            {
                createdTotalDays = Api.World.Calendar.TotalDays;
            }

            double lifespanDays = HuntingWoundsModSystem.Config?.TotalLifespanDays ?? 1.0;
            double ageDays = Api.World.Calendar.TotalDays - createdTotalDays;

            // After the full duration, remove the brown blood trail (and restore any trampled grass/snow)
            if (ageDays >= lifespanDays)
            {
                int restoreId = 0;
                Block prevBlock = null;

                if (!string.IsNullOrEmpty(replacedBlockCode))
                {
                    prevBlock = Api.World.GetBlock(new AssetLocation(replacedBlockCode));
                }
                if (prevBlock == null && replacedBlockId > 0)
                {
                    prevBlock = Api.World.GetBlock(replacedBlockId);
                }

                if (prevBlock != null && prevBlock.Code != null && !prevBlock.Code.Path.StartsWith("bloodtrail"))
                {
                    restoreId = prevBlock.Id;
                }

                Api.World.BlockAccessor.SetBlock(restoreId, Pos);
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
                return;
            }

            // First half of duration: Red (day0)
            // Second half of duration: Brown (day3)
            string targetVariant = (ageDays < lifespanDays * 0.5) ? "day0" : "day3";

            if (currentBlock.Variant != null && currentBlock.Variant.TryGetValue("stage", out string currentStage) && currentStage != targetVariant)
            {
                Block nextStageBlock = Api.World.GetBlock(new AssetLocation("huntingwounds", $"bloodtrail-{targetVariant}"));
                if (nextStageBlock != null)
                {
                    // ExchangeBlock swaps the block variant while keeping this BlockEntity intact
                    Api.World.BlockAccessor.ExchangeBlock(nextStageBlock.Id, Pos);
                    this.Block = nextStageBlock;
                    Api.World.BlockAccessor.MarkBlockDirty(Pos);
                    MarkDirty(false);
                }
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            createdTotalDays = tree.GetDouble("createdTotalDays", -1);
            replacedBlockId = tree.GetInt("replacedBlockId", 0);
            replacedBlockCode = tree.GetString("replacedBlockCode", "");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("createdTotalDays", createdTotalDays);
            tree.SetInt("replacedBlockId", replacedBlockId);
            if (!string.IsNullOrEmpty(replacedBlockCode))
            {
                tree.SetString("replacedBlockCode", replacedBlockCode);
            }
        }
    }

    public class EntityBehaviorHuntingWounds : EntityBehavior
    {
        // TODO: Implement pheromone trails
        private int hitCount = 0;
        private float speedModifier = 0f;
        private double lastWoundedTotalHours = -1;
        private SimpleParticleProperties bloodParticles;
        private float particleTimer = 0f;
        private float recoveryCheckTimer = 0f;
        private BlockPos lastBloodTrailPos;
        private bool isHuntable = true;

        // Bleed only while the entity is in fight-or-flight (fleeing, charging, or attacking)
        private bool isBleeding = false;
        private float calmTimer = 0f;

        // Cached reflection references for TaskAI inspection
        private bool reflectionInitialized = false;
        private object cachedTaskManager;
        private PropertyInfo activeTasksProp;
        private FieldInfo activeTasksField;

        public EntityBehaviorHuntingWounds(Entity entity) : base(entity)
        {
            bloodParticles = new SimpleParticleProperties(
                1, 3,
                ColorUtil.ToRgba(255, 150, 0, 0),
                new Vec3d(), new Vec3d(),
                new Vec3f(-1, 0, -1), new Vec3f(1, 1, 1),
                1.5f, 1f, 0.5f, 1.5f, EnumParticleModel.Cube
            );
        }

        public override string PropertyName() => "huntingwounds";

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);
            hitCount = entity.WatchedAttributes.GetInt("woundHitCount", 0);
            speedModifier = entity.WatchedAttributes.GetFloat("woundSpeedModifier", 0f);
            lastWoundedTotalHours = entity.WatchedAttributes.GetDouble("woundLastTotalHours", -1);
            ApplySpeedModifier();
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
            // Ensure the entity is harvestable (drops food/resources when butchered)
            isHuntable = entity.HasBehavior("harvestable");

            if (entity.Api.Side == EnumAppSide.Server && hitCount > 0)
            {
                CheckWoundRecovery();
            }
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);

            if (!isHuntable || entity.Api.Side != EnumAppSide.Server) return;

            if (damageSource.Type != EnumDamageType.Heal && damage > 0)
            {
                CheckWoundRecovery();

                var config = HuntingWoundsModSystem.Config;
                hitCount++;
                lastWoundedTotalHours = entity.World.Calendar.TotalHours;
                entity.WatchedAttributes.SetInt("woundHitCount", hitCount);
                entity.WatchedAttributes.SetDouble("woundLastTotalHours", lastWoundedTotalHours);

                // Default: 80% chance to slow down, 20% chance for an adrenaline speed boost
                if (entity.World.Rand.NextDouble() < config.SlowdownChance)
                {
                    speedModifier += config.SpeedPenaltyPerHit;
                }
                else
                {
                    speedModifier += config.SpeedBoostPerHit;
                }

                // Cap penalty to prevent negative movement speed
                if (speedModifier < -0.9f) speedModifier = -0.9f;

                entity.WatchedAttributes.SetFloat("woundSpeedModifier", speedModifier);
                ApplySpeedModifier();

                // Start bleeding for this fight-or-flight state and leave an immediate blood splatter
                isBleeding = true;
                calmTimer = 0f;
                lastBloodTrailPos = null;
                TryPlaceBloodTrail();
            }
        }

        private void CheckWoundRecovery()
        {
            if (hitCount <= 0) return;

            double recoveryHours = HuntingWoundsModSystem.Config?.WoundRecoveryHours ?? 24.0;
            if (recoveryHours <= 0) return;

            double currentHours = entity.World.Calendar.TotalHours;
            if (lastWoundedTotalHours < 0)
            {
                lastWoundedTotalHours = currentHours;
                entity.WatchedAttributes.SetDouble("woundLastTotalHours", lastWoundedTotalHours);
                return;
            }

            if (currentHours - lastWoundedTotalHours >= recoveryHours)
            {
                ClearWounds();
            }
        }

        private void ClearWounds()
        {
            hitCount = 0;
            speedModifier = 0f;
            lastWoundedTotalHours = -1;
            isBleeding = false;
            entity.WatchedAttributes.SetInt("woundHitCount", 0);
            entity.WatchedAttributes.SetFloat("woundSpeedModifier", 0f);
            entity.WatchedAttributes.RemoveAttribute("woundLastTotalHours");
            entity.Stats.Remove("walkspeed", "huntingwounds");
        }

        private void ApplySpeedModifier()
        {
            if (hitCount > 0)
            {
                entity.Stats.Set("walkspeed", "huntingwounds", speedModifier, true);
            }
            else
            {
                entity.Stats.Remove("walkspeed", "huntingwounds");
            }
        }

        private void EnsureReflectionInitialized()
        {
            if (reflectionInitialized) return;
            reflectionInitialized = true;

            EntityBehavior taskAi = entity.GetBehavior("taskai");
            if (taskAi == null) return;

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type taskAiType = taskAi.GetType();
            cachedTaskManager = taskAiType.GetProperty("TaskManager", flags)?.GetValue(taskAi)
                             ?? taskAiType.GetField("TaskManager", flags)?.GetValue(taskAi)
                             ?? taskAiType.GetField("taskManager", flags)?.GetValue(taskAi);

            if (cachedTaskManager != null)
            {
                Type tmType = cachedTaskManager.GetType();
                activeTasksProp = tmType.GetProperty("ActiveTasksBySlot", flags);
                activeTasksField = tmType.GetField("ActiveTasksBySlot", flags);
            }
        }

        private bool IsInFightOrFlightTask()
        {
            EnsureReflectionInitialized();

            if (cachedTaskManager != null)
            {
                Array activeTasks = (activeTasksProp?.GetValue(cachedTaskManager)
                                  ?? activeTasksField?.GetValue(cachedTaskManager)) as Array;

                if (activeTasks != null)
                {
                    foreach (object task in activeTasks)
                    {
                        if (task == null) continue;
                        string taskTypeName = task.GetType().Name;
                        if (taskTypeName.IndexOf("Flee", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            taskTypeName.IndexOf("SeekEntity", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            taskTypeName.IndexOf("MeleeAttack", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }

            return false;
        }

        public override void OnGameTick(float deltaTime)
        {
            base.OnGameTick(deltaTime);

            if (hitCount <= 0 || !entity.Alive || entity.Api.Side != EnumAppSide.Server) return;

            if (!isBleeding)
            {
                // Periodically check if an escaped animal's wounds should heal
                recoveryCheckTimer += deltaTime;
                if (recoveryCheckTimer >= 10f)
                {
                    recoveryCheckTimer = 0f;
                    CheckWoundRecovery();
                }
                return;
            }

            particleTimer += deltaTime;
            if (particleTimer < 0.5f) return;

            float elapsed = particleTimer;
            particleTimer = 0f;

            if (IsInFightOrFlightTask())
            {
                calmTimer = 0f;
            }
            else
            {
                calmTimer += elapsed;
                float graceSeconds = HuntingWoundsModSystem.Config?.CalmGraceSeconds ?? 2.5f;
                if (calmTimer >= graceSeconds)
                {
                    isBleeding = false;
                    return;
                }
            }

            Cuboidf box = entity.SelectionBox ?? entity.CollisionBox;
            bloodParticles.MinPos = entity.ServerPos.XYZ;
            if (box != null)
            {
                bloodParticles.AddPos.Set(box.X2 - box.X1, 0, box.Z2 - box.Z1);
            }
            else
            {
                bloodParticles.AddPos.Set(0.5, 0, 0.5);
            }
            entity.World.SpawnParticles(bloodParticles);

            TryPlaceBloodTrail();
        }

        private void TryPlaceBloodTrail()
        {
            IBlockAccessor blockAccessor = entity.World.BlockAccessor;
            BlockPos basePos = entity.ServerPos.XYZ.AsBlockPos;

            // Find the surface position right above a solid ground block
            BlockPos surfacePos = null;
            Block baseBlock = blockAccessor.GetBlock(basePos);
            if (baseBlock.SideSolid[BlockFacing.UP.Index])
            {
                surfacePos = basePos.UpCopy();
            }
            else
            {
                Block belowBlock = blockAccessor.GetBlock(basePos.DownCopy());
                if (belowBlock.SideSolid[BlockFacing.UP.Index])
                {
                    surfacePos = basePos.Copy();
                }
            }

            if (surfacePos == null) return;

            // Avoid re-placing on the exact same block every tick while standing still
            if (lastBloodTrailPos != null && lastBloodTrailPos.Equals(surfacePos)) return;

            if (HuntingWoundsModSystem.Config.RespectLandClaims)
            {
                var claims = entity.World.Claims?.Get(surfacePos);
                if (claims != null && claims.Length > 0) return;
            }

            Block existingBlock = blockAccessor.GetBlock(surfacePos);
            string existingPath = existingBlock.Code?.Path ?? "";

            bool isAir = existingBlock.Id == 0;
            bool isExistingBlood = existingPath.StartsWith("bloodtrail-");
            bool isTrampleableCover = existingPath.StartsWith("tallgrass-") || existingPath == "snowlayer-1";

            if (!isAir && !isExistingBlood && !isTrampleableCover) return;

            Block freshBloodBlock = entity.World.GetBlock(new AssetLocation("huntingwounds", "bloodtrail-day0"));
            if (freshBloodBlock == null) return;

            Block previousBlock = (!isAir && !isExistingBlood) ? existingBlock : null;

            if (isExistingBlood)
            {
                // If stepping over older blood, refresh it to day0 without losing its original replacedBlock
                if (existingBlock.Id != freshBloodBlock.Id)
                {
                    blockAccessor.ExchangeBlock(freshBloodBlock.Id, surfacePos);
                    blockAccessor.MarkBlockDirty(surfacePos);
                }
            }
            else
            {
                blockAccessor.SetBlock(freshBloodBlock.Id, surfacePos);
            }

            if (blockAccessor.GetBlockEntity(surfacePos) is BlockEntityBloodTrail beBlood)
            {
                beBlood.RefreshTrail(freshBloodBlock, entity.World.Calendar.TotalDays, previousBlock);
            }

            lastBloodTrailPos = surfacePos;
        }
    }
}