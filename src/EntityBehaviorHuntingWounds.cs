using System;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HuntingWounds
{
    /// <summary>
    /// Applies hunting wound effects to harvestable animals when damaged:
    /// - Adjusts movement speed per hit (chance to slow down or gain an adrenaline speed boost)
    /// - Spawns blood particles and places blood trail blocks while the animal is fleeing or fighting
    /// - Heals wounds after a configurable number of in-game hours without further damage
    /// </summary>
    public class EntityBehaviorHuntingWounds : EntityBehavior
    {
        public const string BehaviorName = "huntingwounds";

        private const string AttrWoundHitCount = "woundHitCount";
        private const string AttrWoundSpeedModifier = "woundSpeedModifier";
        private const string AttrWoundLastTotalHours = "woundLastTotalHours";
        private const string WalkSpeedStatKey = "walkspeed";

        private const float MaxSpeedPenalty = -0.9f;
        private const float BleedTickIntervalSeconds = 0.5f;
        private const float RecoveryCheckIntervalSeconds = 10f;

        // TODO: Implement pheromone trails
        private int hitCount = 0;
        private float speedModifier = 0f;
        private double lastWoundedTotalHours = -1;
        private readonly SimpleParticleProperties bloodParticles;

        private float bleedTickTimer = 0f;
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
            bloodParticles = CreateBloodParticleTemplate();
        }

        public override string PropertyName() => BehaviorName;

        public override void Initialize(EntityProperties properties, JsonObject attributes)
        {
            base.Initialize(properties, attributes);

            hitCount = entity.WatchedAttributes.GetInt(AttrWoundHitCount, 0);
            speedModifier = entity.WatchedAttributes.GetFloat(AttrWoundSpeedModifier, 0f);
            lastWoundedTotalHours = entity.WatchedAttributes.GetDouble(AttrWoundLastTotalHours, -1);

            ApplySpeedModifier();
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);

            // Only apply hunting wounds to animals that can be harvested/butchered
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
            if (damageSource.Type == EnumDamageType.Heal || damage <= 0) return;

            CheckWoundRecovery();

            RecordNewWound();
            ApplyRandomSpeedChange(HuntingWoundsModSystem.Config);
            StartBleedingImmediately();
        }

        public override void OnGameTick(float deltaTime)
        {
            base.OnGameTick(deltaTime);

            if (hitCount <= 0 || !entity.Alive || entity.Api.Side != EnumAppSide.Server) return;

            if (!isBleeding)
            {
                UpdateOutOfCombatRecovery(deltaTime);
                return;
            }

            bleedTickTimer += deltaTime;
            if (bleedTickTimer < BleedTickIntervalSeconds) return;

            float elapsedSeconds = bleedTickTimer;
            bleedTickTimer = 0f;

            if (!ShouldContinueBleeding(elapsedSeconds))
            {
                isBleeding = false;
                return;
            }

            SpawnBloodParticles();
            TryPlaceBloodTrail();
        }

        private void RecordNewWound()
        {
            hitCount++;
            lastWoundedTotalHours = entity.World.Calendar.TotalHours;
            entity.WatchedAttributes.SetInt(AttrWoundHitCount, hitCount);
            entity.WatchedAttributes.SetDouble(AttrWoundLastTotalHours, lastWoundedTotalHours);
        }

        private void ApplyRandomSpeedChange(HuntingWoundsConfig config)
        {
            // Default: 80% chance to slow down from injury, 20% chance for an adrenaline speed boost
            bool slowsDown = entity.World.Rand.NextDouble() < config.SlowdownChancePerHit;
            speedModifier += slowsDown ? config.WalkSpeedPenaltyPerHit : config.AdrenalineSpeedBoostPerHit;

            // Cap penalty so the animal can still move slowly
            if (speedModifier < MaxSpeedPenalty)
            {
                speedModifier = MaxSpeedPenalty;
            }

            entity.WatchedAttributes.SetFloat(AttrWoundSpeedModifier, speedModifier);
            ApplySpeedModifier();
        }

        private void StartBleedingImmediately()
        {
            isBleeding = true;
            calmTimer = 0f;
            lastBloodTrailPos = null;
            TryPlaceBloodTrail();
        }

        private void UpdateOutOfCombatRecovery(float deltaTime)
        {
            recoveryCheckTimer += deltaTime;
            if (recoveryCheckTimer >= RecoveryCheckIntervalSeconds)
            {
                recoveryCheckTimer = 0f;
                CheckWoundRecovery();
            }
        }

        private bool ShouldContinueBleeding(float elapsedSeconds)
        {
            if (IsInFightOrFlightTask())
            {
                calmTimer = 0f;
                return true;
            }

            calmTimer += elapsedSeconds;
            float calmThresholdSeconds = HuntingWoundsModSystem.Config?.StopBleedingAfterCalmSeconds ?? 2.5f;
            return calmTimer < calmThresholdSeconds;
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
                entity.WatchedAttributes.SetDouble(AttrWoundLastTotalHours, lastWoundedTotalHours);
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

            entity.WatchedAttributes.SetInt(AttrWoundHitCount, 0);
            entity.WatchedAttributes.SetFloat(AttrWoundSpeedModifier, 0f);
            entity.WatchedAttributes.RemoveAttribute(AttrWoundLastTotalHours);
            entity.Stats.Remove(WalkSpeedStatKey, BehaviorName);
        }

        private void ApplySpeedModifier()
        {
            if (hitCount > 0)
            {
                entity.Stats.Set(WalkSpeedStatKey, BehaviorName, speedModifier, true);
            }
            else
            {
                entity.Stats.Remove(WalkSpeedStatKey, BehaviorName);
            }
        }

        private void SpawnBloodParticles()
        {
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
        }

        private void TryPlaceBloodTrail()
        {
            IBlockAccessor blockAccessor = entity.World.BlockAccessor;
            BlockPos surfacePos = FindGroundSurfacePosition(blockAccessor);
            if (surfacePos == null) return;

            // Avoid re-placing on the exact same block every tick while standing still
            if (lastBloodTrailPos != null && lastBloodTrailPos.Equals(surfacePos)) return;

            if (IsProtectedByLandClaim(surfacePos)) return;

            Block existingBlock = blockAccessor.GetBlock(surfacePos);
            if (!CanPlaceBloodOver(existingBlock, out bool isExistingBlood, out Block replacedCoverBlock))
            {
                return;
            }

            Block freshBloodBlock = entity.World.GetBlock(
                new AssetLocation(HuntingWoundsModSystem.ModId, $"bloodtrail-{BlockEntityBloodTrail.FreshBloodStage}")
            );
            if (freshBloodBlock == null) return;

            if (isExistingBlood)
            {
                // Stepping over older dried blood refreshes it to fresh blood without losing the original ground cover block
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

            if (blockAccessor.GetBlockEntity(surfacePos) is BlockEntityBloodTrail bloodEntity)
            {
                bloodEntity.RefreshTrail(freshBloodBlock, entity.World.Calendar.TotalDays, replacedCoverBlock);
            }

            lastBloodTrailPos = surfacePos;
        }

        /// <summary>
        /// Finds the block position directly above solid ground at the entity's feet.
        /// </summary>
        private BlockPos FindGroundSurfacePosition(IBlockAccessor blockAccessor)
        {
            BlockPos feetPos = entity.ServerPos.XYZ.AsBlockPos;

            Block feetBlock = blockAccessor.GetBlock(feetPos);
            if (feetBlock.SideSolid[BlockFacing.UP.Index])
            {
                return feetPos.UpCopy();
            }

            Block belowBlock = blockAccessor.GetBlock(feetPos.DownCopy());
            if (belowBlock.SideSolid[BlockFacing.UP.Index])
            {
                return feetPos.Copy();
            }

            return null;
        }

        private bool IsProtectedByLandClaim(BlockPos pos)
        {
            if (!HuntingWoundsModSystem.Config.RespectLandClaims) return false;

            var claims = entity.World.Claims?.Get(pos);
            return claims != null && claims.Length > 0;
        }

        private static bool CanPlaceBloodOver(Block existingBlock, out bool isExistingBlood, out Block replacedCoverBlock)
        {
            string blockPath = existingBlock.Code?.Path ?? "";

            bool isAir = existingBlock.Id == 0;
            isExistingBlood = blockPath.StartsWith("bloodtrail-");
            bool isTrampleableCover = blockPath.StartsWith("tallgrass-") || blockPath == "snowlayer-1";

            replacedCoverBlock = (!isAir && !isExistingBlood) ? existingBlock : null;
            return isAir || isExistingBlood || isTrampleableCover;
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

        /// <summary>
        /// Checks whether the animal is currently fleeing, chasing, or attacking via its AI task manager.
        /// </summary>
        private bool IsInFightOrFlightTask()
        {
            EnsureReflectionInitialized();
            if (cachedTaskManager == null) return false;

            Array activeTasks = (activeTasksProp?.GetValue(cachedTaskManager)
                              ?? activeTasksField?.GetValue(cachedTaskManager)) as Array;
            if (activeTasks == null) return false;

            foreach (object task in activeTasks)
            {
                if (task == null) continue;

                string taskTypeName = task.GetType().Name;
                bool isFleeing = taskTypeName.IndexOf("Flee", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isChasing = taskTypeName.IndexOf("SeekEntity", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isAttacking = taskTypeName.IndexOf("MeleeAttack", StringComparison.OrdinalIgnoreCase) >= 0;

                if (isFleeing || isChasing || isAttacking)
                {
                    return true;
                }
            }

            return false;
        }

        private static SimpleParticleProperties CreateBloodParticleTemplate()
        {
            return new SimpleParticleProperties(
                1, 3,                                  // Min and max particles per spawn
                ColorUtil.ToRgba(255, 150, 0, 0),      // Dark red blood color (ARGB)
                new Vec3d(), new Vec3d(),              // Min and add spawn position (updated each tick)
                new Vec3f(-1, 0, -1), new Vec3f(1, 1, 1), // Min and max particle velocity
                1.5f,                                  // Particle lifespan in seconds
                1f,                                    // Gravity multiplier
                0.5f, 1.5f,                            // Min and max particle size
                EnumParticleModel.Cube
            );
        }
    }
}
