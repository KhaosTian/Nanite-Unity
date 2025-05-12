using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Nanite
{
    struct InstancePara
    {
        public Matrix4x4 modelMatrix;
        public uint vertexOffset;
    }

    public class NaniteRenderer : MonoBehaviour
    {
        private const int VERTEX_COUNT_PER_MESHLET = 64;
        private const int KERNEL_SIZE_X = 64;
        
        private static readonly int ArgsBufferID = Shader.PropertyToID("_ArgsBuffer");
        private static readonly int VertexBufferID = Shader.PropertyToID("_VertexBuffer");
        private static readonly int MeshletsBufferID = Shader.PropertyToID("_MeshletsBuffer");
        private static readonly int MeshletVerticesBufferID = Shader.PropertyToID("_MeshletVerticesBuffer");
        private static readonly int MeshletTrianglesBufferID = Shader.PropertyToID("_MeshletTrianglesBuffer");
        
        public Camera Camera;
        public Mesh TargetMesh;

        private ComputeShader m_CullingShader;
        private Material m_MeshletMaterial;
        
        private MeshletsContext context;
        
        private GraphicsBuffer m_IndexBuffer; // 索引数据
        private ComputeBuffer m_PositionBuffer; // 所有顶点数据
        private ComputeBuffer m_ArgsBuffer; // 间接渲染参数
        private ComputeBuffer m_MeshletsBuffer; // meshlet 数据
        private ComputeBuffer m_MeshletVerticesBuffer; // meshlet 顶点索引数据
        private ComputeBuffer m_MeshletTrianglesBuffer; // meshlet 局部三角形索引数据
        private ComputeBuffer m_VisibilityBuffer;

        private int m_KernelID;
        private int m_KernelGroupX;

        private Plane[] cullingPlanes = new Plane[6];
        private Vector4[] cullingPlaneVectors = new Vector4[6];

        private Bounds m_ProxyBounds;

        void Start()
        {
            var meshletCount = context.meshlets.Length;
            
            // 间接渲染参数缓冲区
            m_ArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            m_ArgsBuffer.name = nameof(m_ArgsBuffer);

            // 剔除计算着色器
            m_KernelID = m_CullingShader.FindKernel("CullingMain");
            m_CullingShader.SetBuffer(m_KernelID, ArgsBufferID, m_ArgsBuffer);
            
            // 顶点坐标缓冲区
            m_PositionBuffer = new ComputeBuffer(TargetMesh.vertices.Length, sizeof(float) * 3);
            m_PositionBuffer.name = $"{nameof(m_PositionBuffer)}:{m_PositionBuffer.count}";
            m_PositionBuffer.SetData(TargetMesh.vertices);
            m_MeshletMaterial.SetBuffer(VertexBufferID, m_PositionBuffer);
            
            // Meshlet缓冲区
            m_MeshletsBuffer = new ComputeBuffer(meshletCount, Meshlet.SIZE);
            m_MeshletsBuffer.name = $"{nameof(m_MeshletsBuffer)}:{m_MeshletsBuffer.count}";
            m_MeshletsBuffer.SetData(context.meshlets);
            m_CullingShader.SetBuffer(m_KernelID, MeshletsBufferID, m_MeshletsBuffer);
            m_MeshletMaterial.SetBuffer(MeshletsBufferID, m_MeshletsBuffer);
            
            // Meshlet Vertices索引缓冲区
            m_MeshletVerticesBuffer = new ComputeBuffer(context.vertices.Length, sizeof(uint));
            m_MeshletVerticesBuffer.name = $"{nameof(m_MeshletVerticesBuffer)}:{m_MeshletVerticesBuffer.count}";
            m_MeshletVerticesBuffer.SetData(context.vertices);
            m_CullingShader.SetBuffer(m_KernelID, MeshletVerticesBufferID, m_VisibilityBuffer);
            m_MeshletMaterial.SetBuffer("_MeshletVerticesBuffer", m_VisibilityBuffer);
            
            // Meshlet Triangles索引缓冲区
            m_MeshletTrianglesBuffer = new ComputeBuffer(context.triangles.Length, sizeof(byte));
            m_MeshletTrianglesBuffer.name = $"{nameof(m_MeshletTrianglesBuffer)}:{m_MeshletTrianglesBuffer.count}";
            m_MeshletTrianglesBuffer.SetData(context.triangles);
            m_CullingShader.SetBuffer(m_KernelID, MeshletTrianglesBufferID, m_VisibilityBuffer);
            m_MeshletMaterial.SetBuffer("_MeshletTrianglesBuffer", m_VisibilityBuffer);
            
            // 可见性缓冲区
            m_VisibilityBuffer = new ComputeBuffer(meshletCount, sizeof(uint));
            m_VisibilityBuffer.name = $"{nameof(m_VisibilityBuffer)}:{m_VisibilityBuffer.count}";
            m_VisibilityBuffer.SetData(new uint[context.meshlets.Length]);
            m_CullingShader.SetBuffer(m_KernelID, "_VisibilityBuffer", m_VisibilityBuffer);
            m_MeshletMaterial.SetBuffer("_VisibilityBuffer", m_VisibilityBuffer);
            
            m_KernelGroupX = Mathf.CeilToInt(1 / 0f * meshletCount / KERNEL_SIZE_X);
            m_ProxyBounds = new Bounds(Vector3.zero, 1000.0f * Vector3.one);

        }
        

        void Update()
        {
            m_ArgsBuffer.SetData(new uint[5] { (uint)m_IndexBuffer.count, 0, 0, 0, 0 });
            m_CullingShader.Dispatch(m_KernelID, m_KernelGroupX, 1, 1);
            
            Graphics.DrawProceduralIndirect(m_MeshletMaterial, m_ProxyBounds, MeshTopology.Triangles, m_ArgsBuffer);
        }
    }
}