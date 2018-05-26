﻿using UnityEngine;

namespace Crest
{
    /// <summary>
    /// Renders terrain height / ocean depth once into a render target to cache this off and avoid rendering it every frame.
    /// This should be used for static geometry, dynamic objects should be tagged with the Render Ocean Depth component.
    /// </summary>
    public class OceanDepthCache : MonoBehaviour
    {
        public bool _populateOnStartup = true;
        public LayerMask _mask;
        public int _resolution = 512;

        // a big hill will still want to write its height into the depth texture
        public float _cameraMaxTerrainHeight = 100f;

        public bool _forceAlwaysUpdateDebug = false;

        RenderTexture _cache;
        GameObject _drawCacheQuad;
        Camera _camDepthCache;

        void Start()
        {
            if (_populateOnStartup)
            {
                PopulateCache();
            }
        }

#if UNITY_EDITOR
        void Update()
        {
            if (_forceAlwaysUpdateDebug)
            {
                PopulateCache();
            }
        }
#endif

        public void PopulateCache()
        {
            if (_cache == null)
            {
                _cache = new RenderTexture(_resolution, _resolution, 0);
                _cache.name = gameObject.name + "_oceanDepth";
                _cache.format = RenderTextureFormat.RHalf;
                _cache.useMipMap = false;
                _cache.anisoLevel = 0;
            }

            if (_drawCacheQuad == null)
            {
                _drawCacheQuad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(_drawCacheQuad.GetComponent<Collider>());
                _drawCacheQuad.name = "Draw_" + _cache.name;
                _drawCacheQuad.transform.SetParent(transform, false);
                _drawCacheQuad.transform.localEulerAngles = 90f * Vector3.right;
                _drawCacheQuad.AddComponent<RenderOceanDepth>();
                var qr = _drawCacheQuad.GetComponent<Renderer>();
                qr.material = new Material(Shader.Find("Ocean/Ocean Depth Cache"));
                qr.material.mainTexture = _cache;
                qr.enabled = false;
            }

            if (_camDepthCache == null)
            {
                _camDepthCache = new GameObject("DepthCacheCam").AddComponent<Camera>();
                _camDepthCache.transform.position = transform.position + Vector3.up * _cameraMaxTerrainHeight;
                _camDepthCache.transform.parent = transform;
                _camDepthCache.transform.localEulerAngles = 90f * Vector3.right;
                _camDepthCache.orthographic = true;
                _camDepthCache.orthographicSize = Mathf.Max(transform.lossyScale.x / 2f, transform.lossyScale.z / 2f);
                _camDepthCache.targetTexture = _cache;
                _camDepthCache.cullingMask = _mask;
                _camDepthCache.clearFlags = CameraClearFlags.SolidColor;
                _camDepthCache.backgroundColor = Color.red * 10000f;
                _camDepthCache.enabled = false;
                _camDepthCache.allowMSAA = false;
                // I'd prefer to destroy the cam object, but I found sometimes (on first start of editor) it will fail to render.
                _camDepthCache.gameObject.SetActive(false);
            }

            // Hackety-hack: this seems to be the only way to pass parameters to the shader when using RenderWithShader!
            Shader.SetGlobalFloat("_SeaLevel", OceanRenderer.Instance.SeaLevel);
            _camDepthCache.RenderWithShader(Shader.Find("Ocean/Ocean Depth"), null);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(1f, 0f, 1f));
        }
    }
}