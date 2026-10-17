using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace HuntingWounds
{
    public class HuntingWoundsModSystem : ModSystem
    {
        public override void Start(ICoreAPI api)
        {
            base.Start(api);
            api.RegisterEntityBehaviorClass("huntingwounds", typeof(EntityBehaviorHuntingWounds));
            api.RegisterBlockClass("BlockBloodTrail", typeof(BlockBloodTrail));
            api.RegisterBlockEntityClass("BloodTrail", typeof(BlockEntityBloodTrail));
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
            }
        }
    }

    public class BlockEntityBloodTrail : BlockEntity
    {
        private const double TotalLifespanDays = 5.0;
        private double createdTotalDays = -1;
        private int replacedBlockId = 0;

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            if (api.Side == EnumAppSide.Server)
            {
                if (createdTotalDays < 0)
                {
                    createdTotalDays = api.World.Calendar.TotalDays;
                }

                // Check age every 5 seconds on the server
                RegisterGameTickListener(OnServerTick, 5000);
            }
        }

        public void RefreshTrail(Block freshBlock, double currentTotalDays, int previousBlockId)
        {
            this.Block = freshBlock;
            createdTotalDays = currentTotalDays;
            if (replacedBlockId == 0 && previousBlockId != 0)
            {
                replacedBlockId = previousBlockId;
            }
            MarkDirty(true);
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

            double ageDays = Api.World.Calendar.TotalDays - createdTotalDays;

            // After the full 5-day duration, remove the brown blood trail (and restore any trampled grass)
            if (ageDays >= TotalLifespanDays)
            {
                int restoreId = 0;
                if (replacedBlockId > 0)
                {
                    Block prevBlock = Api.World.GetBlock(replacedBlockId);
                    if (prevBlock != null && prevBlock.Code != null && !prevBlock.Code.Path.StartsWith("bloodtrail"))
                    {
                        restoreId = replacedBlockId;
                    }
                }

                Api.World.BlockAccessor.SetBlock(restoreId, Pos);
                Api.World.BlockAccessor.MarkBlockDirty(Pos);
                return;
            }

            // First half of duration (0 to 2.5 days): Red (day0)
            // Second half of duration (2.5 to 5.0 days): Brown (day3)
            string targetVariant = (ageDays < TotalLifespanDays * 0.5) ? "day0" : "day3";

            if (currentBlock.Variant != null && currentBlock.Variant.TryGetValue("stage", out string currentStage) && currentStage != targetVariant)
            {
                Block nextStageBlock = Api.World.GetBlock(new AssetLocation("huntingwounds", $"bloodtrail-{targetVariant}"));
                if (nextStageBlock != null)
                {
                    // ExchangeBlock swaps the block variant while keeping this BlockEntity intact
                    Api.World.BlockAccessor.ExchangeBlock(nextStageBlock.Id, Pos);
                    this.Block = nextStageBlock;
                    MarkDirty(true);
                }
            }
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            createdTotalDays = tree.GetDouble("createdTotalDays", -1);
            replacedBlockId = tree.GetInt("replacedBlockId", 0);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble("createdTotalDays", createdTotalDays);
            tree.SetInt("replacedBlockId", replacedBlockId);
        }
    }

    public class EntityBehaviorHuntingWounds : EntityBehavior
    {
        // TODO: Implement pheromone trails
        private float speedPenaltyPerHit = -0.15f;
        private float speedBoostPerHit = 0.20f;
        private int hitCount = 0;
        private float speedModifier = 0f;
        private SimpleParticleProperties bloodParticles;
        private float particleTimer = 0f;
        private BlockPos lastBloodTrailPos;
        private bool isHuntable = true;

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
            ApplySpeedModifier();
        }

        public override void AfterInitialized(bool onFirstSpawn)
        {
            base.AfterInitialized(onFirstSpawn);
            // Ensure the entity is harvestable (drops food/resources when butchered)
            isHuntable = entity.HasBehavior("harvestable");
        }

        public override void OnEntityReceiveDamage(DamageSource damageSource, ref float damage)
        {
            base.OnEntityReceiveDamage(damageSource, ref damage);

            if (!isHuntable || entity.Api.Side != EnumAppSide.Server) return;

            if (damageSource.Type != EnumDamageType.Heal && damage > 0)
            {
                hitCount++;
                entity.WatchedAttributes.SetInt("woundHitCount", hitCount);

                // 80% chance to slow down, 20% chance for an adrenaline speed boost
                if (entity.World.Rand.NextDouble() < 0.8)
                {
                    speedModifier += speedPenaltyPerHit;
                }
                else
                {
                    speedModifier += speedBoostPerHit;
                }

                // Cap penalty to prevent negative movement speed
                if (speedModifier < -0.9f) speedModifier = -0.9f;

                entity.WatchedAttributes.SetFloat("woundSpeedModifier", speedModifier);
                ApplySpeedModifier();

                // Leave an immediate blood splatter where the animal was hit
                TryPlaceBloodTrail();
            }
        }

        private void ApplySpeedModifier()
        {
            if (hitCount > 0)
            {
                entity.Stats.Set("walkspeed", "huntingwounds", speedModifier, true);
            }
        }

        public override void OnGameTick(float deltaTime)
        {
            base.OnGameTick(deltaTime);

            if (hitCount <= 0 || !entity.Alive || entity.Api.Side != EnumAppSide.Server) return;

            particleTimer += deltaTime;
            if (particleTimer > 0.5f)
            {
                particleTimer = 0f;
                bloodParticles.MinPos = entity.Pos.XYZ;
                bloodParticles.AddPos.Set(entity.SelectionBox.X2 - entity.SelectionBox.X1, 0, entity.SelectionBox.Z2 - entity.SelectionBox.Z1);
                entity.World.SpawnParticles(bloodParticles);

                TryPlaceBloodTrail();
            }
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

            Block existingBlock = blockAccessor.GetBlock(surfacePos);
            string existingPath = existingBlock.Code?.Path ?? "";

            bool isAir = existingBlock.Id == 0;
            bool isExistingBlood = existingPath.StartsWith("bloodtrail-");
            bool isTrampleableCover = existingPath.StartsWith("tallgrass-") || existingPath == "snowlayer-1";

            if (!isAir && !isExistingBlood && !isTrampleableCover) return;

            Block freshBloodBlock = entity.World.GetBlock(new AssetLocation("huntingwounds", "bloodtrail-day0"));
            if (freshBloodBlock == null) return;

            int previousBlockId = (!isAir && !isExistingBlood) ? existingBlock.Id : 0;

            if (isExistingBlood)
            {
                // If stepping over older blood, refresh it to day0 without losing its original replacedBlockId
                if (existingBlock.Id != freshBloodBlock.Id)
                {
                    blockAccessor.ExchangeBlock(freshBloodBlock.Id, surfacePos);
                }
            }
            else
            {
                blockAccessor.SetBlock(freshBloodBlock.Id, surfacePos);
            }

            if (blockAccessor.GetBlockEntity(surfacePos) is BlockEntityBloodTrail beBlood)
            {
                beBlood.RefreshTrail(freshBloodBlock, entity.World.Calendar.TotalDays, previousBlockId);
            }

            lastBloodTrailPos = surfacePos;
        }
    }
}