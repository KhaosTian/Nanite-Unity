using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Nanite
{
    public class NaniteBuilderEditor : EditorWindow
    {
        // Window state
        private Mesh selectedMesh;
        private UnityEngine.Object meshAsset;
        private GameObject previewObject;
        private int selectedSubmeshIndex = 0;
        private bool showWireframe = true;
        private bool showMeshlets = true;
        private bool showVertexNormals = false;
        private Color meshletColor = new Color(0.2f, 1f, 0.3f, 0.8f);
        private float meshletScale = 1.0f;

        // Processed data
        private Dictionary<int, MeshletsContext> submeshDataMap = new Dictionary<int, MeshletsContext>();

        // Editor scroll position
        private Vector2 scrollPosition;

        // 材质和着色器
        private Material meshletWireframeMaterial;

        private Material meshletSolidMaterial;

        // 网格缓存
        private Dictionary<int, List<MeshletRenderData>> meshletRenderCache =
            new Dictionary<int, List<MeshletRenderData>>();

        private bool cacheDirty = true;

        // 缓存控制
        private bool useGPUInstancing = true;
        private int maxMeshletsToRender = 1000;

        private float meshletDrawDistance = 20f;

        // 自定义数据结构
        private class MeshletRenderData
        {
            public Mesh mesh;
            public Matrix4x4 localTransform;
            public Color color;
            public bool hasNormals;
            public Mesh normalsMesh; // 可选的法线可视化网格
        }


        // Add menu item to open the editor window
        [MenuItem("Window/Nanite/Meshlet Builder")]
        public static void ShowWindow()
        {
            NaniteBuilderEditor window = GetWindow<NaniteBuilderEditor>("Nanite Meshlet Builder");
            window.minSize = new Vector2(350, 500);
            window.Show();
        }

        private void InitializeMaterials()
        {
            if (meshletWireframeMaterial == null)
            {
                // 线框材质
                Shader wireframeShader = Shader.Find("Hidden/Internal-Colored") ??
                                         Shader.Find("Legacy Shaders/Transparent/Diffuse");
                meshletWireframeMaterial = new Material(wireframeShader);
                meshletWireframeMaterial.hideFlags = HideFlags.HideAndDontSave;
                meshletWireframeMaterial.SetInt("_ZWrite", 1);
                meshletWireframeMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.LessEqual);
                meshletWireframeMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
                meshletWireframeMaterial.SetFloat("_Mode", 2); // 透明模式
                meshletWireframeMaterial.EnableKeyword("_ALPHABLEND_ON");
                meshletWireframeMaterial.renderQueue = 3000;

                // 启用实例化
                meshletWireframeMaterial.enableInstancing = true;
            }

            if (meshletSolidMaterial == null)
            {
                // 实体材质 (用于顶点法线或meshlet面)
                meshletSolidMaterial = new Material(Shader.Find("Standard"));
                meshletSolidMaterial.hideFlags = HideFlags.HideAndDontSave;
                meshletSolidMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                meshletSolidMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                meshletSolidMaterial.SetInt("_ZWrite", 1);
                meshletSolidMaterial.DisableKeyword("_ALPHATEST_ON");
                meshletSolidMaterial.EnableKeyword("_ALPHABLEND_ON");
                meshletSolidMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                meshletSolidMaterial.renderQueue = 3000;

                // 启用实例化
                meshletSolidMaterial.enableInstancing = true;
            }
        }


        private void CleanupMaterials()
        {
            if (meshletWireframeMaterial != null)
                DestroyImmediate(meshletWireframeMaterial);

            if (meshletSolidMaterial != null)
                DestroyImmediate(meshletSolidMaterial);

            // 清除所有缓存的网格
            foreach (var meshletList in meshletRenderCache.Values)
            {
                foreach (var data in meshletList)
                {
                    if (data.mesh != null)
                        DestroyImmediate(data.mesh);
                    if (data.normalsMesh != null)
                        DestroyImmediate(data.normalsMesh);
                }
            }

            meshletRenderCache.Clear();
        }


        private void OnGUI()
        {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            EditorGUILayout.LabelField("Nanite Meshlet Builder", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            DrawMeshSelection();
            EditorGUILayout.Space();

            if (selectedMesh != null)
            {
                DrawSubmeshSelection();
                EditorGUILayout.Space();

                DrawBakeOptions();
                EditorGUILayout.Space();

                DrawVisualizationOptions();
                EditorGUILayout.Space();

                DrawStatistics();
                EditorGUILayout.Space();

                DrawActions();
            }

            EditorGUILayout.EndScrollView();

            // Handle scene view updates
            if (Event.current.type == EventType.Repaint)
            {
                SceneView.RepaintAll();
            }
        }

        private void DrawMeshSelection()
        {
            EditorGUILayout.LabelField("Mesh Selection", EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            meshAsset = EditorGUILayout.ObjectField("Mesh", meshAsset, typeof(Mesh), false);
            if (EditorGUI.EndChangeCheck())
            {
                if (meshAsset != null)
                {
                    selectedMesh = meshAsset as Mesh;
                    selectedSubmeshIndex = 0;
                    submeshDataMap.Clear();

                    CreatePreviewObject();
                }
                else
                {
                    selectedMesh = null;
                    DestroyPreviewObject();
                }
            }

            // Allow drag and drop
            Rect dropRect = GUILayoutUtility.GetRect(0, 50, GUILayout.ExpandWidth(true));
            GUI.Box(dropRect, "Drop a mesh here", EditorStyles.helpBox);

            Event evt = Event.current;
            switch (evt.type)
            {
                case EventType.DragUpdated:
                case EventType.DragPerform:
                    if (!dropRect.Contains(evt.mousePosition))
                        break;

                    DragAndDrop.visualMode = DragAndDropVisualMode.Copy;

                    if (evt.type == EventType.DragPerform)
                    {
                        DragAndDrop.AcceptDrag();

                        foreach (UnityEngine.Object draggedObject in DragAndDrop.objectReferences)
                        {
                            if (draggedObject is Mesh mesh)
                            {
                                meshAsset = draggedObject;
                                selectedMesh = mesh;
                                selectedSubmeshIndex = 0;
                                submeshDataMap.Clear();
                                CreatePreviewObject();
                                break;
                            }
                            else if (draggedObject is GameObject go)
                            {
                                MeshFilter mf = go.GetComponent<MeshFilter>();
                                if (mf != null && mf.sharedMesh != null)
                                {
                                    meshAsset = mf.sharedMesh;
                                    selectedMesh = mf.sharedMesh;
                                    selectedSubmeshIndex = 0;
                                    submeshDataMap.Clear();
                                    CreatePreviewObject();
                                    break;
                                }
                            }
                        }
                    }

                    evt.Use();
                    break;
            }
        }

        private void DrawSubmeshSelection()
        {
            if (selectedMesh.subMeshCount > 1)
            {
                EditorGUILayout.LabelField("Submesh Selection", EditorStyles.boldLabel);
                string[] submeshOptions = new string[selectedMesh.subMeshCount];
                for (int i = 0; i < selectedMesh.subMeshCount; i++)
                {
                    submeshOptions[i] = $"Submesh {i}";
                }

                selectedSubmeshIndex = EditorGUILayout.Popup("Submesh", selectedSubmeshIndex, submeshOptions);
            }
        }

        private void DrawBakeOptions()
        {
            EditorGUILayout.LabelField("Bake Options", EditorStyles.boldLabel);

            if (GUILayout.Button("Generate Meshlets for Current Submesh"))
            {
                GenerateMeshletsForSubmesh(selectedSubmeshIndex);
                DebugMeshletData();
            }

            if (selectedMesh.subMeshCount > 1 && GUILayout.Button("Generate Meshlets for All Submeshes"))
            {
                for (int i = 0; i < selectedMesh.subMeshCount; i++)
                {
                    GenerateMeshletsForSubmesh(i);
                }

                EditorUtility.DisplayDialog("Nanite Meshlet Builder", "Successfully processed all submeshes", "OK");
            }
        }

        private void GenerateMeshletRenderData(int submeshIndex)
        {
            if (!cacheDirty && meshletRenderCache.ContainsKey(submeshIndex))
                return;

            if (!submeshDataMap.TryGetValue(submeshIndex, out MeshletsContext data))
                return;

            // 清除现有的缓存数据
            if (meshletRenderCache.ContainsKey(submeshIndex))
            {
                foreach (var renderData in meshletRenderCache[submeshIndex])
                {
                    if (renderData.mesh != null)
                        DestroyImmediate(renderData.mesh);
                    if (renderData.normalsMesh != null)
                        DestroyImmediate(renderData.normalsMesh);
                }
            }

            List<MeshletRenderData> renderDataList = new List<MeshletRenderData>();
            int maxMeshletsToProcess = Mathf.Min(data.meshlets.Length, maxMeshletsToRender);

            EditorUtility.DisplayProgressBar("Building Meshlet Visualization",
                "Processing meshlets...", 0);

            try
            {
                for (int i = 0; i < maxMeshletsToProcess; i++)
                {
                    if (i % 50 == 0)
                    {
                        float progress = (float)i / maxMeshletsToProcess;
                        EditorUtility.DisplayProgressBar("Building Meshlet Visualization",
                            $"Processing meshlet {i}/{maxMeshletsToProcess}", progress);
                    }

                    var meshlet = data.meshlets[i];

                    // 检查边界防止崩溃
                    if (meshlet.VertexOffset + meshlet.VertexCount > data.vertices.Length ||
                        meshlet.TriangleOffset + meshlet.TriangleCount > data.triangles.Length)
                        continue;

                    // 创建这个meshlet的渲染数据
                    MeshletRenderData renderData = CreateMeshletMesh(meshlet, data, i, selectedMesh);
                    if (renderData != null && renderData.mesh != null)
                    {
                        renderDataList.Add(renderData);
                    }
                }

                meshletRenderCache[submeshIndex] = renderDataList;
                cacheDirty = false;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private MeshletRenderData CreateMeshletMesh(Meshlet meshlet, MeshletsContext data, int meshletIndex,
            Mesh sourceMesh)
        {
            try
            {
                // 为当前meshlet创建颜色
                Color color = GetMeshletColor(meshletIndex);

                // 收集顶点和索引
                Dictionary<uint, int> vertexMap = new Dictionary<uint, int>();
                List<Vector3> vertices = new List<Vector3>();
                List<int> triangles = new List<int>();
                List<Color> colors = new List<Color>();
                List<Vector3> normals = new List<Vector3>();

                uint primEnd = Math.Min(meshlet.TriangleOffset + meshlet.TriangleCount, (uint)data.triangles.Length);

                for (uint p = meshlet.TriangleOffset; p < primEnd; p += 3)
                {
                    if (p + 2 >= data.triangles.Length)
                        break;

                    byte prim0 = data.triangles[p];
                    byte prim1 = data.triangles[p + 1];
                    byte prim2 = data.triangles[p + 2];

                    // 确保在索引范围内
                    if (prim0 >= meshlet.VertexCount || prim1 >= meshlet.VertexCount || prim2 >= meshlet.VertexCount)
                        continue;

                    uint idx0 = data.vertices[meshlet.VertexOffset + prim0];
                    uint idx1 = data.vertices[meshlet.VertexOffset + prim1];
                    uint idx2 = data.vertices[meshlet.VertexOffset + prim2];

                    // 顶点索引验证
                    if (idx0 >= sourceMesh.vertexCount || idx1 >= sourceMesh.vertexCount ||
                        idx2 >= sourceMesh.vertexCount)
                        continue;

                    // 添加顶点到本地映射
                    int localIdx0 = GetOrAddVertex(idx0, vertexMap, vertices, colors, normals, color, sourceMesh);
                    int localIdx1 = GetOrAddVertex(idx1, vertexMap, vertices, colors, normals, color, sourceMesh);
                    int localIdx2 = GetOrAddVertex(idx2, vertexMap, vertices, colors, normals, color, sourceMesh);

                    // 添加三角形
                    triangles.Add(localIdx0);
                    triangles.Add(localIdx1);
                    triangles.Add(localIdx2);
                }

                if (vertices.Count == 0 || triangles.Count == 0)
                    return null;

                // 创建网格
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

                // 创建并返回渲染数据
                MeshletRenderData renderData = new MeshletRenderData
                {
                    mesh = mesh,
                    localTransform = Matrix4x4.identity,
                    color = color,
                    hasNormals = (sourceMesh.normals != null && sourceMesh.normals.Length > 0)
                };

                // 如果需要显示法线，创建法线显示网格
                if (showVertexNormals && renderData.hasNormals)
                {
                    renderData.normalsMesh = CreateNormalsMesh(vertices, normals);
                }

                return renderData;
            }
            catch (Exception ex)
            {
                Debug.LogError($"创建meshlet网格时错误: {ex.Message}\n{ex.StackTrace}");
                return null;
            }
        }

        private int GetOrAddVertex(uint sourceIdx, Dictionary<uint, int> vertexMap, List<Vector3> vertices,
            List<Color> colors, List<Vector3> normals, Color color, Mesh sourceMesh)
        {
            if (!vertexMap.TryGetValue(sourceIdx, out int localIdx))
            {
                localIdx = vertices.Count;
                vertexMap[sourceIdx] = localIdx;

                // 添加顶点
                vertices.Add(sourceMesh.vertices[sourceIdx]);
                colors.Add(color);

                // 如果有法线，添加法线
                if (sourceMesh.normals != null && sourceMesh.normals.Length > sourceIdx)
                {
                    normals.Add(sourceMesh.normals[sourceIdx]);
                }
            }

            return localIdx;
        }

        private Mesh CreateNormalsMesh(List<Vector3> vertices, List<Vector3> normals)
        {
            // 创建法线可视化网格（线段）
            List<Vector3> normalLineVerts = new List<Vector3>();
            List<int> normalLineIndices = new List<int>();
            List<Color> normalColors = new List<Color>();

            float normalLength = 0.1f * meshletScale;

            for (int i = 0; i < vertices.Count; i++)
            {
                if (i >= normals.Count) continue;

                Vector3 start = vertices[i];
                Vector3 end = start + normals[i] * normalLength;

                int baseIdx = normalLineVerts.Count;
                normalLineVerts.Add(start);
                normalLineVerts.Add(end);

                normalColors.Add(Color.blue);
                normalColors.Add(Color.cyan);

                normalLineIndices.Add(baseIdx);
                normalLineIndices.Add(baseIdx + 1);
            }

            Mesh normalsMesh = new Mesh();
            normalsMesh.name = "NormalsMesh";
            normalsMesh.hideFlags = HideFlags.HideAndDontSave;
            normalsMesh.SetVertices(normalLineVerts);
            normalsMesh.SetIndices(normalLineIndices, MeshTopology.Lines, 0);
            normalsMesh.SetColors(normalColors);

            return normalsMesh;
        }

        private void DrawVisualizationOptions()
        {
            EditorGUILayout.LabelField("Visualization Options", EditorStyles.boldLabel);
            showWireframe = EditorGUILayout.Toggle("Show Wireframe", showWireframe);
            showMeshlets = EditorGUILayout.Toggle("Show Meshlets", showMeshlets);
            showVertexNormals = EditorGUILayout.Toggle("Show Vertex Normals", showVertexNormals);
            useGPUInstancing = EditorGUILayout.Toggle("Use GPU Instancing", useGPUInstancing);
            meshletColor = EditorGUILayout.ColorField("Meshlet Color", meshletColor);
            meshletScale = EditorGUILayout.Slider("Meshlet Scale", meshletScale, 0.1f, 2.0f);
            maxMeshletsToRender = EditorGUILayout.IntSlider("Max Meshlets", maxMeshletsToRender, 10, 5000);
            meshletDrawDistance = EditorGUILayout.FloatField("Draw Distance", meshletDrawDistance);

            if (GUILayout.Button("Rebuild Visualization Cache"))
            {
                cacheDirty = true;
            }
        }


        private void DrawStatistics()
        {
            EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);

            if (selectedMesh != null)
            {
                EditorGUILayout.LabelField($"Mesh: {selectedMesh.name}");
                EditorGUILayout.LabelField($"Vertices: {selectedMesh.vertexCount}");
                EditorGUILayout.LabelField($"Submeshes: {selectedMesh.subMeshCount}");

                if (submeshDataMap.TryGetValue(selectedSubmeshIndex, out MeshletsContext data))
                {
                    EditorGUILayout.LabelField($"Meshlets: {data.meshlets.Length}");
                    EditorGUILayout.LabelField($"Total indices: {data.vertices.Length}");
                    EditorGUILayout.LabelField($"Total primitives: {data.triangles.Length}");

                    float avgVerticesPerMeshlet =
                        data.meshlets.Length > 0 ? (float)data.vertices.Length / data.meshlets.Length : 0;
                    float avgTrianglesPerMeshlet = data.meshlets.Length > 0
                        ? (float)(data.triangles.Length / 3) / data.meshlets.Length
                        : 0;

                    EditorGUILayout.LabelField($"Avg. vertices per meshlet: {avgVerticesPerMeshlet:F1}");
                    EditorGUILayout.LabelField($"Avg. triangles per meshlet: {avgTrianglesPerMeshlet:F1}");
                }
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.LabelField("Actions", EditorStyles.boldLabel);

            if (GUILayout.Button("Export Meshlets as Asset"))
            {
                ExportMeshletsAsAsset();
            }

            if (GUILayout.Button("Clear All Data"))
            {
                submeshDataMap.Clear();
                DestroyPreviewObject();
                selectedMesh = null;
                meshAsset = null;
            }
        }

        private void GenerateMeshletsForSubmesh(int submeshIndex)
        {
            if (selectedMesh == null || submeshIndex < 0 || submeshIndex >= selectedMesh.subMeshCount)
                return;

            EditorUtility.DisplayProgressBar("Nanite Meshlet Builder",
                $"Processing submesh {submeshIndex}...", 0.5f);

            try
            {
                // Get indices for the submesh
                var sourceIndices = selectedMesh.GetIndices(submeshIndex);
                var indices = new uint[sourceIndices.Length];
                for (var i = 0; i < sourceIndices.Length; i++)
                {
                    indices[i] = (uint)sourceIndices[i];
                }

                // Process the mesh using the Nanite plugin
                var meshletData = NanitePlugin.ProcessMesh(indices, selectedMesh.vertices);

                // Store the processed data
                submeshDataMap[submeshIndex] = meshletData;

                Debug.Log($"Generated {meshletData.meshlets.Length} meshlets for submesh {submeshIndex}");
                cacheDirty = true;
            }
            catch (Exception e)
            {
                Debug.LogError($"Failed to generate meshlets: {e.Message}");
                EditorUtility.DisplayDialog("Error", $"Failed to generate meshlets: {e.Message}", "OK");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private void CreatePreviewObject()
        {
            DestroyPreviewObject();

            if (selectedMesh == null)
                return;

            previewObject = new GameObject("Nanite Preview");
            previewObject.hideFlags = HideFlags.HideAndDontSave;

            MeshFilter meshFilter = previewObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = selectedMesh;

            MeshRenderer meshRenderer = previewObject.AddComponent<MeshRenderer>();
            meshRenderer.sharedMaterial = new Material(Shader.Find("Standard"));
        }

        private void DestroyPreviewObject()
        {
            if (previewObject != null)
            {
                DestroyImmediate(previewObject);
                previewObject = null;
            }
        }

        private void ExportMeshletsAsAsset()
        {
            if (selectedMesh == null || submeshDataMap.Count == 0)
                return;

            string path = EditorUtility.SaveFilePanelInProject(
                "Save Meshlet Asset",
                $"{selectedMesh.name}_meshlets",
                "asset",
                "Save the generated meshlets as a Unity asset");

            if (string.IsNullOrEmpty(path))
                return;

            // Create a serializable asset
            NaniteMeshletAsset asset = ScriptableObject.CreateInstance<NaniteMeshletAsset>();
            asset.originMeshName = selectedMesh.name;
            asset.submeshData = new List<MeshletsContext>();

            foreach (var kvp in submeshDataMap)
            {
                asset.submeshData.Add(kvp.Value);
            }

            // Save the asset
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.FocusProjectWindow();
            Selection.activeObject = asset;

            EditorUtility.DisplayDialog("Success", "Meshlet data exported successfully!", "OK");
        }

        private void OnSceneGUI(SceneView sceneView)
        {
            if (!showMeshlets || selectedMesh == null || previewObject == null)
                return;

            try
            {
                // 初始化材质
                InitializeMaterials();

                // 确保变换矩阵更新
                Matrix4x4 matrix = previewObject.transform.localToWorldMatrix;

                // 获取相机位置进行距离检测
                Vector3 cameraPosition = sceneView.camera.transform.position;
                Vector3 objectPosition = previewObject.transform.position;
                float distance = Vector3.Distance(cameraPosition, objectPosition);

                // 超出距离则不绘制
                if (distance > meshletDrawDistance)
                    return;

                // 生成/更新缓存
                GenerateMeshletRenderData(selectedSubmeshIndex);

                if (!meshletRenderCache.TryGetValue(selectedSubmeshIndex, out List<MeshletRenderData> renderList) ||
                    renderList == null || renderList.Count == 0)
                    return;

                // 绘制所有meshlets
                DrawMeshlets(renderList, matrix, sceneView.camera);
            }
            catch (Exception ex)
            {
                Debug.LogError($"OnSceneGUI错误: {ex.Message}\n{ex.StackTrace}");
            }
        }

// 添加预定义配色表
        private readonly Color[] predefinedColors = new Color[]
        {
            new Color(0.121f, 0.466f, 0.705f, 0.8f), // 蓝色
            new Color(0.172f, 0.627f, 0.172f, 0.8f), // 绿色
            new Color(0.839f, 0.152f, 0.156f, 0.8f), // 红色
            new Color(0.580f, 0.403f, 0.741f, 0.8f), // 紫色
            new Color(0.549f, 0.337f, 0.294f, 0.8f), // 棕色
            new Color(0.890f, 0.466f, 0.760f, 0.8f), // 粉色
            new Color(0.737f, 0.741f, 0.133f, 0.8f), // 黄绿色
            new Color(0.090f, 0.745f, 0.811f, 0.8f), // 青色
            new Color(0.682f, 0.780f, 0.909f, 0.8f), // 浅蓝
            new Color(0.980f, 0.705f, 0.180f, 0.8f), // 橙色
            new Color(0.498f, 0.498f, 0.498f, 0.8f), // 灰色
            new Color(0.737f, 0.502f, 0.741f, 0.8f), // 紫红
            new Color(0.231f, 0.690f, 0.298f, 0.8f), // 嫩绿
            new Color(0.337f, 0.707f, 0.913f, 0.8f), // 天蓝
            new Color(0.705f, 0.015f, 0.149f, 0.8f), // 深红
            new Color(0.086f, 0.403f, 0.023f, 0.8f), // 深绿
            new Color(0.203f, 0.227f, 0.576f, 0.8f), // 深蓝
            new Color(0.858f, 0.858f, 0.439f, 0.8f), // 黄色
            new Color(0.992f, 0.749f, 0.435f, 0.8f), // 杏色
            new Color(0.662f, 0.811f, 0.133f, 0.8f) // 亮绿色
        };

// 使用预定义颜色表的颜色获取方法
        private Color GetMeshletColor(int meshletIndex)
        {
            // 使用预定义颜色但添加一些变化
            Color baseColor = predefinedColors[meshletIndex % predefinedColors.Length];

            // 为基础颜色添加微妙变化，使相同类型的颜色也有区别
            float hueShift = (meshletIndex / predefinedColors.Length) * 0.1f;
            float lightnessAdjust = ((meshletIndex / predefinedColors.Length) % 3 - 1) * 0.1f;

            // 转换为HSV添加变化再转回
            Color.RGBToHSV(baseColor, out float h, out float s, out float v);
            h = (h + hueShift) % 1.0f;
            v = Mathf.Clamp01(v + lightnessAdjust);

            return Color.HSVToRGB(h, s, v);
        }

        private void DrawMeshlets(List<MeshletRenderData> renderDataList, Matrix4x4 parentMatrix, Camera camera)
        {
            // 1. 准备实例化绘制参数(若使用)
            if (useGPUInstancing && renderDataList.Count > 1)
            {
                // 按网格形状分组
                Dictionary<Mesh, List<MeshletRenderData>> meshGroups = new Dictionary<Mesh, List<MeshletRenderData>>();

                foreach (var renderData in renderDataList)
                {
                    if (!meshGroups.ContainsKey(renderData.mesh))
                        meshGroups[renderData.mesh] = new List<MeshletRenderData>();

                    meshGroups[renderData.mesh].Add(renderData);
                }

                // 为每组绘制实例
                foreach (var group in meshGroups)
                {
                    Mesh sharedMesh = group.Key;
                    var instances = group.Value;

                    // 准备变换矩阵和属性块
                    Matrix4x4[] matrices = new Matrix4x4[instances.Count];
                    MaterialPropertyBlock propertyBlock = new MaterialPropertyBlock();
                    Color[] colors = new Color[instances.Count];

                    for (int i = 0; i < instances.Count; i++)
                    {
                        matrices[i] = parentMatrix * instances[i].localTransform;
                        colors[i] = instances[i].color;
                    }

                    propertyBlock.SetColor("_Color", new Color(1, 1, 1, 0.7f));

                    // 绘制线框
                    if (showWireframe)
                    {
                        meshletWireframeMaterial.SetPass(0);
                        Graphics.DrawMeshInstanced(sharedMesh, 0, meshletWireframeMaterial,
                            matrices, matrices.Length, propertyBlock);
                    }

                    // 绘制面
                    if (!showWireframe || Event.current.alt)
                    {
                        meshletSolidMaterial.SetFloat("_Mode", 3); // 透明模式
                        meshletSolidMaterial.SetColor("_Color", new Color(1, 1, 1, 0.2f));
                        Graphics.DrawMeshInstanced(sharedMesh, 0, meshletSolidMaterial,
                            matrices, matrices.Length, propertyBlock);
                    }
                }

                // 单独绘制法线
                if (showVertexNormals)
                {
                    foreach (var renderData in renderDataList)
                    {
                        if (renderData.normalsMesh != null)
                        {
                            Graphics.DrawMesh(renderData.normalsMesh, parentMatrix * renderData.localTransform,
                                meshletWireframeMaterial, 0, camera);
                        }
                    }
                }
            }
            else
            {
                // 不使用实例化，逐个绘制
                foreach (var renderData in renderDataList)
                {
                    if (renderData.mesh == null)
                        continue;

                    // 设置颜色
                    MaterialPropertyBlock props = new MaterialPropertyBlock();
                    props.SetColor("_Color",
                        new Color(renderData.color.r, renderData.color.g, renderData.color.b, 0.7f));

                    Matrix4x4 meshMatrix = parentMatrix * renderData.localTransform;

                    // 绘制线框
                    if (showWireframe)
                    {
                        Graphics.DrawMesh(renderData.mesh, meshMatrix, meshletWireframeMaterial, 0, camera, 0, props);
                    }

                    // 绘制面
                    if (!showWireframe || Event.current.alt)
                    {
                        Color faceColor = renderData.color;
                        faceColor.a = 0.2f;
                        props.SetColor("_Color", faceColor);
                        Graphics.DrawMesh(renderData.mesh, meshMatrix, meshletSolidMaterial, 0, camera, 0, props);
                    }

                    // 绘制法线
                    if (showVertexNormals && renderData.normalsMesh != null)
                    {
                        Graphics.DrawMesh(renderData.normalsMesh, meshMatrix, meshletWireframeMaterial, 0, camera);
                    }
                }
            }
        }


        private void OnEnable()
        {
            SceneView.duringSceneGui += OnSceneGUI;
            InitializeMaterials();
        }

        private void OnDisable()
        {
            SceneView.duringSceneGui -= OnSceneGUI;
            CleanupMaterials();
            DestroyPreviewObject();
        }

        private void OnDestroy()
        {
            CleanupMaterials();
        }


        private void DebugMeshletData()
        {
            if (!submeshDataMap.TryGetValue(selectedSubmeshIndex, out MeshletsContext data))
            {
                Debug.LogWarning("无可用meshlet数据");
                return;
            }

            Debug.Log($"网格: {selectedMesh.name}, 子网格: {selectedSubmeshIndex}");
            Debug.Log(
                $"Meshlets: {data.meshlets.Length}, Indices: {data.vertices.Length}, Primitives: {data.triangles.Length}");

            StringBuilder meshletInfo = new StringBuilder();
            // 显示前几个meshlet的详细信息
            for (int i = 0; i < data.meshlets.Length; i++)
            {
                var meshlet = data.meshlets[i];
                meshletInfo.AppendLine($"Meshlet {i}: " +
                                       $"IndicesOffset={meshlet.VertexOffset}, " +
                                       $"PrimitivesOffset={meshlet.TriangleOffset}, " +
                                       $"IndicesCount={meshlet.VertexCount}, " +
                                       $"PrimitivesCount={meshlet.TriangleCount}");
            }

            Debug.Log(meshletInfo);
        }
    }

    // Asset class to store meshlet data
    public class NaniteMeshletAsset : ScriptableObject
    {
        public string originMeshName;
        public List<MeshletsContext> submeshData = new List<MeshletsContext>();
    }
}