using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;

namespace Nanite
{
    
public class MeshletRenderer : MonoBehaviour
{
    // Constants matching shader values
    private const int AS_GROUP_SIZE = 32;
    private const int MAX_VERTS = 64;
    private const int MAX_PRIMS = 126;
    private const int BATCH_MESHLET_SIZE = AS_GROUP_SIZE;

    // Shader properties
    public ComputeShader cullMeshletsCS; 
    public ComputeShader processMeshletsCS;
    public Material meshletMaterial;
    public MeshletAsset meshletAsset;
    
    // Meshlet data references
    private MeshletCollection meshletCollection;
    private Mesh sourceMesh;
    private int meshletCount;
    
    // Kernel IDs
    private int cullKernelID;
    private int processKernelID;

    // Shader property IDs
    private static readonly int ConstantsID = Shader.PropertyToID("_Constants");
    private static readonly int InstanceID = Shader.PropertyToID("_Instance");
    private static readonly int MeshInfoID = Shader.PropertyToID("_MeshInfo");
    private static readonly int BatchIndexID = Shader.PropertyToID("_BatchIndex");
    private static readonly int DispatchArgsID = Shader.PropertyToID("_DispatchArgs");
    private static readonly int VertexBufferID = Shader.PropertyToID("_VertexBuffer");
    private static readonly int IndexBufferID = Shader.PropertyToID("_IndexBuffer");
    private static readonly int PrimitiveIndicesBufferID = Shader.PropertyToID("_PrimitiveIndices");
    private static readonly int UniqueVertexIndicesBufferID = Shader.PropertyToID("_UniqueVertexIndices");
    private static readonly int VerticesBufferID = Shader.PropertyToID("_Vertices");
    private static readonly int MeshletsBufferID = Shader.PropertyToID("_Meshlets");
    private static readonly int MeshletCullDataBufferID = Shader.PropertyToID("_MeshletCullData");
    
    // ComputeBuffers
    private ComputeBuffer constantsBuffer;
    private ComputeBuffer instanceBuffer;
    private ComputeBuffer meshInfoBuffer;
    private ComputeBuffer dispatchArgsBuffer;
    private ComputeBuffer verticesBuffer;
    private ComputeBuffer uniqueVertexIndicesBuffer;
    private ComputeBuffer primitiveIndicesBuffer;
    private ComputeBuffer meshletsBuffer;
    private ComputeBuffer cullDataBuffer;
    private ComputeBuffer vertexBuffer;
    private ComputeBuffer indexBuffer;
    private ComputeBuffer batchIndexBuffer;
    
    // Batch processing
    private int batchCount;
    private Bounds renderBounds;

    // Structures matching shader definitions
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct Constants
    {
        public Matrix4x4 View;
        public Matrix4x4 ViewProj;
        public Vector4[] Planes;       // Size 6
        public Vector3 ViewPosition;
        public uint HighlightedIndex;
        public Vector3 CullViewPosition;
        public uint SelectedIndex;
        public uint DrawMeshlets;
        
        public Constants(Camera camera)
        {
            View = camera.worldToCameraMatrix;
            ViewProj = GL.GetGPUProjectionMatrix(camera.projectionMatrix, false) * View;
            
            var frustumPlanes = new Plane[6];
            Planes = new Vector4[6];
            GeometryUtility.CalculateFrustumPlanes(camera, frustumPlanes);
            for (int i = 0; i < 6; i++)
            {
                Planes[i] = new Vector4(frustumPlanes[i].normal.x, frustumPlanes[i].normal.y, 
                                      frustumPlanes[i].normal.z, frustumPlanes[i].distance);
            }
            
            ViewPosition = camera.transform.position;
            CullViewPosition = ViewPosition; // Typically the same, can differ for culling optimizations
            HighlightedIndex = 0;
            SelectedIndex = 0;
            DrawMeshlets = 1;
        }
    }
    
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct Instance
    {
        public Matrix4x4 World;
        public Matrix4x4 WorldIT;  // Inverse Transpose for normal transformation
        public float Scale;
        public uint Flags;
        
        public Instance(Transform transform, uint flags = 0)
        {
            World = transform.localToWorldMatrix;
            WorldIT = Matrix4x4.Transpose(Matrix4x4.Inverse(World));
            Scale = transform.lossyScale.x;  // Simplified, assumes uniform scale
            Flags = flags;
        }
    }
    
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct MeshInfo
    {
        public uint IndexSize;
        public uint MeshletCount;
        public uint LastMeshletVertCount;
        public uint LastMeshletPrimCount;
    }
    
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct CullData
    {
        public Vector4 BoundingSphere;
        public uint NormalCone;
        public float ApexOffset;
    }
    
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct Meshlet
    {
        public uint VertCount;
        public uint VertOffset;
        public uint PrimCount;
        public uint PrimOffset;
    }
    
    [System.Serializable]
    [StructLayout(LayoutKind.Sequential)]
    private struct VertexOut
    {
        public Vector4 PositionHS;
        public Vector3 PositionVS;
        public Vector3 Normal;
        public uint MeshletIndex;
    }

    private void Start()
    {
        if (meshletAsset == null)
        {
            Debug.LogError("MeshletAsset is not assigned!");
            return;
        }
        
        // Get meshlet data
        meshletCollection = meshletAsset.Collection;
        sourceMesh = meshletAsset.SourceMesh;
        meshletCount = meshletCollection.meshlets.Length;
        
        // Calculate batch count
        batchCount = Mathf.CeilToInt(meshletCount / (float)BATCH_MESHLET_SIZE);
        
        // Initialize bounds for rendering
        renderBounds = new Bounds(transform.position, Vector3.one * 1000f); // Large bounds
        
        // Find shader kernels
        cullKernelID = cullMeshletsCS.FindKernel("CullMeshlets");
        processKernelID = processMeshletsCS.FindKernel("ProcessMeshlets");
        
        // Initialize buffers
        InitializeBuffers();
        
        // Set up shader bindings
        SetupShaderBindings();
    }

    private void InitializeBuffers()
    {
        // Create constant buffers
        constantsBuffer = new ComputeBuffer(1, Marshal.SizeOf<Constants>());
        instanceBuffer = new ComputeBuffer(1, Marshal.SizeOf<Instance>());
        
        // Set up MeshInfo
        var meshInfo = new MeshInfo
        {
            IndexSize = 4, // Assuming 32-bit indices
            MeshletCount = (uint)meshletCount,
            LastMeshletVertCount = meshletCollection.meshlets[meshletCount-1].VertCount,
            LastMeshletPrimCount = meshletCollection.meshlets[meshletCount-1].PrimCount
        };
        meshInfoBuffer = new ComputeBuffer(1, Marshal.SizeOf<MeshInfo>());
        meshInfoBuffer.SetData(new[] { meshInfo });
        
        // Create source data buffers
        meshletsBuffer = new ComputeBuffer(meshletCount, Marshal.SizeOf<Meshlet>());
        meshletsBuffer.SetData(meshletCollection.meshlets);
        
        verticesBuffer = new ComputeBuffer(sourceMesh.vertexCount, Marshal.SizeOf(typeof(Vector3)) * 2); // Position + Normal
        // Fill vertices buffer (would need to extract positions and normals from mesh)
        
        // Setup unique vertex indices and primitive indices
        uniqueVertexIndicesBuffer = new ComputeBuffer(meshletCollection.vertices.Length, sizeof(uint));
        uniqueVertexIndicesBuffer.SetData(meshletCollection.vertices);
        
        primitiveIndicesBuffer = new ComputeBuffer(meshletCollection.triangles.Length, sizeof(uint));
        primitiveIndicesBuffer.SetData(meshletCollection.triangles);
        
        // Setup cull data if available (simplified)
        cullDataBuffer = new ComputeBuffer(meshletCount, Marshal.SizeOf<CullData>());
        // cullDataBuffer.SetData would go here with actual cull data
        
        // Set up dispatch args buffer
        int dispatchArgsSize = (10 + AS_GROUP_SIZE) * batchCount;
        dispatchArgsBuffer = new ComputeBuffer(dispatchArgsSize, sizeof(uint), ComputeBufferType.IndirectArguments);
        
        // Create output buffers for mesh data
        int totalProcessedMeshlets = batchCount * BATCH_MESHLET_SIZE;
        vertexBuffer = new ComputeBuffer(totalProcessedMeshlets * MAX_VERTS, Marshal.SizeOf<VertexOut>());
        indexBuffer = new ComputeBuffer(totalProcessedMeshlets * MAX_PRIMS * 3, sizeof(uint));
        
        // Batch index buffer
        batchIndexBuffer = new ComputeBuffer(1, sizeof(uint));
    }

    private void SetupShaderBindings()
    {
        // Bind buffers to the cull compute shader
        cullMeshletsCS.SetBuffer(cullKernelID, ConstantsID, constantsBuffer);
        cullMeshletsCS.SetBuffer(cullKernelID, InstanceID, instanceBuffer);
        cullMeshletsCS.SetBuffer(cullKernelID, MeshInfoID, meshInfoBuffer);
        cullMeshletsCS.SetBuffer(cullKernelID, DispatchArgsID, dispatchArgsBuffer);
        cullMeshletsCS.SetBuffer(cullKernelID, MeshletCullDataBufferID, cullDataBuffer);
        
        // Bind buffers to the process compute shader
        processMeshletsCS.SetBuffer(processKernelID, ConstantsID, constantsBuffer);
        processMeshletsCS.SetBuffer(processKernelID, InstanceID, instanceBuffer);
        processMeshletsCS.SetBuffer(processKernelID, MeshInfoID, meshInfoBuffer);
        processMeshletsCS.SetBuffer(processKernelID, DispatchArgsID, dispatchArgsBuffer);
        processMeshletsCS.SetBuffer(processKernelID, BatchIndexID, batchIndexBuffer);
        processMeshletsCS.SetBuffer(processKernelID, MeshletsBufferID, meshletsBuffer);
        processMeshletsCS.SetBuffer(processKernelID, VerticesBufferID, verticesBuffer);
        processMeshletsCS.SetBuffer(processKernelID, UniqueVertexIndicesBufferID, uniqueVertexIndicesBuffer);
        processMeshletsCS.SetBuffer(processKernelID, PrimitiveIndicesBufferID, primitiveIndicesBuffer);
        processMeshletsCS.SetBuffer(processKernelID, VertexBufferID, vertexBuffer);
        processMeshletsCS.SetBuffer(processKernelID, IndexBufferID, indexBuffer);
        
        // Bind buffers to material for final rendering
        meshletMaterial.SetBuffer(VertexBufferID, vertexBuffer);
        meshletMaterial.SetBuffer(IndexBufferID, indexBuffer);
    }

    private void Update()
    {
        if (meshletAsset == null) return;
        
        // Update constant buffers
        var constants = new Constants(Camera.main);
        constantsBuffer.SetData(new[] { constants });
        
        var instance = new Instance(transform);
        instanceBuffer.SetData(new[] { instance });
        
        // Execute CullMeshlets compute shader (AS stage)
        cullMeshletsCS.Dispatch(cullKernelID, Mathf.CeilToInt(meshletCount / (float)AS_GROUP_SIZE), 1, 1);
        
        // Process each batch
        for (int i = 0; i < batchCount; i++)
        {
            // Set batch index
            batchIndexBuffer.SetData(new[] { (uint)i });
            // Execute ProcessMeshlets compute shader (MS stage)
            processMeshletsCS.DispatchIndirect(processKernelID, dispatchArgsBuffer); // We use groups to control batch processing
            
            // Draw this batch
            DrawBatch(i);
        }
    }
    
    private void DrawBatch(int batchIndex)
    {
        var args = new uint[]
        {
            0,  // We'll fill this from the compute shader result
            1,  // instance count
            0,  // start index location (depends on batch)
            0,  // base vertex location
            0   // start instance location
        };
        
        // Read the index count for this batch from the dispatch args buffer
        int argsOffset = (10 + AS_GROUP_SIZE) * batchIndex;
        var batchData = new uint[1];
        dispatchArgsBuffer.GetData(batchData,0, argsOffset + 7, 1); // Read x (visible meshlet count)
        uint visibleMeshletCount = batchData[0];
        
        // Calculate indices to draw
        args[0] = visibleMeshletCount * MAX_PRIMS * 3;
        args[2] = (uint)(MAX_PRIMS * 3 * BATCH_MESHLET_SIZE * batchIndex);
        
        // Since we can't directly modify the index buffer on GPU, we create a small buffer for DrawProcedural
        var drawArgsBuffer = new ComputeBuffer(1, args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        drawArgsBuffer.SetData(args);
        
        // Draw using Graphics.DrawProceduralIndirect
        Graphics.DrawProceduralIndirect(
            meshletMaterial,
            renderBounds,
            MeshTopology.Triangles,
            drawArgsBuffer
        );
        
        drawArgsBuffer.Release();
    }

    private void OnDestroy()
    {
        constantsBuffer?.Release();
        instanceBuffer?.Release();
        meshInfoBuffer?.Release();
        dispatchArgsBuffer?.Release();
        verticesBuffer?.Release();
        uniqueVertexIndicesBuffer?.Release();
        primitiveIndicesBuffer?.Release();
        meshletsBuffer?.Release();
        cullDataBuffer?.Release();
        vertexBuffer?.Release();
        indexBuffer?.Release();
        batchIndexBuffer?.Release();
    }
}

}