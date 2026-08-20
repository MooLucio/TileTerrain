using UnityEngine;

namespace MooLucio.TileTerrain
{
    public partial class TileTerrainEditor
    {
        private bool IsSafeToCarve(TileTerrainGridData data, int idx)
        {
            int w = data.Width;
            int h = data.Height;
            int row = w + 1;
            int vx = idx % row;
            int vz = idx / row;
            var baseV = data.Vertices[idx];
            int baseLevel = baseV.CliffByte;

            for (int nz = Mathf.Max(0, vz - 1); nz <= Mathf.Min(h, vz + 1); nz++)
            {
                for (int nx = Mathf.Max(0, vx - 1); nx <= Mathf.Min(w, vx + 1); nx++)
                {
                    if (nx == vx && nz == vz) continue;
                    var nv = data.Vertices[nz * row + nx];
                    int nLevel = nv.IsWater ? nv.WaterLevel : nv.CliffByte;
                    if (nLevel < baseLevel) return false;
                }
            }
            return true;
        }

        private bool IsBoundary(TileTerrainGridData data, int idx)
        {
            int w = data.Width;
            int h = data.Height;
            int row = w + 1;
            int vx = idx % row;
            int vz = idx / row;
            var baseV = data.Vertices[idx];

            for (int nz = Mathf.Max(0, vz - 1); nz <= Mathf.Min(h, vz + 1); nz++)
            {
                for (int nx = Mathf.Max(0, vx - 1); nx <= Mathf.Min(w, vx + 1); nx++)
                {
                    if (nx == vx && nz == vz) continue;
                    var nv = data.Vertices[nz * row + nx];
                    if (nv.IsWater != baseV.IsWater) return true;
                }
            }
            return false;
        }

        private bool TouchesWater(TileTerrainGridData data, int w, int h, int row, int vx, int vz)
        {
            for (int nz = Mathf.Max(0, vz - 1); nz <= Mathf.Min(h, vz + 1); nz++)
            {
                for (int nx = Mathf.Max(0, vx - 1); nx <= Mathf.Min(w, vx + 1); nx++)
                {
                    if (nx == vx && nz == vz) continue;
                    if (data.Vertices[nz * row + nx].IsWater) return true;
                }
            }
            return false;
        }

        private bool IsCliffEdge(TileTerrainGridData data, int idx)
        {
            int w = data.Width;
            int h = data.Height;
            int row = w + 1;
            int vx = idx % row;
            int vz = idx / row;
            int baseByte = data.Vertices[idx].CliffByte;

            for (int nz = Mathf.Max(0, vz - 1); nz <= Mathf.Min(h, vz + 1); nz++)
            {
                for (int nx = Mathf.Max(0, vx - 1); nx <= Mathf.Min(w, vx + 1); nx++)
                {
                    if (nx == vx && nz == vz) continue;
                    var neighbor = data.Vertices[nz * row + nx];
                    if (neighbor.IsWater) continue;
                    if (neighbor.CliffByte != baseByte)
                        return true;
                }
            }
            return false;
        }
    }
}
