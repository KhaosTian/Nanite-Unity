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
                float4x4 modelMatrix;
                uint vertexOffset;
                uint meshletIndex; // 实体在meshlet缓冲区中的起始索引
                float3 color;
            };

            struct MeshletDescription
            {
                uint vertexOffset;
                uint triangleOffset;
                uint vertexCount;
                uint triangleCount;
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

                uint triangleIndex = v.vertexID / 3; // 在meshlet中第几个三角形
                uint vertexInTriangle = v.vertexID % 3; // 在三角形中第几个顶点

                uint triangleOffset = meshlet.triangleOffset
                    + triangleIndex * 3 // 该三角形的偏移值
                    + vertexInTriangle; // 该三角形顶点的偏移值

                uint localVertexIndex = _MeshletTrianglesBuffer[triangleOffset]; // 局部三角形顶点索引
                uint globalVertexIndex = _MeshletVerticesBuffer[meshlet.vertexOffset + localVertexIndex]; // 全局三角形顶点索引

                float3 position = _PositionBuffer[globalVertexIndex];

                v2f o;
                unity_ObjectToWorld = entity.modelMatrix;
                o.vertex = UnityObjectToClipPos(position);
                o.color = entity.color;
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