// MeshletCommon.hlsl

#ifndef MESHLET_COMMON_INCLUDED
#define MESHLET_COMMON_INCLUDED

// Constants
// 常量定义
#define MAX_PRIMS_PER_MESHLET 126
#define MAX_VERTS_PER_MESHLET 64
#define MS_GROUP_SIZE 128
#define AS_GROUP_SIZE 32
#define BATCH_MESHLET_SIZE AS_GROUP_SIZE
#define BATCH_VERTEX_SIZE (MAX_VERTS * BATCH_MESHLET_SIZE)
#define CULL_FLAG 0x1

struct Constants
{
    float4x4 View;
    float4x4 ViewProj;
    float4 Planes[6];
    float3 ViewPosition;
    uint HighlightedIndex;
    float3 CullViewPosition;
    uint SelectedIndex;
    uint DrawMeshlets;
};

struct Instance
{
    float4x4 World;
    float4x4 WorldIT; // World inverse transpose
    float Scale;
    uint Flags;
};

struct MeshInfo
{
    uint MeshletCount;
    uint LastMeshletVertCount;
    uint LastMeshletPrimCount;
};

struct Meshlet
{
    uint VertCount;
    uint VertOffset;
    uint PrimCount;
    uint PrimOffset;
};

struct CullData
{
    float4 BoundingSphere;
    uint NormalCone;
    float ApexOffset;
};

struct Vertex
{
    float3 Position;
    float3 Normal;
};

struct VertexOut
{
    float4 PositionHS : SV_Position;
    float3 PositionVS : POSITION;
    float3 Normal : NORMAL;
    uint MeshletIndex : COLOR;
};


// Shared buffers
ConstantBuffer<Constants> _Constants;
ConstantBuffer<MeshInfo> _MeshInfo;
ConstantBuffer<Instance> _Instance;
StructuredBuffer<Vertex> _Vertices;
StructuredBuffer<Meshlet> _Meshlets;
ByteAddressBuffer _UniqueVertexIndices;
StructuredBuffer<uint> _PrimitiveIndices;
StructuredBuffer<CullData> _MeshletCullData;

// Utility functions
bool IsConeDegenerate(CullData c)
{
    return (c.NormalCone >> 24) == 0xff;
}

float4 UnpackCone(uint packed)
{
    float4 v;
    v.x = float((packed >> 0) & 0xFF);
    v.y = float((packed >> 8) & 0xFF);
    v.z = float((packed >> 16) & 0xFF);
    v.w = float((packed >> 24) & 0xFF);

    v = v / 255.0;
    v.xyz = v.xyz * 2.0 - 1.0;

    return v;
}

#endif // MESHLET_COMMON_INCLUDED
