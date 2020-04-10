using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Svnvav.SRP2018
{
    [CreateAssetMenu(menuName = "Rendering/My Pipeline")]
    public class MyPipelineAsset : RenderPipelineAsset
    {
        protected override IRenderPipeline InternalCreatePipeline()
        {
            return new MyPipeline();
        }
    }
}