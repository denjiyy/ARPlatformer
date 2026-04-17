using UnityEngine;

namespace ARPlatformer
{
    public static class PlatformerMarkerTextureFactory
    {
        private static readonly Color32 White = new(245, 244, 238, 255);
        private static readonly Color32 Black = new(19, 21, 24, 255);
        private static readonly Color32 Accent = new(208, 61, 31, 255);
        private static readonly Color32 AccentTwo = new(24, 129, 104, 255);

        public static Texture2D CreateTexture(int size, string textureName)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color32[size * size];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = White;

            texture.SetPixels32(pixels);

            var border = size / 18;
            FillRect(texture, 0, 0, size, border, Black);
            FillRect(texture, 0, size - border, size, border, Black);
            FillRect(texture, 0, 0, border, size, Black);
            FillRect(texture, size - border, 0, border, size, Black);

            var grid = 12;
            var cell = (size - (border * 2)) / grid;
            for (var y = 0; y < grid; y++)
            {
                for (var x = 0; x < grid; x++)
                {
                    var value = (x * 17 + y * 23 + (x * y * 7)) % 13;
                    if (value < 4)
                    {
                        var color = (x + y) % 3 == 0 ? Accent : ((x * 2 + y) % 4 == 0 ? AccentTwo : Black);
                        FillRect(texture, border + x * cell, border + y * cell, cell - 6, cell - 6, color);
                    }
                }
            }

            DrawCornerMarker(texture, border + cell / 3, border + cell / 3, cell, Black, Accent);
            DrawCornerMarker(texture, size - border - (cell * 3) - cell / 3, border + cell / 3, cell, Black, AccentTwo);
            DrawCornerMarker(texture, border + cell / 3, size - border - (cell * 3) - cell / 3, cell, Black, AccentTwo);

            texture.Apply(false, false);
            return texture;
        }

        private static void DrawCornerMarker(Texture2D texture, int startX, int startY, int size, Color32 outer, Color32 inner)
        {
            FillRect(texture, startX, startY, size * 3, size * 3, outer);
            FillRect(texture, startX + size / 2, startY + size / 2, size * 2, size * 2, White);
            FillRect(texture, startX + size, startY + size, size, size, inner);
        }

        private static void FillRect(Texture2D texture, int x, int y, int width, int height, Color32 color)
        {
            for (var yIndex = y; yIndex < y + height; yIndex++)
            {
                for (var xIndex = x; xIndex < x + width; xIndex++)
                    texture.SetPixel(xIndex, yIndex, color);
            }
        }
    }
}
