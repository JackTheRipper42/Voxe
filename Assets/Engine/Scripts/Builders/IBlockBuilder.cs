using Assets.Engine.Scripts.Core.Blocks;
using Assets.Engine.Scripts.Utils;
using UnityEngine;
using RenderBuffer = Assets.Engine.Scripts.Rendering.RenderBuffer;

namespace Assets.Engine.Scripts.Builders
{
    /// <summary>
    ///     Interface for building static meshes for blocks
    /// </summary>
    public interface IBlockBuilder
    {
        Rect GetTexture(int face);

        void Build(RenderBuffer targetMesh, ref BlockData block, BlockFace face, bool backFace, ref Vector3[] vecs);
    }
}