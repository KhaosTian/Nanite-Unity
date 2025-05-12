using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Unity.VisualScripting;
using UnityEngine;
using UnityEditor;

namespace Nanite
{
    // Serializable asset to store meshlet data
    [CreateAssetMenu(fileName = "MeshletData", menuName = "Nanite/Meshlet Data")]
    public class MeshletData : ScriptableObject
    {
        [Serializable]
        public class SerializedMeshlet
        {
            public Vector3[] vertices;
            public int[] triangles;
            public Vector3[] normals;
            public Color color;
        }

        public SerializedMeshlet[] meshlets;
        public Mesh sourceMesh;
    }

    // Editor window for generating meshlets
    public class MeshletGenerator : EditorWindow
    {
        private UnityEngine.Object targetObject;
        private Mesh sourceMesh;
        private Material previewMaterial;
        private List<MeshletRenderData> previewMeshlets = new List<MeshletRenderData>();
        private MaterialPropertyBlock propertyBlock;
        private bool showPreview = false;
        private Vector2 scrollPosition;
        private float previewScale = 1.0f;
        private int selectedMeshletIndex = -1;
        private MeshletData generatedData;
        private string savePath = "Assets/MeshletData.asset";

        [MenuItem("Window/Nanite/Meshlet Generator")]
        public static void ShowWindow()
        {
            GetWindow<MeshletGenerator>("Meshlet Generator");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            
            // Create material for preview
            previewMaterial = new Material(Shader.Find("Standard"));
            previewMaterial.enableInstancing = true;
            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            CleanupPreview();

            if (previewMaterial != null)
                DestroyImmediate(previewMaterial);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Meshlet Generator", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Object selection
            EditorGUI.BeginChangeCheck();
            targetObject = EditorGUILayout.ObjectField("Mesh", targetObject, typeof(Mesh), false);
            if (EditorGUI.EndChangeCheck())
            {
                CleanupPreview();
                showPreview = false;
            }

            if (targetObject == null)
                return;


            sourceMesh = targetObject as Mesh;

            // Controls
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(showPreview ? "Hide Preview" : "Generate Preview"))
            {
                if (!showPreview)
                {
                    GenerateMeshlets();
                }
                else
                {
                    CleanupPreview();
                }

                showPreview = !showPreview;
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Refresh Preview") && showPreview)
            {
                CleanupPreview();
                GenerateMeshlets();
                SceneView.RepaintAll();
            }

            EditorGUILayout.EndHorizontal();

            // Display options
            if (showPreview && previewMeshlets.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Preview Options", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                previewScale = EditorGUILayout.Slider("Meshlet Scale", previewScale, 0.5f, 100.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    SceneView.RepaintAll();
                }

                // Meshlet count info
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Total Meshlets: {previewMeshlets.Count}");

                // Save options
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Save Meshlet Data", EditorStyles.boldLabel);
                savePath = EditorGUILayout.TextField("Save Path", savePath);
                
                if (GUILayout.Button("Save Meshlet Data"))
                {
                    SaveMeshletData();
                }

                // Meshlet list for preview selection
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Preview Meshlets", EditorStyles.boldLabel);
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

                for (int i = 0; i < previewMeshlets.Count; i++)
                {
                    EditorGUI.BeginChangeCheck();
                    bool isSelected = (selectedMeshletIndex == i);
                    bool newSelected = EditorGUILayout.ToggleLeft(
                        $"Meshlet {i} ({previewMeshlets[i].mesh.vertexCount} vertices)", isSelected);
                    if (EditorGUI.EndChangeCheck())
                    {
                        selectedMeshletIndex = newSelected ? i : -1;
                        SceneView.RepaintAll();
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void SaveMeshletData()
        {
            if (previewMeshlets.Count == 0 || generatedData == null)
            {
                EditorUtility.DisplayDialog("Error", "No meshlet data to save.", "OK");
                return;
            }

            // Make sure the path has the right extension
            if (!savePath.EndsWith(".asset"))
                savePath += ".asset";

            // Create the directory if it doesn't exist
            string directory = System.IO.Path.GetDirectoryName(savePath);
            if (!System.IO.Directory.Exists(directory))
                System.IO.Directory.CreateDirectory(directory);

            // Save source mesh reference
            generatedData.sourceMesh = sourceMesh;

            // Save the asset
            AssetDatabase.CreateAsset(generatedData, savePath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayDialog("Success", $"Meshlet data saved to {savePath}", "OK");
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!showPreview || targetObject == null || previewMeshlets.Count == 0)
                return;

            Matrix4x4 objectMatrix = Matrix4x4.identity;

            // Render meshlets
            for (int i = 0; i < previewMeshlets.Count; i++)
            {
                var data = previewMeshlets[i];

                // Skip rendering if this isn't the selected meshlet (when one is selected)
                if (selectedMeshletIndex != -1 && selectedMeshletIndex != i)
                    continue;

                propertyBlock.SetColor("_Color", data.color);

                // Apply scaling based on the slider
                Matrix4x4 scaledMatrix = Matrix4x4.Scale(Vector3.one * previewScale);
                Matrix4x4 finalMatrix = objectMatrix * data.localTransform * scaledMatrix;

                Graphics.DrawMesh(data.mesh, finalMatrix, previewMaterial, 0, sceneView.camera, 0, propertyBlock);
            }
        }

        private void GenerateMeshlets()
        {
            if (sourceMesh == null) return;

            CleanupPreview();

            try
            {
                EditorUtility.DisplayProgressBar("Meshlet Generator", "Preparing mesh data...", 0.1f);

                // Convert mesh data for the API
                int[] triangles = sourceMesh.triangles;
                uint[] indices = new uint[triangles.Length];
                for (int i = 0; i < triangles.Length; i++)
                    indices[i] = (uint)triangles[i];

                EditorUtility.DisplayProgressBar("Meshlet Generator", "Processing mesh for meshlets...", 0.3f);

                // Process the mesh to get meshlets data
                MeshletsContext meshletsContext = NanitePlugin.ProcessMesh(indices, sourceMesh.vertices);

                Debug.Log($"Generated {meshletsContext.meshlets.Length} meshlets");
                
                StringBuilder meshletInfo = new StringBuilder();
                
                // Create serializable meshlet data asset
                generatedData = CreateInstance<MeshletData>();
                generatedData.meshlets = new MeshletData.SerializedMeshlet[meshletsContext.meshlets.Length];
                
                // Create individual meshlet meshes with progress updates
                for (int i = 0; i < meshletsContext.meshlets.Length; i++)
                {
                    float progress = 0.4f + (0.5f * i / meshletsContext.meshlets.Length);
                    EditorUtility.DisplayProgressBar("Meshlet Generator", 
                        $"Creating meshlet {i + 1}/{meshletsContext.meshlets.Length}...", 
                        progress);

                    MeshletRenderData renderData = CreateMeshletMesh(meshletsContext.meshlets[i], meshletsContext, i, sourceMesh);
                    if (renderData != null)
                    {
                        previewMeshlets.Add(renderData);
                        
                        // Create serialized meshlet data
                        SerializeMeshletData(renderData, i);
                        
                        meshletInfo.AppendLine($"Meshlet {i}: " +
                                            $"IndicesOffset={meshletsContext.meshlets[i].VertexOffset}, " +
                                            $"PrimitivesOffset={meshletsContext.meshlets[i].TriangleOffset}, " +
                                            $"IndicesCount={meshletsContext.meshlets[i].VertexCount}, " +
                                            $"PrimitivesCount={meshletsContext.meshlets[i].TriangleCount}" );
                    }
                }
                
                Debug.Log($"Meshlets: {meshletsContext.meshlets.Length}, Indices: {meshletsContext.vertices.Length}, Primitives: {meshletsContext.triangles.Length}");
                Debug.Log(meshletInfo);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error generating meshlets: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void SerializeMeshletData(MeshletRenderData renderData, int index)
        {
            var serializedMeshlet = new MeshletData.SerializedMeshlet
            {
                vertices = renderData.mesh.vertices,
                triangles = renderData.mesh.triangles,
                normals = renderData.mesh.normals,
                color = renderData.color
            };
            
            generatedData.meshlets[index] = serializedMeshlet;
        }

        private void CleanupPreview()
        {
            foreach (var renderData in previewMeshlets)
            {
                if (renderData.mesh != null)
                {
                    DestroyImmediate(renderData.mesh);
                }
            }

            previewMeshlets.Clear();
            selectedMeshletIndex = -1;
            
            if (generatedData != null && !AssetDatabase.Contains(generatedData))
            {
                DestroyImmediate(generatedData);
                generatedData = null;
            }
        }

        private MeshletRenderData CreateMeshletMesh(Meshlet meshlet, MeshletsContext data, int meshletIndex, Mesh sourceMesh)
        {
            // For the current meshlet create a color
            Color color = GetMeshletColor(meshletIndex);

            // Collect vertices and indices
            Dictionary<uint, int> vertexMap = new Dictionary<uint, int>();
            List<Vector3> vertices = new List<Vector3>();
            List<int> triangles = new List<int>();
            List<Color> colors = new List<Color>();
            List<Vector3> normals = new List<Vector3>();

            uint primEnd = Math.Min(meshlet.TriangleOffset + meshlet.TriangleCount * 3, (uint)data.triangles.Length);

            for (uint p = meshlet.TriangleOffset; p < primEnd; p += 3)
            {
                if (p + 2 >= data.triangles.Length)
                    break;

                byte prim0 = data.triangles[p];
                byte prim1 = data.triangles[p + 1];
                byte prim2 = data.triangles[p + 2];

                // Ensure within index range
                if (prim0 >= meshlet.VertexCount || prim1 >= meshlet.VertexCount || prim2 >= meshlet.VertexCount)
                    continue;

                uint idx0 = data.vertices[meshlet.VertexOffset + prim0];
                uint idx1 = data.vertices[meshlet.VertexOffset + prim1];
                uint idx2 = data.vertices[meshlet.VertexOffset + prim2];

                // Vertex index validation
                if (idx0 >= sourceMesh.vertexCount || idx1 >= sourceMesh.vertexCount || idx2 >= sourceMesh.vertexCount)
                    continue;

                // Add vertices to local mapping
                int localIdx0 = GetOrAddVertex(idx0, vertexMap, vertices, colors, normals, color, sourceMesh);
                int localIdx1 = GetOrAddVertex(idx1, vertexMap, vertices, colors, normals, color, sourceMesh);
                int localIdx2 = GetOrAddVertex(idx2, vertexMap, vertices, colors, normals, color, sourceMesh);

                // Add triangle
                triangles.Add(localIdx0);
                triangles.Add(localIdx1);
                triangles.Add(localIdx2);
            }

            if (vertices.Count == 0 || triangles.Count == 0)
                return null;

            // Create mesh
            Mesh mesh = new Mesh();
            mesh.name = $"Meshlet_{meshletIndex}";
            mesh.hideFlags = HideFlags.HideAndDontSave;
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.SetColors(colors);

            if (sourceMesh.normals != null && sourceMesh.normals.Length > 0)
            {
                mesh.SetNormals(normals);
            }
            else
            {
                mesh.RecalculateNormals();
            }

            // Create and return render data
            MeshletRenderData renderData = new MeshletRenderData
            {
                mesh = mesh,
                localTransform = Matrix4x4.identity,
                color = color,
            };

            return renderData;
        }

        // Helper method to get or add a vertex to the local vertex list
        private int GetOrAddVertex(uint globalIndex, Dictionary<uint, int> vertexMap, List<Vector3> vertices,
            List<Color> colors, List<Vector3> normals, Color color, Mesh sourceMesh)
        {
            if (vertexMap.TryGetValue(globalIndex, out int localIndex))
                return localIndex;

            localIndex = vertices.Count;
            vertexMap[globalIndex] = localIndex;

            vertices.Add(sourceMesh.vertices[globalIndex]);
            colors.Add(color);

            if (sourceMesh.normals != null && sourceMesh.normals.Length > 0)
            {
                normals.Add(sourceMesh.normals[globalIndex]);
            }

            return localIndex;
        }

        // Method to generate a color based on meshlet index
        private Color GetMeshletColor(int meshletIndex)
        {
            // Use golden ratio to create visually distinct colors
            float hue = (meshletIndex * 0.618033988749895f) % 1.0f;
            return Color.HSVToRGB(hue, 0.7f, 0.95f);
        }
    }

    // Class to hold meshlet render data for the preview
    [Serializable]
    public class MeshletRenderData
    {
        public Mesh mesh;
        public Matrix4x4 localTransform;
        public Color color;
    }
}

