using System;
using UnityEngine;

namespace Svnvav.SRP2018
{
    public class InstancedColor : MonoBehaviour
    {
        private static MaterialPropertyBlock _propertyBlock;
        private static int _colorID = Shader.PropertyToID("_Color");
        
        [SerializeField] private Color _color;
        
        private void Awake () {
            OnValidate();
        }

        private void OnValidate()
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            _propertyBlock.SetColor(_colorID, _color);
            GetComponent<MeshRenderer>().SetPropertyBlock(_propertyBlock);
        }
    }
}