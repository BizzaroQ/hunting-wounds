using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace HuntingWounds
{
    /// <summary>
    /// Tracks the age of a placed blood trail on the server, transitions it from
    /// fresh red blood to dried brown blood halfway through its lifespan, and
    /// restores any trampled grass or thin snow once it expires.
    /// </summary>
    public class BlockEntityBloodTrail : BlockEntity
    {
        public const string FreshBloodStage = "day0";
        public const string DriedBloodStage = "day3";
        private const int AgeCheckIntervalMs = 30000;

        private const string AttrCreatedTotalDays = "createdTotalDays";
        private const string AttrReplacedBlockId = "replacedBlockId";
        private const string AttrReplacedBlockCode = "replacedBlockCode";

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
                RegisterGameTickListener(OnServerTick, AgeCheckIntervalMs);
            }
        }

        /// <summary>
        /// Resets the trail's age to fresh and remembers any ground cover block (tall grass or thin snow)
        /// that was originally replaced when the first blood drop landed here.
        /// </summary>
        public void RefreshTrail(Block freshBlock, double currentTotalDays, Block previousBlock)
        {
            this.Block = freshBlock;
            createdTotalDays = currentTotalDays;

            bool hasSavedCoverBlock = replacedBlockId != 0 || !string.IsNullOrEmpty(replacedBlockCode);
            if (!hasSavedCoverBlock && previousBlock != null && previousBlock.Id != 0)
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
            if (!IsBloodTrailBlock(currentBlock))
            {
                return;
            }

            if (createdTotalDays < 0)
            {
                createdTotalDays = Api.World.Calendar.TotalDays;
            }

            double lifespanDays = HuntingWoundsModSystem.Config?.BloodTrailLifespanDays ?? 1.0;
            double ageDays = Api.World.Calendar.TotalDays - createdTotalDays;

            if (ageDays >= lifespanDays)
            {
                RemoveTrailAndRestoreGround();
                return;
            }

            UpdateVisualStage(currentBlock, ageDays, lifespanDays);
        }

        private void RemoveTrailAndRestoreGround()
        {
            int blockIdToRestore = ResolveOriginalCoverBlockId();
            Api.World.BlockAccessor.SetBlock(blockIdToRestore, Pos);
            Api.World.BlockAccessor.MarkBlockDirty(Pos);
        }

        private int ResolveOriginalCoverBlockId()
        {
            Block originalBlock = null;

            if (!string.IsNullOrEmpty(replacedBlockCode))
            {
                originalBlock = Api.World.GetBlock(new AssetLocation(replacedBlockCode));
            }

            if (originalBlock == null && replacedBlockId > 0)
            {
                originalBlock = Api.World.GetBlock(replacedBlockId);
            }

            if (originalBlock?.Code != null && !IsBloodTrailBlock(originalBlock))
            {
                return originalBlock.Id;
            }

            return 0;
        }

        private void UpdateVisualStage(Block currentBlock, double ageDays, double lifespanDays)
        {
            // First half of duration: Fresh Red (day0)
            // Second half of duration: Dried Brown (day3)
            string targetStage = (ageDays < lifespanDays * 0.5) ? FreshBloodStage : DriedBloodStage;

            if (currentBlock.Variant != null &&
                currentBlock.Variant.TryGetValue("stage", out string currentStage) &&
                currentStage != targetStage)
            {
                Block nextStageBlock = Api.World.GetBlock(new AssetLocation(HuntingWoundsModSystem.ModId, $"bloodtrail-{targetStage}"));
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

        private static bool IsBloodTrailBlock(Block block)
        {
            return block?.Code != null && block.Code.Path.StartsWith("bloodtrail-");
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);
            createdTotalDays = tree.GetDouble(AttrCreatedTotalDays, -1);
            replacedBlockId = tree.GetInt(AttrReplacedBlockId, 0);
            replacedBlockCode = tree.GetString(AttrReplacedBlockCode, "");
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetDouble(AttrCreatedTotalDays, createdTotalDays);
            tree.SetInt(AttrReplacedBlockId, replacedBlockId);
            if (!string.IsNullOrEmpty(replacedBlockCode))
            {
                tree.SetString(AttrReplacedBlockCode, replacedBlockCode);
            }
        }
    }
}

