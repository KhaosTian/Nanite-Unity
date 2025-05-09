using System;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.Serialization;

namespace Nanite
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Meshlet
    {
        public uint VertexOffset;
        public uint TriangleOffset;
        public uint VertexCount;
        public uint TriangleCount;

    }

    public class MeshletsContext
    {
        public byte[] triangles;
        public uint[] vertices;
        public Meshlet[] meshlets;
    }

    public class NanitePlugin
    {
        // DLL Import statements
        [DllImport("NaniteUnity")]
        private static extern IntPtr CreateNaniteBuilder(
            [In] uint[] indices, uint indicesCount,
            [In] float[] positions, uint positionsCount);

        [DllImport("NaniteUnity")]
        private static extern void DestroyNaniteBuilder(IntPtr builder);

        [DllImport("NaniteUnity")]
        private static extern IntPtr BuildMeshlets(IntPtr builder);

        [DllImport("NaniteUnity")]
        private static extern void DestroyMeshletsContext(IntPtr context);

        [DllImport("NaniteUnity")]
        private static extern uint GetMeshletsCount(IntPtr context);

        [DllImport("NaniteUnity")]
        private static extern bool GetMeshlets(IntPtr context, [Out] Meshlet[] meshlets, uint bufferSize);

        [DllImport("NaniteUnity")]
        private static extern uint GetVerticesCount(IntPtr context);

        [DllImport("NaniteUnity")]
        private static extern bool GetVertices(IntPtr context, [Out] uint[] indices, uint bufferSize);

        [DllImport("NaniteUnity")]
        private static extern uint GetTriangleCount(IntPtr context);

        [DllImport("NaniteUnity")]
        private static extern bool GetTriangles(IntPtr context, [Out] byte[] primitives, uint bufferSize);

       
        public static MeshletsContext ProcessMesh(uint[] indices, Vector3[] vertices)
        {
            // Convert Vector3 array to float array
            var positions = new float[vertices.Length * 3];
            for (var i = 0; i < vertices.Length; i++)
            {
                positions[i * 3] = vertices[i].x;
                positions[i * 3 + 1] = vertices[i].y;
                positions[i * 3 + 2] = vertices[i].z;
            }

            // Create NaniteBuilder
            var builder = CreateNaniteBuilder(
                indices, (uint)indices.Length,
                positions, (uint)positions.Length
            );

            if (builder == IntPtr.Zero) 
                throw new Exception("Failed to create NaniteBuilder");

            try
            {
                // Build meshlets
                var context = BuildMeshlets(builder);
                if (context == IntPtr.Zero)
                    throw new Exception("Failed to build meshlets");

                try
                {
                    // Create result structure
                    var result = new MeshletsContext();

                    // Get meshlets
                    var meshletCount = GetMeshletsCount(context);
                    result.meshlets = new Meshlet[meshletCount];
                    if (!GetMeshlets(context, result.meshlets, meshletCount))
                        throw new Exception("Failed to get meshlets data");

                    // Get vertices
                    var verticesCount = GetVerticesCount(context);
                    result.vertices = new uint[verticesCount];
                    if (!GetVertices(context, result.vertices, verticesCount))
                        throw new Exception("Failed to get vertices data");

                    // Get triangles
                    var triangleCount = GetTriangleCount(context);
                    result.triangles = new byte[triangleCount];
                    if (!GetTriangles(context, result.triangles, triangleCount))
                        throw new Exception("Failed to get triangles data");

                    return result;
                }
                finally
                {
                    DestroyMeshletsContext(context);
                }
            }
            finally
            {
                DestroyNaniteBuilder(builder);
            }
        }
    }
}