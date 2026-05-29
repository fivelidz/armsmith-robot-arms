// StlImporter.cs — runtime binary + ASCII STL loader for ArmSmith / SO-ARM100 meshes.
// Coordinate conversion: STL is Z-up right-handed (mm); Unity is Y-up left-handed (m).
//   • Scale    : multiply every coordinate by 0.001 (mm → m)
//   • Axis remap : (x_stl, y_stl, z_stl) → (x_stl, z_stl, y_stl)  [swap Y↔Z]
//   • Winding flip: every triangle is emitted CW (Unity expects CW when viewed from front
//     in left-hand coords); the axis-swap already mirrors the mesh, so winding is reversed
//     simply by swapping the second and third vertex of each triangle.
//
// Attribution: SO-ARM100/SO-101 robot arm meshes © The Robot Studio, Apache-2.0
//              https://github.com/TheRobotStudio/SO-ARM100

using System;
using System.IO;
using System.Text;
using System.Collections.Generic;
using UnityEngine;

namespace ArmSmith
{
    /// <summary>
    /// Runtime STL loader. Handles both binary and ASCII formats.
    /// All public methods are thread-safe to call from the main thread; mesh upload to GPU
    /// must happen on the main thread (Unity restriction), so do not call from a Job.
    /// </summary>
    public static class StlImporter
    {
        // ─────────────────────────────────────────────────────────────────────────────
        //  Public API
        // ─────────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Load an STL file from an absolute path, auto-detecting binary vs ASCII format.
        /// Returns <c>null</c> (+ a LogWarning) on any error.
        /// </summary>
        public static Mesh Load(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("[StlImporter] Load called with null/empty path.");
                return null;
            }

            if (!File.Exists(path))
            {
                Debug.LogWarning($"[StlImporter] File not found: {path}");
                return null;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (IsBinary(bytes))
                    return ParseBinary(bytes, path);
                else
                    return ParseAscii(bytes, path);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[StlImporter] Failed to load '{path}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Convenience loader that resolves a relative path against
        /// <c>Application.dataPath + "/Meshes/SOARM100/"</c> so it works both in the
        /// Editor (Assets/) and in a built player (where dataPath points to the Data folder
        /// next to the executable — but note STLs need to be copied into StreamingAssets
        /// for builds; see StlArmSkin for details).
        /// </summary>
        public static Mesh LoadFromDataPath(string relPath)
        {
            string full = Path.Combine(Application.dataPath, "Meshes", "SOARM100", relPath);
            return Load(full);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  Detection
        // ─────────────────────────────────────────────────────────────────────────────

        // A binary STL is: 80-byte header + 4-byte uint32 triCount + triCount * 50 bytes.
        // An ASCII STL always starts (after optional whitespace) with "solid".
        // The heuristic: if byte-count matches the binary formula exactly, it IS binary.
        // Otherwise fall back to checking the "solid" keyword in the first 256 bytes.
        static bool IsBinary(byte[] bytes)
        {
            if (bytes.Length < 84) return false;            // too small to be a valid binary STL

            uint triCount = BitConverter.ToUInt32(bytes, 80);
            long expected  = 84L + (long)triCount * 50L;
            if ((long)bytes.Length == expected) return true; // size matches exactly → binary

            // ASCII STLs can rarely have size coincidentally equal to the formula for a small
            // triangle count; guard with keyword check.
            string header = Encoding.ASCII.GetString(bytes, 0, Math.Min(256, bytes.Length));
            if (header.TrimStart().StartsWith("solid", StringComparison.OrdinalIgnoreCase))
                return false;

            // Default to binary (safer for corrupted / non-conforming files).
            return true;
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  Binary parser
        // ─────────────────────────────────────────────────────────────────────────────

        static Mesh ParseBinary(byte[] bytes, string debugName)
        {
            // Header: bytes[0..79] — ignored (often contains CAD metadata).
            uint triCount = BitConverter.ToUInt32(bytes, 80);

            if (triCount == 0)
            {
                Debug.LogWarning($"[StlImporter] Binary STL has 0 triangles: {debugName}");
                return null;
            }

            long required = 84L + (long)triCount * 50L;
            if ((long)bytes.Length < required)
            {
                Debug.LogWarning($"[StlImporter] Binary STL truncated (expected {required} bytes, got {bytes.Length}): {debugName}");
                return null;
            }

            // Pre-allocate arrays — each triangle produces 3 independent vertices (flat/smooth
            // normals are recomputed anyway, so no vertex merging needed here).
            int vCount = (int)triCount * 3;

            // Guard: Unity's Mesh API supports at most 2^32-1 indices with 32-bit index buffer,
            // but MeshTopology.Triangles means vertices = indices. If triCount * 3 > int.MaxValue
            // we'd overflow; in practice SO-ARM STLs are <2M tris so this is academic.
            if (vCount < 0)
            {
                Debug.LogWarning($"[StlImporter] Triangle count overflow ({triCount}): {debugName}");
                return null;
            }

            Vector3[] verts   = new Vector3[vCount];
            int[]     indices = new int[vCount];

            int offset = 84;
            int vi     = 0;

            for (uint t = 0; t < triCount; t++)
            {
                // Skip the face normal (bytes 0-11 of this record) — we recompute them.
                offset += 12;

                // Read 3 vertices (each is 3 × float32 = 12 bytes), then 2-byte attribute.
                float v0x = BitConverter.ToSingle(bytes, offset + 0);
                float v0y = BitConverter.ToSingle(bytes, offset + 4);
                float v0z = BitConverter.ToSingle(bytes, offset + 8);
                offset += 12;

                float v1x = BitConverter.ToSingle(bytes, offset + 0);
                float v1y = BitConverter.ToSingle(bytes, offset + 4);
                float v1z = BitConverter.ToSingle(bytes, offset + 8);
                offset += 12;

                float v2x = BitConverter.ToSingle(bytes, offset + 0);
                float v2y = BitConverter.ToSingle(bytes, offset + 4);
                float v2z = BitConverter.ToSingle(bytes, offset + 8);
                offset += 12;

                offset += 2; // attribute byte count (always skip)

                // Coordinate conversion:
                //   STL  : right-hand, Z-up,  mm
                //   Unity: left-hand,  Y-up,  m
                //   Remap: (x, y, z) → (x * 0.001, z * 0.001, y * 0.001)
                //   The Y↔Z swap is an orientation mirror; to restore correct front-face
                //   winding for Unity's left-hand CCW-front convention we swap vertices 1 & 2.

                verts[vi + 0] = new Vector3(v0x * 0.001f, v0z * 0.001f, v0y * 0.001f);
                verts[vi + 1] = new Vector3(v2x * 0.001f, v2z * 0.001f, v2y * 0.001f); // swapped
                verts[vi + 2] = new Vector3(v1x * 0.001f, v1z * 0.001f, v1y * 0.001f); // swapped

                indices[vi + 0] = vi + 0;
                indices[vi + 1] = vi + 1;
                indices[vi + 2] = vi + 2;
                vi += 3;
            }

            return BuildMesh(verts, indices, debugName);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  ASCII parser
        // ─────────────────────────────────────────────────────────────────────────────

        static Mesh ParseAscii(byte[] bytes, string debugName)
        {
            string text = Encoding.ASCII.GetString(bytes);
            var verts   = new List<Vector3>(4096);
            var indices = new List<int>(4096);

            // State machine: look for "outer loop" blocks containing three "vertex x y z" lines.
            int    pos       = 0;
            int    len       = text.Length;
            int    vi        = 0;
            float  v0x = 0, v0y = 0, v0z = 0;
            float  v1x = 0, v1y = 0, v1z = 0;
            float  v2x = 0, v2y = 0, v2z = 0;
            bool   inLoop    = false;
            int    loopVert  = 0;

            while (pos < len)
            {
                // Advance past whitespace / newlines
                while (pos < len && (text[pos] == ' ' || text[pos] == '\t' ||
                                     text[pos] == '\r' || text[pos] == '\n'))
                    pos++;

                if (pos >= len) break;

                // Read the next keyword token (up to the next space/newline)
                int kStart = pos;
                while (pos < len && text[pos] != ' ' && text[pos] != '\t' &&
                       text[pos] != '\r' && text[pos] != '\n')
                    pos++;
                string keyword = text.Substring(kStart, pos - kStart);

                if (keyword.Equals("outer", StringComparison.OrdinalIgnoreCase))
                {
                    inLoop   = true;
                    loopVert = 0;
                }
                else if (keyword.Equals("vertex", StringComparison.OrdinalIgnoreCase) && inLoop)
                {
                    // Parse three floats on the same line.
                    float x = ReadFloat(text, ref pos);
                    float y = ReadFloat(text, ref pos);
                    float z = ReadFloat(text, ref pos);

                    switch (loopVert)
                    {
                        case 0: v0x = x; v0y = y; v0z = z; break;
                        case 1: v1x = x; v1y = y; v1z = z; break;
                        case 2: v2x = x; v2y = y; v2z = z; break;
                    }
                    loopVert++;
                }
                else if (keyword.Equals("endloop", StringComparison.OrdinalIgnoreCase) && inLoop)
                {
                    inLoop = false;
                    if (loopVert == 3)
                    {
                        // Same conversion as binary: remap + swap v1/v2 for winding.
                        verts.Add(new Vector3(v0x * 0.001f, v0z * 0.001f, v0y * 0.001f));
                        verts.Add(new Vector3(v2x * 0.001f, v2z * 0.001f, v2y * 0.001f));
                        verts.Add(new Vector3(v1x * 0.001f, v1z * 0.001f, v1y * 0.001f));
                        indices.Add(vi); indices.Add(vi + 1); indices.Add(vi + 2);
                        vi += 3;
                    }
                }
                // All other keywords (solid, facet, normal, endfacet, endsolid) are skipped.
            }

            if (verts.Count == 0)
            {
                Debug.LogWarning($"[StlImporter] ASCII STL parsed 0 triangles: {debugName}");
                return null;
            }

            return BuildMesh(verts.ToArray(), indices.ToArray(), debugName);
        }

        // ─────────────────────────────────────────────────────────────────────────────
        //  Helpers
        // ─────────────────────────────────────────────────────────────────────────────

        static float ReadFloat(string text, ref int pos)
        {
            int len = text.Length;
            // Skip leading spaces / tabs (NOT newlines — stay on the same logical line)
            while (pos < len && (text[pos] == ' ' || text[pos] == '\t')) pos++;

            int start = pos;
            // Accept: digits, '.', '-', '+', 'e', 'E'
            while (pos < len && text[pos] != ' ' && text[pos] != '\t' &&
                   text[pos] != '\r' && text[pos] != '\n')
                pos++;

            if (pos == start) return 0f;
            string s = text.Substring(start, pos - start);
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out float v) ? v : 0f;
        }

        static Mesh BuildMesh(Vector3[] verts, int[] indices, string debugName)
        {
            var mesh = new UnityEngine.Mesh();
            mesh.name = Path.GetFileNameWithoutExtension(debugName);

            // Use 32-bit index buffer if we have more than 65535 vertices.
            if (verts.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;

            mesh.vertices  = verts;
            mesh.triangles = indices;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
