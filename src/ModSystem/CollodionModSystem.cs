using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using ProtoBuf;

namespace Collodion
{
    [ProtoContract]
    public class PhotoTakenPacket
    {
        [ProtoMember(1)]
        public string PhotoId { get; set; } = string.Empty;
    }

    [ProtoContract]
    public class CameraLoadPlatePacket
    {
        [ProtoMember(1)]
        public bool Load { get; set; }
    }

    public partial class CollodionModSystem : ModSystem
    {
        public static CollodionModSystem? ClientInstance { get; private set; }

        public const string ClientConfigFileName = "collodion-client.json";
        public const string ServerPhotoIndexFileName = "collodion-photoindex.json";
        public CollodionClientConfig ClientConfig { get; private set; } = new CollodionClientConfig();

        public ICoreAPI? Api;
        public IClientNetworkChannel? ClientChannel;
        public IServerNetworkChannel? ServerChannel;
        internal ICoreClientAPI? ClientApi;
        private PhotoCaptureRenderer? CaptureRenderer;

        internal WetplatePhotoSync? PhotoSync;

        private PhotoLastSeenIndex? serverPhotoLastSeenIndex;
        private bool serverPhotoLastSeenDirty;
        private long? serverPhotoLastSeenFlushListenerId;

        private readonly Dictionary<string, long> clientLastPhotoSeenPingMs = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        public override void Start(ICoreAPI api)
        {
            this.Api = api;
            api.RegisterItemClass("WetplateCamera", typeof(ItemWetplateCamera));
            api.RegisterItemClass("FramedPhotograph", typeof(ItemFramedPhotograph));
            api.RegisterItemClass("GlassPlate", typeof(ItemGlassPlate));
            api.RegisterItemClass("SilveredPlate", typeof(ItemSilveredPlate));
            api.RegisterItemClass("ExposedPlate", typeof(ItemExposedPlate));
            api.RegisterItemClass("GenericPlate", typeof(ItemGenericPlate));
            api.RegisterItemClass("FinishedPhotoPlate", typeof(ItemFinishedPhotoPlate));

            api.RegisterBlockClass("GlassPlate", typeof(BlockGlassPlate));
            api.RegisterBlockClass("BlockFramedPhotograph", typeof(BlockFramedPhotograph));
            api.RegisterBlockClass("DevelopmentTray", typeof(BlockDevelopmentTray));
            api.RegisterBlockEntityClass("BlockEntityPhotograph", typeof(BlockEntityPhotograph));
            api.RegisterBlockEntityClass("BlockEntityDevelopmentTray", typeof(BlockEntityDevelopmentTray));
            
            // Register Network Channel
            api.Network.RegisterChannel("collodion")
                .RegisterMessageType(typeof(PhotoTakenPacket))
                .RegisterMessageType(typeof(CameraLoadPlatePacket))
                .RegisterMessageType(typeof(PhotoBlobRequestPacket))
                .RegisterMessageType(typeof(PhotoBlobChunkPacket))
                .RegisterMessageType(typeof(PhotoBlobAckPacket))
                .RegisterMessageType(typeof(PhotoCaptionSetPacket))
                .RegisterMessageType(typeof(PhotoSeenPacket));
        }

        public override void StartClientSide(ICoreClientAPI api)
        {
            ClientApi = api;
            ClientInstance = this;
            ClientChannel = api.Network.GetChannel("collodion");
            PhotoSync = new WetplatePhotoSync(this);

            ClientConfig = LoadOrCreateClientConfig(api);

            ClientChannel
                .SetMessageHandler<PhotoBlobChunkPacket>((p) => PhotoSync?.ClientHandleChunk(p))
                .SetMessageHandler<PhotoBlobAckPacket>((p) => PhotoSync?.ClientHandleAck(p));

            try
            {
                var asm = typeof(CollodionModSystem).Assembly;
                string ver = asm.GetName().Version?.ToString() ?? "<nover>";
                string loc = asm.Location;
                string stamp = "<unknown>";
                try
                {
                    if (!string.IsNullOrEmpty(loc))
                    {
                        stamp = System.IO.File.GetLastWriteTime(loc).ToString("yyyy-MM-dd HH:mm:ss");
                    }
                }
                catch { }

                ClientApi.ShowChatMessage($"Collodion: loaded mod dll (ver={ver}, build={stamp})");
            }
            catch { }

            // Capture screenshots after the 3D scene is blitted to the default framebuffer,
            // but before GUI/HUD is rendered (EnumRenderStage.AfterBlit).
            CaptureRenderer = new PhotoCaptureRenderer(api);
            api.Event.RegisterRenderer(CaptureRenderer, EnumRenderStage.AfterBlit, "collodion-photocapture");

#pragma warning disable CS0618 // Keep legacy command registration for compatibility
            api.RegisterCommand(
                "collodion",
                "Collodion mod commands",
                ".collodion clearcache | .collodion hud | .collodion pose | .collodion effects",
                OnWetplateClientCommand
            );
#pragma warning restore CS0618

            // Some client setups don't reliably invoke OnHeldInteractStart for held items
            // (especially when aiming at air). Poll RMB state as a fallback.
            viewfinderTickListenerId = api.Event.RegisterGameTickListener(OnClientViewfinderTick, 20, 0);

            // Patch Set3DProjection so viewfinder can zoom reliably.
            TryEnsureHarmonyProjectionZoomPatch();
            // Note: do NOT also force RenderAPI.Set3DProjection per-frame; that can lead to
            // mismatched projections (e.g., skybox-only zoom). The hook on
            // ClientMain.Set3DProjection affects the actual world projection.

            LoadPoseDeltas();
        }

        private static CollodionClientConfig LoadOrCreateClientConfig(ICoreClientAPI capi)
        {
            CollodionClientConfig? cfg = null;
            try
            {
                cfg = capi.LoadModConfig<CollodionClientConfig>(ClientConfigFileName);
            }
            catch
            {
                cfg = null;
            }

            if (cfg == null)
            {
                cfg = new CollodionClientConfig();
                try
                {
                    cfg.ClampInPlace();
                    capi.StoreModConfig(cfg, ClientConfigFileName);
                }
                catch
                {
                    // ignore
                }
            }

            cfg.ClampInPlace();
            return cfg;
        }

        private static PhotoLastSeenIndex LoadOrCreateServerPhotoLastSeenIndex(ICoreServerAPI sapi)
        {
            PhotoLastSeenIndex? idx = null;
            try
            {
                idx = sapi.LoadModConfig<PhotoLastSeenIndex>(ServerPhotoIndexFileName);
            }
            catch
            {
                idx = null;
            }

            if (idx == null)
            {
                idx = new PhotoLastSeenIndex();
                try
                {
                    idx.ClampInPlace();
                    sapi.StoreModConfig(idx, ServerPhotoIndexFileName);
                }
                catch
                {
                    // ignore
                }
            }

            idx.ClampInPlace();
            return idx;
        }

        internal void ServerTouchPhotoSeen(string photoId)
        {
            if (serverPhotoLastSeenIndex == null) return;

            serverPhotoLastSeenIndex.Touch(photoId);
            serverPhotoLastSeenDirty = true;
        }

        private void ServerMaybeFlushPhotoLastSeenIndex(ICoreServerAPI sapi)
        {
            if (!serverPhotoLastSeenDirty) return;
            if (serverPhotoLastSeenIndex == null) return;

            try
            {
                serverPhotoLastSeenIndex.ClampInPlace();
                sapi.StoreModConfig(serverPhotoLastSeenIndex, ServerPhotoIndexFileName);
                serverPhotoLastSeenDirty = false;
            }
            catch
            {
                // Keep dirty so we retry.
                serverPhotoLastSeenDirty = true;
            }
        }

        private void OnPhotoSeen(IServerPlayer player, PhotoSeenPacket packet)
        {
            if (packet == null) return;
            ServerTouchPhotoSeen(packet.PhotoId);
        }

        internal void ClientMaybeSendPhotoSeen(string photoId)
        {
            if (ClientApi == null || ClientChannel == null) return;

            int intervalSeconds = ClientConfig?.PhotoSeenPingIntervalSeconds ?? 0;
            if (intervalSeconds <= 0) return;

            photoId = WetplatePhotoSync.NormalizePhotoId(photoId);
            if (string.IsNullOrEmpty(photoId)) return;

            long nowMs;
            try
            {
                nowMs = (long)ClientApi.World.ElapsedMilliseconds;
            }
            catch
            {
                return;
            }

            if (clientLastPhotoSeenPingMs.TryGetValue(photoId, out long lastMs))
            {
                if (nowMs - lastMs < intervalSeconds * 1000L) return;
            }

            clientLastPhotoSeenPingMs[photoId] = nowMs;
            ClientChannel.SendPacket(new PhotoSeenPacket { PhotoId = photoId });
        }

        public override void StartServerSide(ICoreServerAPI api)
        {
            ServerChannel = api.Network.GetChannel("collodion");
            ServerChannel.SetMessageHandler<PhotoTakenPacket>(OnPhotoTakenReceived);
            ServerChannel.SetMessageHandler<CameraLoadPlatePacket>(OnCameraLoadPlateReceived);
            PhotoSync = new WetplatePhotoSync(this);

            serverPhotoLastSeenIndex = LoadOrCreateServerPhotoLastSeenIndex(api);
            serverPhotoLastSeenDirty = false;
            serverPhotoLastSeenFlushListenerId = api.Event.RegisterGameTickListener(_ => ServerMaybeFlushPhotoLastSeenIndex(api), 10_000);

            ServerChannel
                .SetMessageHandler<PhotoBlobRequestPacket>((player, p) => PhotoSync?.ServerHandleRequest(player, p))
                .SetMessageHandler<PhotoBlobChunkPacket>((player, p) => PhotoSync?.ServerHandleChunk(player, p))
                .SetMessageHandler<PhotoCaptionSetPacket>((player, p) => OnPhotoCaptionSet(player, p))
                .SetMessageHandler<PhotoSeenPacket>((player, p) => OnPhotoSeen(player, p));
        }

        private static readonly AssetLocation SilveredPlateItemCode = new AssetLocation("collodion", "silveredplate");
        private static readonly AssetLocation ExposedPlateItemCode = new AssetLocation("collodion", "exposedplate");

        private void OnCameraLoadPlateReceived(IServerPlayer player, CameraLoadPlatePacket packet)
        {
            if (Api == null) return;

            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (cameraStack?.Item is not ItemWetplateCamera) return;
            if (cameraSlot == null) return;

            bool wantLoad = packet?.Load != false;

            ItemSlot? offhandSlot = player.InventoryManager.OffhandHotbarSlot;
            ItemStack? offhandStack = offhandSlot?.Itemstack;

            if (wantLoad)
            {
                // Only load if currently empty.
                string alreadyLoaded = cameraStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty);
                if (!string.IsNullOrEmpty(alreadyLoaded)) return;

                if (offhandStack?.Collectible?.Code == null) return;

                AssetLocation code = offhandStack.Collectible.Code;
                bool isSupportedPlate = code == SilveredPlateItemCode || code == ExposedPlateItemCode;
                if (!isSupportedPlate) return;

                // Store the plate stack (including any attributes) inside the camera so we can unload it later.
                // IMPORTANT: clone so we don't retain a reference that could be mutated elsewhere.
                try
                {
                    cameraStack.Attributes.SetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, offhandStack.Clone());
                }
                catch
                {
                    // If we can't store state, don't consume the plate.
                    return;
                }

                if (offhandSlot == null) return;
                offhandSlot.TakeOut(1);
                offhandSlot.MarkDirty();

                cameraStack.Attributes.SetString(ItemWetplateCamera.AttrLoadedPlate, code.ToString());
                cameraSlot.MarkDirty();
                return;
            }

            // Unload
            string loaded = cameraStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty);
            if (string.IsNullOrEmpty(loaded)) return;

            // Only unload when offhand is empty.
            if (offhandSlot == null || !offhandSlot.Empty) return;

            // Restore the stored plate stack if present (preferred).
            ItemStack? stored = null;
            try
            {
                stored = cameraStack.Attributes.GetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, null);
                stored?.ResolveBlockOrItem(Api.World);
            }
            catch
            {
                stored = null;
            }

            if (stored == null)
            {
                // Fallback: reconstruct from the stored code string.
                AssetLocation loadedLoc;
                try
                {
                    loadedLoc = new AssetLocation(loaded);
                }
                catch
                {
                    return;
                }

                if (!(loadedLoc == SilveredPlateItemCode || loadedLoc == ExposedPlateItemCode))
                {
                    return;
                }

                Item? plateItem = Api.World.GetItem(loadedLoc);
                if (plateItem == null) return;
                stored = new ItemStack(plateItem);
            }

            stored.StackSize = 1;

            // Place into offhand slot.
            offhandSlot.Itemstack = stored;
            offhandSlot.MarkDirty();

            // Clear the camera state.
            cameraStack.Attributes.RemoveAttribute(ItemWetplateCamera.AttrLoadedPlate);
            cameraStack.Attributes.RemoveAttribute(ItemWetplateCamera.AttrLoadedPlateStack);
            cameraSlot.MarkDirty();
        }

        private void OnPhotoCaptionSet(IServerPlayer player, PhotoCaptionSetPacket packet)
        {
            if (Api == null || packet == null) return;

            var pos = new Vintagestory.API.MathTools.BlockPos(packet.X, packet.Y, packet.Z);
            var be = Api.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityPhotograph;
            if (be == null) return;

            // Basic sanity limit to avoid abuse.
            string caption = packet.Caption ?? string.Empty;
            if (caption.Length > 200) caption = caption.Substring(0, 200);

            be.SetCaption(caption);

            if (!string.IsNullOrEmpty(be.PhotoId))
            {
                ServerTouchPhotoSeen(be.PhotoId);
            }

            // Force a block entity update/sync.
            try
            {
                Api.World.BlockAccessor.MarkBlockEntityDirty(pos);
            }
            catch { }
        }

        private void OnPhotoTakenReceived(IServerPlayer player, PhotoTakenPacket packet)
        {
            if (Api == null || packet == null) return;

            // Verify player is holding camera to prevent cheating
            ItemSlot? cameraSlot = player.InventoryManager.ActiveHotbarSlot;
            ItemStack? cameraStack = cameraSlot?.Itemstack;
            if (cameraStack?.Item is not ItemWetplateCamera) return;
            if (cameraSlot == null) return;

            // Only allow exposure when a silvered plate is loaded.
            string loaded = cameraStack.Attributes.GetString(ItemWetplateCamera.AttrLoadedPlate, string.Empty);
            if (!string.Equals(loaded, SilveredPlateItemCode.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // Pull the stored plate stack (preferred) so we preserve wetness attributes.
            ItemStack? stored = null;
            try
            {
                stored = cameraStack.Attributes.GetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, null);
                stored?.ResolveBlockOrItem(Api.World);
            }
            catch
            {
                stored = null;
            }

            if (stored == null || stored.Item is not ItemSilveredPlate)
            {
                // If we can't prove a silvered plate was loaded, don't create an exposure.
                return;
            }

            Item? exposedItem = Api.World.GetItem(ExposedPlateItemCode);
            if (exposedItem == null) return;

            var exposedStack = new ItemStack(exposedItem);
            try
            {
                // Copy all existing plate attrs (wetness timer etc.)
                exposedStack.Attributes.MergeTree(stored.Attributes.Clone());
            }
            catch
            {
                // Best-effort; continue with just photo metadata.
            }

            // Attach photo metadata to the plate.
            string photoId = packet.PhotoId ?? string.Empty;
            exposedStack.Attributes.SetString(WetPlateAttrs.PhotoId, photoId);
            exposedStack.Attributes.SetString("timestamp", DateTime.Now.ToString());
            exposedStack.Attributes.SetString("photographer", player.PlayerName);
            exposedStack.Attributes.SetString(WetPlateAttrs.PlateStage, "exposed");

            ServerTouchPhotoSeen(photoId);

            // Keep the exposed (now photo-bearing) plate loaded in the camera.
            cameraStack.Attributes.SetItemstack(ItemWetplateCamera.AttrLoadedPlateStack, exposedStack);
            cameraStack.Attributes.SetString(ItemWetplateCamera.AttrLoadedPlate, ExposedPlateItemCode.ToString());
            cameraSlot?.MarkDirty();
        }
    }
}