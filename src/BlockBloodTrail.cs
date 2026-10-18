using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace HuntingWounds
{
    /// <summary>
    /// Represents a blood splatter block placed on the ground by a wounded animal.
    /// Automatically breaks if the supporting block underneath is removed.
    /// </summary>
    public class BlockBloodTrail : Block
    {
        public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
        {
            base.OnNeighbourBlockChange(world, pos, neibpos);

            if (!HasSolidGroundBelow(world, pos))
            {
                world.BlockAccessor.SetBlock(0, pos);
                world.BlockAccessor.MarkBlockDirty(pos);
            }
        }

        private static bool HasSolidGroundBelow(IWorldAccessor world, BlockPos pos)
        {
            Block belowBlock = world.BlockAccessor.GetBlock(pos.DownCopy());
            return belowBlock.SideSolid[BlockFacing.UP.Index];
        }
    }
}

