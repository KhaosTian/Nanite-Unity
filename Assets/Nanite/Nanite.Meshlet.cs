using System;
using System.Runtime.InteropServices;
using UnityEngine.Serialization;

namespace Nanite
{
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public struct Meshlet
    {
        public uint VertOffset;
        public uint PrimOffset;
        public uint VertCount;
        public uint PrimCount;
    }
    
    [Serializable]
    [StructLayout(LayoutKind.Sequential)]
    public class MeshletCollection
    {
        public uint[] triangles;
        public uint[] vertices;
        public Meshlet[] meshlets;
    }
}