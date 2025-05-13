Shader "Nanite/MeshletRendering"
{
    Properties
    {
        _BackFaceColor("Color", Color) = (0,0,0,1)
    }

    SubShader
    {
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #define MESHLET_VERTEX_COUNT 64

            #include "UnityCG.cginc"

            struct appdata
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float3 color : COLOR;
            };

            struct EntityPara
            {
                float4x4 model;
                uint vertexOffset;
                uint meshletIndex; // 实体在meshlet缓冲区中的起始索引
            };

            struct MeshletDescription
            {
                uint VertexOffset;
                uint TriangleOffset;
                uint VertexCount;
                uint TriangleCount;
            };

            StructuredBuffer<float3> _PositionBuffer;
            StructuredBuffer<MeshletDescription> _MeshletsBuffer;
            StructuredBuffer<uint> _MeshletVerticesBuffer;
            StructuredBuffer<uint> _MeshletTrianglesBuffer;

            StructuredBuffer<uint> _VisibilityBuffer;

            StructuredBuffer<EntityPara> _EntityBuffer;
            StructuredBuffer<uint> _MeshletRefBuffer;

            float4 _BackFaceColor;

            v2f vert(appdata v)
            {
                uint visbibleID = _VisibilityBuffer[v.instanceID];
                uint entityID = _MeshletRefBuffer[visbibleID];
                EntityPara entity = _EntityBuffer[entityID];
                
                MeshletDescription meshlet = _MeshletsBuffer[visbibleID];
                
                
                v2f o;
                return o;
            }
            fixed4 frag(v2f i, bool facing : SV_IsFrontFace) : SV_Target
            {
                fixed4 col = facing ? fixed4(i.color, 1) : _BackFaceColor;
                return col;
            }
            ENDCG

        }
    }
}