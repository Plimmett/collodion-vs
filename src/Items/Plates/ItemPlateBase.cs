using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Collodion
{
    public abstract class ItemPlateBase : Item
    {
        public override void OnBeforeRender(ICoreClientAPI capi, ItemStack itemstack, EnumItemRenderTarget target, ref ItemRenderInfo renderinfo)
        {
#pragma warning disable CS0618 // Preserve FP pose behavior on older targets
            base.OnBeforeRender(capi, itemstack, target, ref renderinfo);
#pragma warning restore CS0618

            try
            {
                var modSys = capi.ModLoader.GetModSystem<CollodionModSystem>();

                // Inventory/hover preview uses the GUI render target and applies its own animated rotation.
                // For alignment tuning, we want a stable, non-spinning preview.
                if (target == EnumItemRenderTarget.Gui)
                {
                    var t = new ModelTransform();
                    t.Origin.X = 0.5f;
                    t.Origin.Y = 0.5f;
                    t.Origin.Z = 0.5f;
                    t.ScaleXYZ = new FastVec3f(1f, 1f, 1f);
                    renderinfo.Transform = t;
                }

                // NOTE: EnumItemRenderTarget.HandFp is obsolete in newer API, but still correct on 1.21.6.
#pragma warning disable CS0618
                string poseKey = target switch
                {
                    EnumItemRenderTarget.HandFp => "plate-fp",
                    EnumItemRenderTarget.HandTp => "plate-tp",
                    EnumItemRenderTarget.Gui => "plate-gui",
                    EnumItemRenderTarget.Ground => "plate-ground",
                    _ => string.Empty
                };
#pragma warning restore CS0618

                if (!string.IsNullOrEmpty(poseKey))
                {
                    RenderPoseUtil.ApplyPoseDelta(modSys, poseKey, ref renderinfo);
                }
            }
            catch
            {
                // ignore
            }
        }
    }

    public sealed class ItemGenericPlate : ItemPlateBase
    {
    }
}
