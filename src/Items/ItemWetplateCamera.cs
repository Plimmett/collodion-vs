using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public class ItemWetplateCamera : Item
    {
        public const string AttrLoadedPlate = "collodionLoadedPlate";
        public const string AttrLoadedPlateStack = "collodionLoadedPlateStack";

        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
    #pragma warning disable CS0618 // Preserve FP pose behavior on older targets
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);

            var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();

            string poseTarget = target switch
            {
                EnumItemRenderTarget.HandFp => "fp",
                EnumItemRenderTarget.HandTp => "tp",
                EnumItemRenderTarget.Gui => "gui",
                _ => string.Empty
            };
#pragma warning restore CS0618

            if (string.IsNullOrEmpty(poseTarget)) return;

            // Inventory/hover preview uses the GUI render target and applies its own animated rotation.
            // Provide a centered, fully-specified baseline transform so the preview spins in place.
            if (target == EnumItemRenderTarget.Gui)
            {
                var t = new ModelTransform();
                t.Translation = new FastVec3f(0f, 0f, 0f);
                t.Rotation = new FastVec3f(0f, 0f, 0f);
                t.Origin.X = 0.5f;
                t.Origin.Y = 0.5f;
                t.Origin.Z = 0.5f;
                t.Rotate = true;
                t.ScaleXYZ = new FastVec3f(1f, 1f, 1f);
                renderinfo.Transform = t;
            }

            // IMPORTANT: do NOT mutate renderinfo.Transform in-place.
            // VS may reuse the same ModelTransform instance between frames; adding deltas
            // every frame causes accumulation (item drifts away / disappears) and "reset"
            // won't appear to restore it until restart.
            RenderPoseUtil.ApplyPoseDelta(modSys, poseTarget, ref renderinfo);
        }

        public override void OnHeldAttackStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, ref EnumHandHandling handling)
        {
            base.OnHeldAttackStart(slot, byEntity, blockSel, entitySel, ref handling);

            if (api.Side != EnumAppSide.Client) return;

            var modSys = api.ModLoader.GetModSystem<CollodionModSystem>();
            if (!modSys.IsViewfinderActive)
            {
                // Not in viewfinder mode: do not take a photo; allow default left click behavior.
                return;
            }

            // In viewfinder: left click acts as shutter.
            handling = EnumHandHandling.PreventDefault;
            modSys.RequestPhotoCaptureFromViewfinder(byEntity, silentIfBusy: true);
        }

        public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handling)
        {
            handling = EnumHandHandling.PreventDefault;

            // Viewfinder mode is driven by the client tick polling RMB state in CollodionModSystem.
            // We still prevent default use/interact while holding the camera.
        }

        public override void OnHeldInteractStop(float secondsUsed, ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel, EntitySelection entitySel)
        {
            base.OnHeldInteractStop(secondsUsed, slot, byEntity, blockSel, entitySel);
            if (api.Side != EnumAppSide.Client) return;

            // Viewfinder mode exit is driven by tick polling.
        }

        public override void GetHeldItemInfo(ItemSlot inSlot, System.Text.StringBuilder dsc, IWorldAccessor world, bool withDebugInfo)
        {
            base.GetHeldItemInfo(inSlot, dsc, world, withDebugInfo);
            dsc.AppendLine("RMB + LMB to look through the viewfinder and expose a plate.");

            string? loadedPlate = inSlot?.Itemstack?.Attributes?.GetString(AttrLoadedPlate, null);
            if (!string.IsNullOrEmpty(loadedPlate))
            {
                dsc.AppendLine($"Loaded plate: {loadedPlate}");
            }
            else
            {
                dsc.AppendLine("Loaded plate: (none)");
            }

            if (string.IsNullOrEmpty(loadedPlate))
            {
                dsc.AppendLine("Shift+Right click with a Silvered Plate in offhand to load.");
            }
            else
            {
                dsc.AppendLine("Shift+Right click with empty offhand to unload.");
            }
        }
    }
}
