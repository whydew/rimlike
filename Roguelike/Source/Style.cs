using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace Roguelike
{
    /// <summary>
    /// Procedural textures and drawing helpers for the mod's UI: card bodies, soft glows, sheen sweeps, vignettes,
    /// and a tiny particle system. Everything is generated at startup so the mod ships no art beyond node icons.
    /// </summary>
    [StaticConstructorOnStartup]
    public static class Style
    {
        public static readonly Texture2D CardBody = VerticalGradient(new Color(0.17f, 0.17f, 0.19f), new Color(0.07f, 0.07f, 0.08f), 128, grain: 0.025f);
        public static readonly Texture2D Glow = Radial(64, 2.2f);
        public static readonly Texture2D SoftGlow = Radial(64, 1.2f);
        public static readonly Texture2D Vignette = Radial(128, 0.6f, invert: true);
        public static readonly Texture2D Sheen = SheenBand(64);
        public static readonly Texture2D BannerFade = HorizontalFade(64);
        public static readonly Texture2D White = SolidColorMaterials.NewSolidColorTexture(Color.white);
        /// <summary>An anti-aliased filled circle and a ring, for map nodes.</summary>
        public static readonly Texture2D Disc = Circle(64, 0f);
        public static readonly Texture2D Ring = Circle(64, 0.16f);

        public static readonly Color Gold = new Color(0.93f, 0.78f, 0.42f);
        public static readonly Color Parchment = new Color(0.92f, 0.89f, 0.82f);

        public static float Now => Time.realtimeSinceStartup;

        // ---------- easing ----------

        public static float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);

        public static float EaseInCubic(float t) => Mathf.Pow(Mathf.Clamp01(t), 3f);

        /// <summary>Overshoots slightly before settling, for a satisfying "deal".</summary>
        public static float EaseOutBack(float t)
        {
            t = Mathf.Clamp01(t);
            const float c1 = 1.70158f, c3 = c1 + 1f;
            return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);
        }

        public static float Pulse(float speed = 2f, float phase = 0f) => 0.5f + 0.5f * Mathf.Sin(Now * speed + phase);

        // ---------- drawing ----------

        public static void Tex(Rect r, Texture tex, Color c)
        {
            var old = GUI.color;
            GUI.color = new Color(c.r, c.g, c.b, c.a * old.a);
            GUI.DrawTexture(r, tex);
            GUI.color = old;
        }

        public static void Box(Rect r, Color c) => Tex(r, White, c);

        public static void Border(Rect r, Color c, float width)
        {
            Box(new Rect(r.x, r.y, r.width, width), c);
            Box(new Rect(r.x, r.yMax - width, r.width, width), c);
            Box(new Rect(r.x, r.y, width, r.height), c);
            Box(new Rect(r.xMax - width, r.y, width, r.height), c);
        }

        /// <summary>A glow centred on <paramref name="r"/> and <paramref name="spread"/> times its size.</summary>
        public static void GlowAround(Rect r, Color c, float spread = 1.35f, Texture2D tex = null)
        {
            var g = new Rect(r.center.x - r.width * spread / 2f, r.center.y - r.height * spread / 2f, r.width * spread, r.height * spread);
            Tex(g, tex ?? Glow, c);
        }

        /// <summary>A diagonal light band sweeping across <paramref name="r"/>; <paramref name="t"/> runs 0..1.</summary>
        public static void SheenSweep(Rect r, float t, float alpha)
        {
            if (alpha <= 0f) return;
            GUI.BeginGroup(r);
            float w = r.width * 0.45f;
            float x = Mathf.Lerp(-w * 1.5f, r.width + w * 0.5f, t);
            var m = GUI.matrix;
            GUIUtility.RotateAroundPivot(18f, new Vector2(x + w / 2f, r.height / 2f));
            Tex(new Rect(x, -r.height * 0.25f, w, r.height * 1.5f), Sheen, new Color(1f, 1f, 1f, alpha));
            GUI.matrix = m;
            GUI.EndGroup();
        }

        /// <summary>Text with a soft dark shadow so it reads on any background.</summary>
        public static void ShadowLabel(Rect r, string text, Color color, float shadow = 0.75f)
        {
            var old = GUI.color;
            GUI.color = new Color(0f, 0f, 0f, shadow * old.a);
            Widgets.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text);
            GUI.color = new Color(color.r, color.g, color.b, color.a * old.a);
            Widgets.Label(r, text);
            GUI.color = old;
        }

        /// <summary>
        /// A card in the mod's style: shadow, coloured aura, gradient body, frame and a banner. Selected cards get a gold
        /// frame and a strong breathing aura. Returns the area below the banner for the card's content.
        /// </summary>
        public static Rect CardFrame(Rect r, Color kind, float hover, bool selected, string banner, float phase = 0f)
        {
            Tex(new Rect(r.x + 8f, r.y + 16f, r.width, r.height).ExpandedBy(16f), SoftGlow, new Color(0f, 0f, 0f, 0.65f));
            float aura = selected ? 0.45f + 0.2f * Pulse(2.6f, phase) : 0.1f + 0.35f * hover;
            var auraColor = selected ? Gold : kind;
            GlowAround(r, new Color(auraColor.r, auraColor.g, auraColor.b, aura), 1.25f, SoftGlow);
            Tex(r, CardBody, Color.white);
            Box(r, new Color(kind.r, kind.g, kind.b, 0.05f + 0.05f * hover));
            var frame = selected ? Gold : Color.Lerp(kind * 0.8f, Color.white, 0.3f * hover);
            Border(r, new Color(frame.r, frame.g, frame.b, 1f), selected ? 3f : 2f);
            Border(r.ContractedBy(5f), new Color(kind.r, kind.g, kind.b, 0.22f), 1f);
            var b = new Rect(r.x + 2f, r.y + 2f, r.width - 4f, 26f);
            Box(b, new Color(kind.r * 0.45f, kind.g * 0.45f, kind.b * 0.45f, 0.95f));
            Tex(b, BannerFade, new Color(kind.r, kind.g, kind.b, 0.55f));
            var font = Text.Font;
            var anchor = Text.Anchor;
            Text.Font = GameFont.Tiny;
            Text.Anchor = TextAnchor.MiddleCenter;
            ShadowLabel(b, banner.ToUpperInvariant(), new Color(1f, 0.97f, 0.9f));
            Text.Font = font;
            Text.Anchor = anchor;
            return new Rect(r.x + 10f, b.yMax + 6f, r.width - 20f, r.yMax - b.yMax - 14f);
        }

        /// <summary>The dimmed, vignetted backdrop behind full-screen screens.</summary>
        public static void Backdrop(Rect r, float alpha)
        {
            Box(r, new Color(0.02f, 0.02f, 0.025f, 0.82f * alpha));
            Tex(r.ExpandedBy(r.width * 0.12f), Vignette, new Color(0f, 0f, 0f, 0.9f * alpha));
        }

        /// <summary>A button in the mod's style: dark body, accent tint and border, glow on hover.</summary>
        public static bool Button(Rect r, string label, Color accent, bool active = true)
        {
            bool over = active && Mouse.IsOver(r);
            if (over) GlowAround(r, new Color(accent.r, accent.g, accent.b, 0.35f), 1.3f, SoftGlow);
            Tex(r, CardBody, Color.white);
            Box(r, new Color(accent.r, accent.g, accent.b, !active ? 0.06f : over ? 0.32f : 0.16f));
            Border(r, new Color(accent.r, accent.g, accent.b, !active ? 0.35f : over ? 1f : 0.7f), 1f);
            var anchor = Text.Anchor;
            Text.Anchor = TextAnchor.MiddleCenter;
            ShadowLabel(r, label, active ? Parchment : new Color(0.6f, 0.6f, 0.6f));
            Text.Anchor = anchor;
            if (!active || !Widgets.ButtonInvisible(r)) return false;
            Verse.Sound.SoundStarter.PlayOneShotOnCamera(RimWorld.SoundDefOf.Click);
            return true;
        }

        // ---------- particles ----------

        public class Particle
        {
            public Vector2 pos, vel;
            public float life, maxLife, size;
            public Color color;
        }

        public static void Burst(List<Particle> into, Vector2 at, Color color, int count, float speed = 380f)
        {
            for (int i = 0; i < count; i++)
            {
                float a = Rand.Range(0f, Mathf.PI * 2f), s = Rand.Range(0.3f, 1f) * speed;
                into.Add(new Particle
                {
                    pos = at, vel = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * s,
                    life = 0f, maxLife = Rand.Range(0.5f, 1.1f), size = Rand.Range(3f, 8f),
                    color = Color.Lerp(color, Color.white, Rand.Range(0f, 0.6f)),
                });
            }
        }

        public static void UpdateAndDraw(List<Particle> particles)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            for (int i = particles.Count - 1; i >= 0; i--)
            {
                var p = particles[i];
                p.life += dt;
                if (p.life >= p.maxLife) { particles.RemoveAt(i); continue; }
                p.vel *= 1f - 2.2f * dt;
                p.vel.y += 160f * dt;
                p.pos += p.vel * dt;
                float a = 1f - p.life / p.maxLife;
                float s = p.size * (0.6f + 0.4f * a);
                Tex(new Rect(p.pos.x - s * 2f, p.pos.y - s * 2f, s * 4f, s * 4f), SoftGlow, new Color(p.color.r, p.color.g, p.color.b, a * 0.6f));
                Box(new Rect(p.pos.x - s / 2f, p.pos.y - s / 2f, s, s), new Color(p.color.r, p.color.g, p.color.b, a));
            }
        }

        // ---------- texture generation ----------

        private static Texture2D NewTex(int w, int h)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            return t;
        }

        private static Texture2D VerticalGradient(Color top, Color bottom, int h, float grain)
        {
            var t = NewTex(4, h);
            var rand = new System.Random(1234);
            for (int y = 0; y < h; y++)
            {
                var c = Color.Lerp(bottom, top, y / (float)(h - 1));
                for (int x = 0; x < 4; x++)
                {
                    float n = ((float)rand.NextDouble() - 0.5f) * grain;
                    t.SetPixel(x, y, new Color(c.r + n, c.g + n, c.b + n, 1f));
                }
            }
            t.Apply();
            return t;
        }

        private static Texture2D Radial(int size, float falloff, bool invert = false)
        {
            var t = NewTex(size, size);
            float half = (size - 1) / 2f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Clamp01(Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half);
                float a = Mathf.Pow(1f - d, falloff);
                if (invert) a = Mathf.Clamp01(Mathf.Pow(d, 1f / falloff));
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }

        /// <summary>A circle filling the texture; with <paramref name="ring"/> > 0, only an outer band that thick (as a fraction of the radius).</summary>
        private static Texture2D Circle(int size, float ring)
        {
            var t = NewTex(size, size);
            float half = size / 2f, edge = 1.5f / half;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Mathf.Sqrt((x + 0.5f - half) * (x + 0.5f - half) + (y + 0.5f - half) * (y + 0.5f - half)) / half;
                float a = Mathf.Clamp01((1f - d) / edge);
                if (ring > 0f) a *= Mathf.Clamp01((d - (1f - ring)) / edge);
                t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }

        private static Texture2D SheenBand(int w)
        {
            var t = NewTex(w, 4);
            for (int x = 0; x < w; x++)
            {
                float d = Mathf.Abs(x / (float)(w - 1) - 0.5f) * 2f;
                float a = Mathf.Pow(1f - d, 2.5f);
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }

        private static Texture2D HorizontalFade(int w)
        {
            var t = NewTex(w, 4);
            for (int x = 0; x < w; x++)
            {
                float a = 1f - Mathf.Pow(x / (float)(w - 1), 1.5f);
                for (int y = 0; y < 4; y++) t.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            t.Apply();
            return t;
        }
    }
}
