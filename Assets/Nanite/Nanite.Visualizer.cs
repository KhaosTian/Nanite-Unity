using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEditor;

namespace Nanite
{
    [Serializable]
    public class MeshletRenderData
    {
        public Mesh mesh;
        public Matrix4x4 localTransform;
        public Color color;
    }

    public class MeshletVisualizer : EditorWindow
    {
        private GameObject targetObject;
        private Mesh sourceMesh;
        private Material meshletMaterial;
        private MaterialPropertyBlock propertyBlock;
        private List<MeshletRenderData> meshletData = new List<MeshletRenderData>();
        private bool showMeshlets = false;
        private Vector2 scrollPosition;
        private float meshletScale = 1.0f;
        private int selectedMeshletIndex = -1;

        [MenuItem("Window/Meshlet Visualizer")]
        public static void ShowWindow()
        {
            GetWindow<MeshletVisualizer>("Meshlet Visualizer");
        }

        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;

            // Create material for rendering meshlets
            meshletMaterial = new Material(Shader.Find("Standard"));
            meshletMaterial.enableInstancing = true;
            propertyBlock = new MaterialPropertyBlock();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            CleanupMeshlets();

            if (meshletMaterial != null)
                DestroyImmediate(meshletMaterial);
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Meshlet Visualization Tool", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Object selection
            EditorGUI.BeginChangeCheck();
            targetObject =
                EditorGUILayout.ObjectField("Target Mesh Object", targetObject, typeof(GameObject), true) as GameObject;
            if (EditorGUI.EndChangeCheck())
            {
                CleanupMeshlets();
                showMeshlets = false;
            }

            if (targetObject == null)
                return;

            MeshFilter meshFilter = targetObject.GetComponent<MeshFilter>();
            if (meshFilter == null || meshFilter.sharedMesh == null)
            {
                EditorGUILayout.HelpBox("Selected object doesn't have a valid mesh.", MessageType.Warning);
                return;
            }

            sourceMesh = meshFilter.sharedMesh;

            // Controls
            EditorGUILayout.Space();
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(showMeshlets ? "Hide Meshlets" : "Generate Meshlets"))
            {
                if (!showMeshlets)
                {
                    GenerateMeshlets();
                }
                else
                {
                    CleanupMeshlets();
                }

                showMeshlets = !showMeshlets;
                SceneView.RepaintAll();
            }

            if (GUILayout.Button("Refresh"))
            {
                if (showMeshlets)
                {
                    CleanupMeshlets();
                    GenerateMeshlets();
                    SceneView.RepaintAll();
                }
            }

            EditorGUILayout.EndHorizontal();

            // Display options
            if (showMeshlets && meshletData.Count > 0)
            {
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Display Options", EditorStyles.boldLabel);

                EditorGUI.BeginChangeCheck();
                meshletScale = EditorGUILayout.Slider("Meshlet Scale", meshletScale, 0.5f, 1.0f);
                if (EditorGUI.EndChangeCheck())
                {
                    SceneView.RepaintAll();
                }

                // Meshlet count info
                EditorGUILayout.Space();
                EditorGUILayout.LabelField($"Total Meshlets: {meshletData.Count}");

                // Meshlet list
                EditorGUILayout.Space();
                EditorGUILayout.LabelField("Meshlets", EditorStyles.boldLabel);
                scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.Height(200));

                for (int i = 0; i < meshletData.Count; i++)
                {
                    EditorGUI.BeginChangeCheck();
                    bool isSelected = (selectedMeshletIndex == i);
                    bool newSelected =
                        EditorGUILayout.ToggleLeft($"Meshlet {i} ({meshletData[i].mesh.vertexCount} vertices)",
                            isSelected);
                    if (EditorGUI.EndChangeCheck())
                    {
                        selectedMeshletIndex = newSelected ? i : -1;
                        SceneView.RepaintAll();
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!showMeshlets || targetObject == null || meshletData.Count == 0)
                return;

            Matrix4x4 objectMatrix = targetObject.transform.localToWorldMatrix;

            // Render meshlets
            for (int i = 0; i < meshletData.Count; i++)
            {
                var data = meshletData[i];

                // Skip rendering if this isn't the selected meshlet (when one is selected)
                if (selectedMeshletIndex != -1 && selectedMeshletIndex != i)
                    continue;

                // Adjust the color alpha for better visibility
                Color renderColor = data.color;
                propertyBlock.SetColor("_Color", renderColor);

                // Apply scaling based on the slider
                Matrix4x4 scaledMatrix = Matrix4x4.Scale(Vector3.one * meshletScale);
                Matrix4x4 finalMatrix = objectMatrix * data.localTransform * scaledMatrix;

                Graphics.DrawMesh(data.mesh, finalMatrix, meshletMaterial, 0, sceneView.camera, 0, propertyBlock);
            }
        }

        private void GenerateMeshlets()
        {
            if (sourceMesh == null) return;

            CleanupMeshlets();

            try
            {
                // Start showing progress
                EditorUtility.DisplayProgressBar("Meshlet Generator", "Preparing mesh data...", 0.1f);

                // Convert mesh data for the API
                int[] triangles = sourceMesh.triangles;
                uint[] indices = new uint[triangles.Length];
                for (int i = 0; i < triangles.Length; i++)
                    indices[i] = (uint)triangles[i];

                // Update progress before potentially long operation
                EditorUtility.DisplayProgressBar("Meshlet Generator", "Processing mesh for meshlets...", 0.3f);

                // Process the mesh to get meshlets data
                MeshletsContext meshletsContext = NanitePlugin.ProcessMesh(indices, sourceMesh.vertices);

                Debug.Log($"Generated {meshletsContext.meshlets.Length} meshlets");

                // Create individual meshlet meshes with progress updates
                for (int i = 0; i < meshletsContext.meshlets.Length; i++)
                {
                    // Calculate progress (from 0.4 to 0.9 during meshlet creation)
                    float progress = 0.4f + (0.5f * i / meshletsContext.meshlets.Length);
                    EditorUtility.DisplayProgressBar("Meshlet Generator",
                        $"Creating meshlet {i + 1}/{meshletsContext.meshlets.Length}...",
                        progress);

                    MeshletRenderData renderData =
                        CreateMeshletMesh(meshletsContext.meshlets[i], meshletsContext, i, sourceMesh);
                    if (renderData != null)
                    {
                        meshletData.Add(renderData);
                    }
                }

                // Final progress update
                EditorUtility.DisplayProgressBar("Meshlet Generator", "Finalizing...", 0.95f);
            }
            catch (Exception e)
            {
                Debug.LogError($"Error generating meshlets: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                // Make sure to clear the progress bar when done or if an exception occurs
                EditorUtility.ClearProgressBar();
            }
        }


        private void CleanupMeshlets()
        {
            foreach (var renderData in meshletData)
            {
                if (renderData.mesh != null)
                {
                    DestroyImmediate(renderData.mesh);
                }
            }

            meshletData.Clear();
            selectedMeshletIndex = -1;
        }

        private MeshletRenderData CreateMeshletMesh(Meshlet meshlet, MeshletsContext data, int meshletIndex,
            Mesh sourceMesh)
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
                if (idx0 >= sourceMesh.vertexCount || idx1 >= sourceMesh.vertexCount ||
                    idx2 >= sourceMesh.vertexCount)
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
}