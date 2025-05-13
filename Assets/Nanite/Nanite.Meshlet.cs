using System;
using System.Runtime.InteropServices;

namespace Nanite
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct MeshletDescription
    {
        public uint VertexOffset;
        public uint TriangleOffset;
        public uint VertexCount;
        public uint TriangleCount;
        public static int SIZE => sizeof(uint) * 4;
    }
    
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public class MeshletCollection
    {
        public byte[] triangles;
        public uint[] vertices;
        public MeshletDescription[] meshlets;
    }
}