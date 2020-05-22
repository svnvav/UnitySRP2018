using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace Svnvav.SRP2018
{
    [CreateAssetMenu(menuName = "Rendering/My Pipeline")]
    public class MyPipelineAsset : RenderPipelineAsset
    {
        public enum ShadowMapSize
        {
            _256 = 256,
            _512 = 512,
            _1024 = 1024,
            _2048 = 2048,
            _4096 = 4096
        }

        [SerializeField] private ShadowMapSize _shadowMapSize = ShadowMapSize._1024;
        [SerializeField] bool _dynamicBatching;
        [SerializeField] bool _instancing;
        
        protected override IRenderPipeline InternalCreatePipeline()
        {
            return new MyPipeline(_dynamicBatching, _instancing, (int)_shadowMapSize);
        }
    }
}