﻿using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Nanite
{
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
        private static extern bool GetTriangles(IntPtr context, [Out] uint[] primitives, uint bufferSize);

       
        public static MeshletCollection ProcessMesh(uint[] indices, Vector3[] vertices)
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
                    var collection = new MeshletCollection();

                    // Get meshlets
                    var meshletCount = GetMeshletsCount(context);
                    collection.meshlets = new Meshlet[meshletCount];
                    if (!GetMeshlets(context, collection.meshlets, meshletCount))
                        throw new Exception("Failed to get meshlets data");

                    // Get vertices
                    var verticesCount = GetVerticesCount(context);
                    collection.vertices = new uint[verticesCount];
                    if (!GetVertices(context, collection.vertices, verticesCount))
                        throw new Exception("Failed to get vertices data");

                    // Get triangles
                    var triangleCount = GetTriangleCount(context);
                    collection.triangles = new uint[triangleCount];
                    if (!GetTriangles(context, collection.triangles, triangleCount))
                        throw new Exception("Failed to get triangles data");

                    return collection;
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