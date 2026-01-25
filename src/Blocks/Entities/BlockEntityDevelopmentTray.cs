using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace Collodion
{
    public sealed class BlockEntityDevelopmentTray : BlockEntity
    {
        private const string AttrPlateStack = "collodionPlateStack";

        private readonly object plateLock = new object();

        private readonly object clientMeshLock = new object();
        private MeshData? clientPlateMesh;
        private bool clientMeshQueued;
        private bool clientNeedsRebuild;

        public ItemStack? PlateStack { get; private set; }

        public bool HasPlate
        {
            get
            {
                lock (plateLock)
                {
                    return PlateStack != null;
                }
            }
        }

        public override void Initialize(ICoreAPI api)
        {
            base.Initialize(api);

            // If we already have a plate (loaded from disk), ensure the chunk is rebuilt so it shows.
            if (api?.Side == EnumAppSide.Client)
            {
                clientNeedsRebuild = true;
                RequestClientMeshRebuild();
                try
                {
                    ((ICoreClientAPI)api).World.BlockAccessor.MarkBlockDirty(Pos);
                }
                catch { }
            }
        }

        public bool TryInsertPlate(ItemStack stack)
        {
            if (stack == null) return false;
            if (HasPlate) return false;

            lock (plateLock)
            {
                PlateStack = stack.Clone();
                PlateStack.StackSize = 1;
            }

            if (Api?.Side == EnumAppSide.Client)
            {
                clientNeedsRebuild = true;
                RequestClientMeshRebuild();
            }
            MarkDirty(true);
            return true;
        }

        public ItemStack? TakePlate()
        {
            if (!HasPlate) return null;

            ItemStack? stack;
            lock (plateLock)
            {
                stack = PlateStack;
                PlateStack = null;
            }

            if (Api?.Side == EnumAppSide.Client)
            {
                clientNeedsRebuild = true;
                RequestClientMeshRebuild();
            }
            MarkDirty(true);
            return stack;
        }

        public bool TrySetPlate(ItemStack stack)
        {
            if (stack == null) return false;

            lock (plateLock)
            {
                PlateStack = stack;
                PlateStack.StackSize = 1;
            }

            if (Api?.Side == EnumAppSide.Client)
            {
                clientNeedsRebuild = true;
                RequestClientMeshRebuild();
            }
            MarkDirty(true);
            return true;
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
        {
            base.FromTreeAttributes(tree, worldAccessForResolve);

            try
            {
                ItemStack? loaded = tree.GetItemstack(AttrPlateStack, null);
                loaded?.ResolveBlockOrItem(worldAccessForResolve);

                bool changed;
                lock (plateLock)
                {
                    changed = (PlateStack == null) != (loaded == null)
                        || (PlateStack?.Collectible?.Code != loaded?.Collectible?.Code);
                    PlateStack = loaded;
                }

                if (changed && Api?.Side == EnumAppSide.Client)
                {
                    clientNeedsRebuild = true;
                    RequestClientMeshRebuild();
                    try
                    {
                        ((ICoreClientAPI)Api).World.BlockAccessor.MarkBlockDirty(Pos);
                    }
                    catch { }
                }
            }
            catch
            {
                lock (plateLock)
                {
                    PlateStack = null;
                }

                if (Api?.Side == EnumAppSide.Client)
                {
                    clientNeedsRebuild = true;
                    RequestClientMeshRebuild();
                    try
                    {
                        ((ICoreClientAPI)Api).World.BlockAccessor.MarkBlockDirty(Pos);
                    }
                    catch { }
                }
            }
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);

            ItemStack? toSave;
            lock (plateLock)
            {
                toSave = PlateStack;
            }

            if (toSave != null) tree.SetItemstack(AttrPlateStack, toSave);
            else tree.RemoveAttribute(AttrPlateStack);
        }

        public override bool OnTesselation(ITerrainMeshPool mesher, ITesselatorAPI tessThreadTesselator)
        {
            if (Api?.Side != EnumAppSide.Client)
            {
                return base.OnTesselation(mesher, tessThreadTesselator);
            }

            // OnTesselation runs on the tesselation thread: only read cached meshes here.
            if (clientNeedsRebuild)
            {
                clientNeedsRebuild = false;
                RequestClientMeshRebuild();
            }

            MeshData? plateMesh;
            lock (clientMeshLock)
            {
                plateMesh = clientPlateMesh;
            }

            if (plateMesh != null)
            {
                mesher.AddMeshData(plateMesh.Clone());
            }

            // Return false so the normal tray block mesh still renders.
            return base.OnTesselation(mesher, tessThreadTesselator);
        }

        private void RequestClientMeshRebuild()
        {
            if (Api?.Side != EnumAppSide.Client) return;

            ICoreClientAPI capi = (ICoreClientAPI)Api;
            lock (clientMeshLock)
            {
                if (clientMeshQueued) return;
                clientMeshQueued = true;
            }

            try
            {
                capi.Event.EnqueueMainThreadTask(() =>
                {
                    lock (clientMeshLock) clientMeshQueued = false;
                    BuildClientMesh(capi);
                }, "collodion-devtray-rebuild");
            }
            catch
            {
                lock (clientMeshLock) clientMeshQueued = false;
            }
        }

        private void BuildClientMesh(ICoreClientAPI capi)
        {
            if (capi == null) return;

            ItemStack? plate;
            lock (plateLock)
            {
                plate = PlateStack?.Clone();
            }

            if (plate?.Collectible?.Code == null)
            {
                lock (clientMeshLock) clientPlateMesh = null;
                MarkDirty(true);
                return;
            }

            AssetLocation texBase = plate.Collectible.Code.Path switch
            {
                "exposedplate" => new AssetLocation("collodion", "item/plate-exposed"),
                "developedplate" => new AssetLocation("collodion", "item/plate-developed"),
                "finishedphotoplate" => new AssetLocation("collodion", "item/plate-finished"),
                _ => new AssetLocation("collodion", "item/plate-rough")
            };

            AssetLocation pngLoc = new AssetLocation(texBase.Domain, $"textures/{texBase.Path}.png");
            IAsset? asset = capi.Assets.TryGet(pngLoc);

            TextureAtlasPosition texPos = capi.BlockTextureAtlas.UnknownTexturePosition;
            if (asset != null)
            {
                try
                {
                    AssetLocation atlasKey = new AssetLocation("collodion", $"devtrayplate-{texBase.Path.Replace('/', '-')}");
                    capi.BlockTextureAtlas.GetOrInsertTexture(
                        atlasKey,
                        out int _,
                        out texPos,
                        () => asset.ToBitmap(capi),
                        0.05f
                    );
                }
                catch
                {
                    texPos = capi.BlockTextureAtlas.UnknownTexturePosition;
                }
            }

            // Render as a thin, double-sided quad inside the tray.
            // Using an explicit quad avoids terrain-mesh edge cases with missing per-face counts.
            const float inset = 1f / 16f;
            const float baseTopY = 2f / 16f;
            const float yEps = 0.002f;

            float x1 = inset;
            float x2 = 1f - inset;
            float z1 = inset;
            float z2 = 1f - inset;
            float y = baseTopY + yEps;

            MeshData mesh = CreateDoubleSidedUpQuad(x1, z1, x2, z2, y).WithTexPos(texPos);
            mesh.Rgba.Fill((byte)255);

            lock (clientMeshLock)
            {
                clientPlateMesh = mesh;
            }

            MarkDirty(true);
            try
            {
                capi.World.BlockAccessor.MarkBlockDirty(Pos);
            }
            catch { }
        }

        private static MeshData CreateDoubleSidedUpQuad(float x1, float z1, float x2, float z2, float y)
        {
            // Terrain-mesh expectations: explicit face counts + packed normals.
            MeshData m = new MeshData(capacityVertices: 8, capacityIndices: 12, withNormals: false, withUv: true, withRgba: true, withFlags: true);

            // Two quads (up + down) sharing the same XYZ.
            m.SetXyz(new float[]
            {
                x1, y, z1,
                x2, y, z1,
                x2, y, z2,
                x1, y, z2,
                x1, y, z1,
                x2, y, z1,
                x2, y, z2,
                x1, y, z2
            });

            // UVs in 0..1 range (BL, BR, TR, TL) per quad.
            m.SetUv(new float[]
            {
                0f, 1f,
                1f, 1f,
                1f, 0f,
                0f, 0f,
                0f, 1f,
                1f, 1f,
                1f, 0f,
                0f, 0f
            });

            m.SetVerticesCount(8);

            // One texture index per face.
            m.TextureIndices = new byte[2];
            m.TextureIndicesCount = 2;

            // Face IDs (up + down).
            m.XyzFaces = new byte[] { 4, 5 };
            m.XyzFacesCount = 2;

            // Default render pass (solid).
            m.RenderPassesAndExtraBits = new short[] { 0, 0 };
            m.RenderPassCount = 2;

            // Packed normals into Flags.
            int packedUp = VertexFlags.PackNormal(0, 1, 0);
            int packedDown = VertexFlags.PackNormal(0, -1, 0);
            for (int i = 0; i < 4; i++) m.Flags[i] = packedUp;
            for (int i = 4; i < 8; i++) m.Flags[i] = packedDown;

            // Up face: standard winding. Down face: reversed winding.
            m.SetIndices(new int[]
            {
                0, 1, 2, 0, 2, 3,
                4, 6, 5, 4, 7, 6
            });
            m.SetIndicesCount(12);

            return m;
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder dsc)
        {
            base.GetBlockInfo(forPlayer, dsc);

            ItemStack? plate;
            lock (plateLock)
            {
                plate = PlateStack;
            }

            if (plate?.Collectible?.Code != null)
            {
                dsc.AppendLine($"Plate: {plate.Collectible.Code}");
            }
            else
            {
                dsc.AppendLine("Plate: (none)");
            }
        }
    }
}
