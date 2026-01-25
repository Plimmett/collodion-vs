using System;
using SkiaSharp;

namespace Collodion
{
    public static partial class WetplateEffects
    {
        private static void DrawDust(SKCanvas canvas, int w, int h, Random rng, WetplateEffectsConfig cfg)
        {
            if (cfg.DustCount <= 0 || cfg.DustOpacity <= 0.001f) return;

            using var paint = new SKPaint
            {
                IsAntialias = true,
                BlendMode = SKBlendMode.Screen,
                Color = new SKColor(255, 255, 255, (byte)(255 * cfg.DustOpacity))
            };

            int count = cfg.DustCount;
            for (int i = 0; i < count; i++)
            {
                float x;
                float y;
                if (cfg.Imperfection > 0.001f && rng.NextDouble() < (0.65 * cfg.Imperfection))
                {
                    SampleEdgeBiasedPoint(w, h, rng, out x, out y);
                }
                else
                {
                    x = (float)rng.NextDouble() * w;
                    y = (float)rng.NextDouble() * h;
                }
                float r = 0.4f + (float)rng.NextDouble() * 1.8f;

                // A few larger flecks
                if ((i % 37) == 0) r *= 2.2f;

                canvas.DrawCircle(x, y, r, paint);
            }
        }

        private static void DrawScratches(SKCanvas canvas, int w, int h, Random rng, WetplateEffectsConfig cfg)
        {
            if (cfg.ScratchCount <= 0 || cfg.ScratchOpacity <= 0.001f) return;

            using var paint = new SKPaint
            {
                IsAntialias = true,
                StrokeCap = SKStrokeCap.Round,
                BlendMode = SKBlendMode.Screen,
                Color = new SKColor(255, 255, 255, (byte)(255 * cfg.ScratchOpacity))
            };

            int count = cfg.ScratchCount;
            for (int i = 0; i < count; i++)
            {
                float x0 = (float)rng.NextDouble() * w;
                float y0 = (float)rng.NextDouble() * h;

                // Mostly vertical-ish scratches.
                float angle = (-0.15f + 0.30f * (float)rng.NextDouble());
                float len = (0.35f + 0.85f * (float)rng.NextDouble()) * Math.Max(w, h);

                float dx = (float)Math.Sin(angle) * len;
                float dy = (float)Math.Cos(angle) * len;

                paint.StrokeWidth = 0.4f + 1.2f * (float)rng.NextDouble();
                canvas.DrawLine(x0, y0, x0 + dx, y0 + dy, paint);

                // Occasionally add a darker scratch.
                if ((i % 5) == 0)
                {
                    paint.BlendMode = SKBlendMode.Multiply;
                    paint.Color = new SKColor(0, 0, 0, (byte)(255 * (cfg.ScratchOpacity * 0.65f)));
                    paint.StrokeWidth *= 0.7f;
                    canvas.DrawLine(x0 + 1, y0, x0 + dx + 1, y0 + dy, paint);
                    paint.BlendMode = SKBlendMode.Screen;
                    paint.Color = new SKColor(255, 255, 255, (byte)(255 * cfg.ScratchOpacity));
                }
            }
        }

        private static void SampleEdgeBiasedPoint(int w, int h, Random rng, out float x, out float y)
        {
            // Pick an edge and sample close to it with an exponential falloff.
            int edge = rng.Next(4); // 0=top,1=right,2=bottom,3=left

            // Exponential-ish: smaller values are more likely.
            float t = (float)rng.NextDouble();
            float d = (float)(-Math.Log(Math.Max(1e-6, t))); // 0..inf
            float maxD = Math.Min(w, h) * 0.22f;
            d = Math.Min(maxD, d * (maxD / 3.0f));

            switch (edge)
            {
                case 0: // top
                    x = (float)rng.NextDouble() * w;
                    y = d;
                    break;
                case 1: // right
                    x = (w - 1) - d;
                    y = (float)rng.NextDouble() * h;
                    break;
                case 2: // bottom
                    x = (float)rng.NextDouble() * w;
                    y = (h - 1) - d;
                    break;
                default: // left
                    x = d;
                    y = (float)rng.NextDouble() * h;
                    break;
            }
        }
    }
}
