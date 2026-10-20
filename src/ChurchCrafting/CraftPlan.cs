using System;
using System.Collections.Generic;

namespace LocalSulfur.ChurchCrafting
{
    internal sealed class Material
    {
        public int Token, Id, Count;
    }
    internal sealed class Spend
    {
        public int Token, Count;
    }
    internal sealed class Destination
    {
        public int Token = -1, X, Y, Count;
        public bool Rotated;
    }
    internal static class CraftPlan
    {
        // Materials are ordered by source priority; a stack can satisfy repeated ingredients.
        internal static bool Ingredients(List<Material> materials, Dictionary<int, int> required, out List<Spend> result)
        {
            result = new List<Spend>();
            if (required.Count == 0) return false;
            foreach (var need in required)
            {
                if (need.Key <= 0 || need.Value <= 0) return false;
                int remaining = need.Value;
                foreach (var material in materials)
                {
                    if (material.Id != need.Key || material.Count <= 0) continue;
                    int take = Math.Min(remaining, material.Count);
                    result.Add(new Spend { Token = material.Token, Count = take });
                    remaining -= take;
                    if (remaining == 0) break;
                }
                if (remaining > 0) { result.Clear(); return false; }
            }
            return true;
        }

        internal static bool Outputs(bool[,] occupied, int pageRows, int width, int height,
            int maxStack, int quantity, List<Material> stackCapacity, out List<Destination> result)
        {
            result = new List<Destination>();
            if (width <= 0 || height <= 0 || maxStack <= 0 || quantity <= 0 || pageRows <= 0) return false;
            bool[,] reserved = (bool[,])occupied.Clone();
            foreach (var capacity in stackCapacity)
            {
                int add = Math.Min(quantity, Math.Max(0, capacity.Count));
                if (add > 0) result.Add(new Destination { Token = capacity.Token, Count = add });
                quantity -= add;
                if (quantity == 0) return true;
            }
            while (quantity > 0)
            {
                bool found = false;
                for (int rotation = 0; rotation < 2 && !found; rotation++)
                {
                    int w = rotation == 0 ? width : height;
                    int h = rotation == 0 ? height : width;
                    for (int y = 0; y <= reserved.GetLength(1) - h && !found; y++)
                    {
                        // Items cannot straddle two visible backpack pages.
                        if (y / pageRows != (y + h - 1) / pageRows) continue;
                        for (int x = 0; x <= reserved.GetLength(0) - w && !found; x++)
                        {
                            bool free = true;
                            for (int dx = 0; dx < w && free; dx++)
                                for (int dy = 0; dy < h; dy++)
                                    if (reserved[x + dx, y + dy]) { free = false; break; }
                            if (!free) continue;
                            for (int dx = 0; dx < w; dx++)
                                for (int dy = 0; dy < h; dy++) reserved[x + dx, y + dy] = true;
                            int add = Math.Min(quantity, maxStack);
                            result.Add(new Destination { X = x, Y = y, Count = add, Rotated = rotation != 0 });
                            quantity -= add;
                            found = true;
                        }
                    }
                }
                if (!found) { result.Clear(); return false; }
            }
            return true;
        }
    }
}
