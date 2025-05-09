Shader "Custom/MeshletInstancing"
{
    SubShader
    {
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #define MESHLET_VERTEX_MAX_COUNT 128

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

            struct InstancePara
            {
                float4x4 model;
                float4 color;
                uint meshletOffset; // 该实例使用的第一个meshlet索引
                uint meshletCount; // 该实例使用的meshlet数量
            };

            struct MeshletDescription
            {
                uint vertexOffset; // meshlet_vertices中的起始索引
                uint triangleOffset; // meshlet_triangles中的起始索引
                uint vertexCount; // 顶点数量
                uint triangleCount; // 三角形数量
            };

            uint _ClusterCount;
            StructuredBuffer<float3> _VertexBuffer;
            StructuredBuffer<uint> _Indices;
            StructuredBuffer<uint> _Primitives;
            StructuredBuffer<MeshletDescription> _MeshletDescriptions;

            v2f vert(appdata v)
            {
                uint instanceIndex = v.instanceID / _MM; // 以cluster为单位的instance id
                uint clusterID = v.instanceID % _ClusterCount; // 
                InstancePara para = _InstanceBuffer[instanceID];
                uint index = clusterID * MESHLET_VERTEX_MAX_COUNT + v.vertexID;
                float3 vertex = _VertexBuffer[index];

                v2f o;
                unity_ObjectToWorld = para.model;
                o.vertex = UnityObjectToClipPos(vertex);
                o.color = para.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                fixed4 col = fixed4(i.color, 1);
                return col;
            }
            ENDCG
        }
    }
}