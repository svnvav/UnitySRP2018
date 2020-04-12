using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Svnvav.SRP2018
{
    [CreateAssetMenu(menuName = "Rendering/My Pipeline")]
    public class MyPipelineAsset : RenderPipelineAsset
    {
        [SerializeField] bool _dynamicBatching;
        [SerializeField] bool _instancing;
        
        protected override IRenderPipeline InternalCreatePipeline()
        {
            return new MyPipeline(_dynamicBatching, _instancing);
        }
    }
}