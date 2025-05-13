using UnityEngine;

namespace Nanite
{
    public class NaniteRendering : MonoBehaviour
    {
        private const int MESHLET_VERTEX_COUNT = 64;
        private const int KERNEL_SIZE_X = 64;

        private static readonly int ArgsBufferID = Shader.PropertyToID("_IndirectDrawArgsBuffer");
        private static readonly int PositionBufferID = Shader.PropertyToID("_PositionBuffer");
        private static readonly int MeshletsBufferID = Shader.PropertyToID("_MeshletsBuffer");
        private static readonly int MeshletVerticesBufferID = Shader.PropertyToID("_MeshletVerticesBuffer");
        private static readonly int MeshletTrianglesBufferID = Shader.PropertyToID("_MeshletTrianglesBuffer");
        private static readonly int MeshletCountID = Shader.PropertyToID("_MeshletCount");
        private static readonly int VisibilityBufferID = Shader.PropertyToID("_VisibilityBuffer");

        private int m_MeshletCount;


        private Material m_MeshletMaterial;
        public ComputeShader CullingCompute;
        public MeshletAsset SelectedMeshletAsset;
        private MeshletCollection m_Collection;
        private Mesh m_SourceMesh;

        private struct EntityPara
        {
            public Matrix4x4 ModelMatrix;
            public uint VertexOffset;
            public uint MeshletIndex;
        }

        private struct MeshletBounds
        {
            
        }
        
        private ComputeBuffer m_IndirectDrawArgsBuffer; // 间接渲染参数
        private ComputeBuffer m_VisibilityBuffer;
        
        private ComputeBuffer m_PositionBuffer; // 所有顶点数据
        private ComputeBuffer m_MeshletsBuffer; // meshlet 数据
        private ComputeBuffer m_MeshletVerticesBuffer; // meshlet 顶点索引数据
        private ComputeBuffer m_MeshletTrianglesBuffer; // meshlet 局部三角形索引数据
        
        private ComputeBuffer m_EntityParaBuffer;
        private ComputeBuffer m_MeshletRefBuffer;
        private ComputeBuffer m_MeshletBoundsBuffer;

        private int m_KernelID;
        private int m_KernelGroupX;

        private Plane[] m_CullingPlanes = new Plane[6];
        private Vector4[] m_CullingPlaneVectors = new Vector4[6];

        private Bounds m_ProxyBounds;

        private void Start()
        {
            if (!SelectedMeshletAsset) return;
            m_Collection = SelectedMeshletAsset.Collection;
            m_SourceMesh = SelectedMeshletAsset.SourceMesh;
            m_MeshletMaterial = new Material(Shader.Find("Nanite/MeshletRendering"));
            
            InitParas();
            InitBuffers();
            SetupShaders();
        }

        private void InitParas()
        {
            m_MeshletCount = SelectedMeshletAsset.Collection.meshlets.Length;
            ;
            m_KernelGroupX = Mathf.CeilToInt(1.0f * m_MeshletCount / KERNEL_SIZE_X);
            m_ProxyBounds = new Bounds(Vector3.zero, 1000.0f * Vector3.one);
        }

        private void InitBuffers()
        {
            // 间接渲染参数缓冲区
            m_IndirectDrawArgsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
            m_IndirectDrawArgsBuffer.name = nameof(m_IndirectDrawArgsBuffer);

            // 顶点坐标缓冲区
            m_PositionBuffer = new ComputeBuffer(m_SourceMesh.vertices.Length, sizeof(float) * 3);
            m_PositionBuffer.name = $"{nameof(m_PositionBuffer)}:{m_PositionBuffer.count}";
            m_PositionBuffer.SetData(m_SourceMesh.vertices);


            // Meshlet缓冲区
            m_MeshletsBuffer = new ComputeBuffer(m_MeshletCount, MeshletDescription.SIZE);
            m_MeshletsBuffer.name = $"{nameof(m_MeshletsBuffer)}:{m_MeshletsBuffer.count}";
            m_MeshletsBuffer.SetData(m_Collection.meshlets);


            // Meshlet Vertices索引缓冲区
            m_MeshletVerticesBuffer = new ComputeBuffer(m_Collection.vertices.Length, sizeof(uint));
            m_MeshletVerticesBuffer.name = $"{nameof(m_MeshletVerticesBuffer)}:{m_MeshletVerticesBuffer.count}";
            m_MeshletVerticesBuffer.SetData(m_Collection.vertices);


            // Meshlet Triangles索引缓冲区
            m_MeshletTrianglesBuffer = new ComputeBuffer(m_Collection.triangles.Length, sizeof(byte));
            m_MeshletTrianglesBuffer.name = $"{nameof(m_MeshletTrianglesBuffer)}:{m_MeshletTrianglesBuffer.count}";
            m_MeshletTrianglesBuffer.SetData(m_Collection.triangles);


            // 可见性缓冲区
            m_VisibilityBuffer = new ComputeBuffer(m_MeshletCount, sizeof(uint));
            m_VisibilityBuffer.name = $"{nameof(m_VisibilityBuffer)}:{m_VisibilityBuffer.count}";
            m_VisibilityBuffer.SetData(new uint[m_Collection.meshlets.Length]);
        }

        private void SetupShaders()
        {
            m_KernelID = CullingCompute.FindKernel("CullingMain");
            CullingCompute.SetBuffer(m_KernelID, ArgsBufferID, m_IndirectDrawArgsBuffer);
            CullingCompute.SetBuffer(m_KernelID, VisibilityBufferID, m_VisibilityBuffer);
            
            m_MeshletMaterial.SetBuffer(VisibilityBufferID, m_VisibilityBuffer);
            m_MeshletMaterial.SetBuffer(PositionBufferID, m_PositionBuffer);
            m_MeshletMaterial.SetBuffer(MeshletsBufferID, m_MeshletsBuffer);
            m_MeshletMaterial.SetBuffer(MeshletVerticesBufferID, m_VisibilityBuffer);
            m_MeshletMaterial.SetBuffer(MeshletTrianglesBufferID, m_VisibilityBuffer);
        }

        private void Update()
        {
            if (!SelectedMeshletAsset) return;

            m_IndirectDrawArgsBuffer.SetData(new uint[5] { MESHLET_VERTEX_COUNT, 0, 0, 0, 0 });
            CullingCompute.SetInt(MeshletCountID, m_MeshletCount);

            CullingCompute.Dispatch(m_KernelID, m_KernelGroupX, 1, 1);

            Graphics.DrawProceduralIndirect(m_MeshletMaterial, m_ProxyBounds, MeshTopology.Triangles,
                m_IndirectDrawArgsBuffer);
        }

        private void OnDestroy()
        {
            m_MeshletVerticesBuffer?.Release();
            m_MeshletTrianglesBuffer?.Release();
            m_PositionBuffer?.Release();
            m_IndirectDrawArgsBuffer?.Release();
            m_VisibilityBuffer?.Release();
            m_MeshletsBuffer?.Release();
        }
    }
}