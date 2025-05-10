using System;
using System.Collections.Generic;
using UnityEngine;

namespace Nanite
{
    [ExecuteInEditMode]
    public class MeshletRenderer : MonoBehaviour
    {
        [SerializeField] private MeshletData meshletData;
        [SerializeField] private Material material;
        [Range(0.5f, 1.0f)]
        [SerializeField] private float meshletScale = 1.0f;
        [SerializeField] private bool showOnlySelected = false;
        [SerializeField] private int selectedMeshletIndex = -1;
        
        private List<Mesh> meshes = new List<Mesh>();
        private MaterialPropertyBlock propertyBlock;
        
        void OnEnable()
        {
            propertyBlock = new MaterialPropertyBlock();
            
            if (meshletData != null)
                InitializeMeshlets();
        }

        void OnDisable()
        {
            CleanupMeshlets();
        }
        
        void Update()
        {
            if (meshletData == null || material == null || meshes.Count == 0)
                return;
                
            DrawMeshlets();
        }
        
        public void SetMeshletData(MeshletData data)
        {
            CleanupMeshlets();
            meshletData = data;
            InitializeMeshlets();
        }
        
        private void InitializeMeshlets()
        {
            CleanupMeshlets();
            
            if (meshletData == null || meshletData.meshlets == null)
                return;
                
            try
            {
                for (int i = 0; i < meshletData.meshlets.Length; i++)
                {
                    var meshletInfo = meshletData.meshlets[i];
                    if (meshletInfo == null || meshletInfo.vertices == null || meshletInfo.triangles == null)
                        continue;
                        
                    // Create mesh from serialized data
                    Mesh mesh = new Mesh();
                    mesh.name = $"Meshlet_{i}";
                    mesh.vertices = meshletInfo.vertices;
                    mesh.triangles = meshletInfo.triangles;
                    
                    if (meshletInfo.normals != null && meshletInfo.normals.Length > 0)
                    {
                        mesh.normals = meshletInfo.normals;
                    }
                    else
                    {
                        mesh.RecalculateNormals();
                    }
                    
                    meshes.Add(mesh);
                }
                
                Debug.Log($"Initialized {meshes.Count} meshlets for rendering");
            }
            catch (Exception e)
            {
                Debug.LogError($"Error initializing meshlets: {e.Message}");
                CleanupMeshlets();
            }
        }
        
        private void DrawMeshlets()
        {
            for (int i = 0; i < meshes.Count && i < meshletData.meshlets.Length; i++)
            {
                // Skip if only showing selected and this isn't selected
                if (showOnlySelected && selectedMeshletIndex != i)
                    continue;
                    
                // Set the color for this meshlet
                propertyBlock.SetColor("_Color", meshletData.meshlets[i].color);
                
                // Apply scaling
                Matrix4x4 scaledMatrix = Matrix4x4.Scale(Vector3.one * meshletScale);
                Matrix4x4 finalMatrix = transform.localToWorldMatrix * scaledMatrix;
                
                // Draw the mesh
                Graphics.DrawMesh(
                    meshes[i], 
                    finalMatrix, 
                    material, 
                    gameObject.layer, 
                    null, 
                    0, 
                    propertyBlock
                );
            }
        }
        
        private void CleanupMeshlets()
        {
            foreach (var mesh in meshes)
            {
                if (mesh != null)
                {
                    if (Application.isPlaying)
                        Destroy(mesh);
                    else
                        DestroyImmediate(mesh);
                }
            }
            
            meshes.Clear();
        }
        
        void OnValidate()
        {
            // When properties change in the inspector
            if (meshletData != null && meshes.Count == 0)
                InitializeMeshlets();
        }
        
#if UNITY_EDITOR
        // Editor visualization helper
        private void OnDrawGizmosSelected()
        {
            if (meshletData == null || meshes.Count == 0)
                return;
                
            // Draw bounding box for the selected meshlet
            if (selectedMeshletIndex >= 0 && selectedMeshletIndex < meshes.Count)
            {
                Gizmos.color = Color.yellow;
                Bounds bounds = meshes[selectedMeshletIndex].bounds;
                Matrix4x4 scaledMatrix = Matrix4x4.Scale(Vector3.one * meshletScale);
                Matrix4x4 finalMatrix = transform.localToWorldMatrix * scaledMatrix;
                
                Vector3 center = finalMatrix.MultiplyPoint(bounds.center);
                Vector3 size = new Vector3(
                    bounds.size.x * transform.lossyScale.x * meshletScale,
                    bounds.size.y * transform.lossyScale.y * meshletScale,
                    bounds.size.z * transform.lossyScale.z * meshletScale
                );
                
                Gizmos.DrawWireCube(center, size);
            }
        }
#endif
    }
}
