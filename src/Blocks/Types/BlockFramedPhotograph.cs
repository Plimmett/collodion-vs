using Vintagestory.API.Common;

namespace Collodion
{
    public class BlockFramedPhotograph : BlockPhotographBase
    {
        private static readonly AssetLocation FramedItemCode = new AssetLocation("collodion:framedphotograph");

        private static bool IsPlankBlock(ItemStack? stack)
        {
            Block? block = stack?.Block;
            string path = block?.Code?.Path ?? string.Empty;
            return path.IndexOf("planks", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        protected override AssetLocation PhotoItemCode => FramedItemCode;

        protected override string PlacedInfoName => "Framed Photograph";

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            // Allow re-skinning the frame using any plank block.
            // Works for both ground-placed and wall-mounted frames.
            string path = Code?.Path ?? string.Empty;
            if (path.StartsWith("framedphotographground", System.StringComparison.OrdinalIgnoreCase)
                || path.StartsWith("framedphotographwall", System.StringComparison.OrdinalIgnoreCase))
            {
                ItemStack? held = byPlayer.InventoryManager?.ActiveHotbarSlot?.Itemstack;
                if (IsPlankBlock(held))
                {
                    if (world.Side == EnumAppSide.Server)
                    {
                        if (world.BlockAccessor.GetBlockEntity(blockSel.Position) is BlockEntityPhotograph be)
                        {
                            string plankBlockCode = held!.Block!.Code.ToString();
                            be.SetFramePlankBlockCode(plankBlockCode);

                            bool isCreative = byPlayer.WorldData?.CurrentGameMode == EnumGameMode.Creative;
                            if (!isCreative)
                            {
                                ItemSlot? slot = byPlayer.InventoryManager?.ActiveHotbarSlot;
                                slot?.TakeOut(1);
                                slot?.MarkDirty();
                            }
                        }
                    }

                    // Prevent pickup while applying.
                    return true;
                }
            }

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
