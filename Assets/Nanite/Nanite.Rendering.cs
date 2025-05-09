using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Nanite
{
    public class MeshletEditor
    {

        public static void BakeFromMesh(Mesh mesh)
        {
            for (var subIndex = 0; subIndex < mesh.subMeshCount; ++subIndex)
            {
                var sourceIndices = mesh.GetIndices(subIndex);
                var indices = new uint[sourceIndices.Length];
                for (var i = 0; i < sourceIndices.Length; i++)
                {
                    indices[i] = (uint)sourceIndices[i];
                }

                NanitePlugin.ProcessMesh(indices, mesh.vertices);
            }
        }
    }
}