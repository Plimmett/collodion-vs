using System;
using System.Globalization;

namespace Collodion
{
    public partial class CollodionModSystem
    {
        private void HandleWetplateEffectsCommand(Vintagestory.API.Common.CmdArgs args)
        {
            if (ClientApi == null) return;

            string param = args.PopWord();

            if (string.IsNullOrEmpty(param) || param.Equals("show", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = WetplateEffects.LoadOrCreate(ClientApi);
                ClientApi.ShowChatMessage($"Wetplate effects: enabled={cfg.Enabled}");
                ClientApi.ShowChatMessage($"  greyscale={cfg.Greyscale} preGray RGB=({cfg.PreGrayRed:0.00}, {cfg.PreGrayGreen:0.00}, {cfg.PreGrayBlue:0.00})");
                ClientApi.ShowChatMessage($"  sepia={cfg.SepiaStrength:0.00} contrast={cfg.Contrast:0.00} brightness={cfg.Brightness:0.00}");
                ClientApi.ShowChatMessage($"  curve: shoulder={cfg.HighlightShoulder:0.00} threshold={cfg.HighlightThreshold:0.00} shadowfloor={cfg.ShadowFloor:0.00} contraststart={cfg.ContrastStart:0.00}");
                ClientApi.ShowChatMessage($"  vignette={cfg.Vignette:0.00} skyblowout={cfg.SkyBlowout:0.00} grain={cfg.Grain:0.00}");
                ClientApi.ShowChatMessage($"  realism: imperfection={cfg.Imperfection:0.00} microblur={cfg.MicroBlur:0.00} skyuneven={cfg.SkyUnevenness:0.00} skytop={cfg.SkyTopFraction:0.00} edgewarmth={cfg.EdgeWarmth:0.00}");
                ClientApi.ShowChatMessage($"  dust={cfg.DustCount} (opacity={cfg.DustOpacity:0.00})");
                ClientApi.ShowChatMessage($"  scratches={cfg.ScratchCount} (opacity={cfg.ScratchOpacity:0.00})");
                ClientApi.ShowChatMessage("Usage: .collodion effects <show|enable|disable|reset|preset|set> [param] [value]");
                return;
            }

            if (param.Equals("reset", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = new WetplateEffectsConfig();
                cfg.ClampInPlace();
                ClientApi.StoreModConfig(cfg, WetplateEffects.EffectsConfigFileName);
                CaptureRenderer?.ReloadEffectsConfig();
                ClientApi.ShowChatMessage("Wetplate: effects reset to defaults");
                return;
            }

            if (param.Equals("enable", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = WetplateEffects.LoadOrCreate(ClientApi);
                cfg.Enabled = true;
                ClientApi.StoreModConfig(cfg, WetplateEffects.EffectsConfigFileName);
                CaptureRenderer?.ReloadEffectsConfig();
                ClientApi.ShowChatMessage("Wetplate: effects enabled");
                return;
            }

            if (param.Equals("disable", StringComparison.OrdinalIgnoreCase))
            {
                var cfg = WetplateEffects.LoadOrCreate(ClientApi);
                cfg.Enabled = false;
                ClientApi.StoreModConfig(cfg, WetplateEffects.EffectsConfigFileName);
                CaptureRenderer?.ReloadEffectsConfig();
                ClientApi.ShowChatMessage("Wetplate: effects disabled");
                return;
            }

            if (param.Equals("preset", StringComparison.OrdinalIgnoreCase))
            {
                string which = args.PopWord();
                if (string.IsNullOrEmpty(which))
                {
                    ClientApi.ShowChatMessage("Wetplate: usage: .collodion effects preset <indoor|outdoor>");
                    return;
                }

                string file = which.Equals("indoor", StringComparison.OrdinalIgnoreCase) ? "collodion-effects-indoor.json"
                             : which.Equals("outdoor", StringComparison.OrdinalIgnoreCase) ? "collodion-effects-outdoor.json"
                             : string.Empty;

                if (string.IsNullOrEmpty(file))
                {
                    ClientApi.ShowChatMessage("Wetplate: preset must be 'indoor' or 'outdoor'");
                    return;
                }

                WetplateEffectsConfig? preset = null;
                try { preset = ClientApi.LoadModConfig<WetplateEffectsConfig>(file); } catch { preset = null; }

                if (preset == null)
                {
                    // Seed from current active config
                    var current = WetplateEffects.LoadOrCreate(ClientApi);
                    preset = current;
                    try { ClientApi.StoreModConfig(preset, file); } catch { }
                }

                preset.ClampInPlace();

                try
                {
                    // Activate preset by writing to the active config
                    ClientApi.StoreModConfig(preset, WetplateEffects.EffectsConfigFileName);
                    CaptureRenderer?.ReloadEffectsConfig();
                    ClientApi.ShowChatMessage($"Wetplate: preset '{which}' activated");
                }
                catch
                {
                    ClientApi.ShowChatMessage("Wetplate: failed to activate preset");
                }
                return;
            }

            if (param.Equals("set", StringComparison.OrdinalIgnoreCase))
            {
                string prop = args.PopWord();
                string valStr = args.PopWord();

                if (string.IsNullOrEmpty(prop) || string.IsNullOrEmpty(valStr))
                {
                    ClientApi.ShowChatMessage("Wetplate: usage: .collodion effects set <property> <value>");
                    ClientApi.ShowChatMessage("Properties: greyscale, pregrayred, pregraygreen, pregrayblue, sepia, contrast, brightness, shadowfloor, contraststart, highlightshoulder, highlightthreshold, vignette, skyblowout, grain, imperfection, microblur, skyunevenness, skytopfraction, edgewarmth, dust, dustopacity, scratches, scratchopacity");
                    return;
                }

                var cfg = WetplateEffects.LoadOrCreate(ClientApi);

                switch (prop.ToLowerInvariant())
                {
                    case "greyscale":
                    case "grayscale":
                    case "gray":
                    case "grey":
                        if (!bool.TryParse(valStr, out bool gs))
                        {
                            ClientApi.ShowChatMessage("Wetplate: greyscale value must be true/false");
                            return;
                        }
                        cfg.Greyscale = gs;
                        break;

                    case "pregrayred":
                    case "pregreyr":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float pgr))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.PreGrayRed = pgr;
                        break;

                    case "pregraygreen":
                    case "pregreyg":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float pgg))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.PreGrayGreen = pgg;
                        break;

                    case "pregrayblue":
                    case "pregreyb":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float pgb))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.PreGrayBlue = pgb;
                        break;

                    case "imperfection":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float imp))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Imperfection = imp;
                        break;

                    case "microblur":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float mb))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.MicroBlur = mb;
                        break;

                    case "skyunevenness":
                    case "skyuneven":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float su))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.SkyUnevenness = su;
                        break;

                    case "skytopfraction":
                    case "skytop":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float st))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.SkyTopFraction = st;
                        break;

                    case "edgewarmth":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float ew))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.EdgeWarmth = ew;
                        break;

                    case "sepia":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sep))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.SepiaStrength = sep;
                        break;
                    case "contrast":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float con))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Contrast = con;
                        break;

                    case "highlightshoulder":
                    case "shoulder":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float hs))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.HighlightShoulder = hs;
                        break;

                    case "highlightthreshold":
                    case "threshold":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float ht))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.HighlightThreshold = ht;
                        break;
                    case "brightness":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float bri))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Brightness = bri;
                        break;

                    case "shadowfloor":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sf))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.ShadowFloor = sf;
                        break;

                    case "contraststart":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float cs))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.ContrastStart = cs;
                        break;
                    case "vignette":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float vig))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Vignette = vig;
                        break;

                    case "skyblowout":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sb))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.SkyBlowout = sb;
                        break;
                    case "grain":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float gr))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.Grain = gr;
                        break;
                    case "dust":
                        if (!int.TryParse(valStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int dust))
                        {
                            ClientApi.ShowChatMessage("Wetplate: dust must be an integer");
                            return;
                        }
                        cfg.DustCount = dust;
                        break;
                    case "dustopacity":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float dop))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.DustOpacity = dop;
                        break;
                    case "scratches":
                        if (!int.TryParse(valStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int scr))
                        {
                            ClientApi.ShowChatMessage("Wetplate: scratches must be an integer");
                            return;
                        }
                        cfg.ScratchCount = scr;
                        break;
                    case "scratchopacity":
                        if (!float.TryParse(valStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float sop))
                        {
                            ClientApi.ShowChatMessage("Wetplate: value must be a number (use . not ,)");
                            return;
                        }
                        cfg.ScratchOpacity = sop;
                        break;
                    default:
                        ClientApi.ShowChatMessage($"Wetplate: unknown property '{prop}'");
                        return;
                }

                cfg.ClampInPlace();
                ClientApi.StoreModConfig(cfg, WetplateEffects.EffectsConfigFileName);
                CaptureRenderer?.ReloadEffectsConfig();
                ClientApi.ShowChatMessage($"Wetplate: set {prop} = {valStr}");
                ClientApi.ShowChatMessage("Note: effects apply to newly taken photos. Use .collodion clearcache to reload existing photos.");
                return;
            }

            ClientApi.ShowChatMessage("Wetplate: usage: .collodion effects <show|enable|disable|reset|preset|set>");
        }
    }
}
