﻿using UnityEngine;

namespace Crest
{
    public class GPUReadbackDisps : GPUReadbackBase<LodDataAnimatedWaves>, ICollProvider
    {
        PerLodData _areaData;

        static GPUReadbackDisps _instance;
        public static GPUReadbackDisps Instance
        {
            get
            {
                return _instance
#if UNITY_EDITOR
                    // Allow hot code edit/recompile in editor - reinit singleton reference.
                    ?? (_instance = FindObjectOfType<GPUReadbackDisps>())
#endif
                    ;
            }
        }

        protected override bool CanUseLastLOD
        {
            get
            {
                // The wave contents from the last LOD can be moved back and forth between the second-to-last LOD and it
                // results in pops if we use it
                return false;
            }
        }

        protected override void Start()
        {
            base.Start();

            if (enabled == false)
            {
                return;
            }

            Debug.Assert(_instance == null);
            _instance = this;

            _settingsProvider = _lodComponents[0].Settings as SimSettingsAnimatedWaves;
        }

        #region ICollProvider
        public bool ComputeUndisplacedPosition(ref Vector3 in__worldPos, out Vector3 undisplacedWorldPos, float minSpatialLength)
        {
            // fpi - guess should converge to location that displaces to the target position
            Vector3 guess = in__worldPos;
            // 2 iterations was enough to get very close when chop = 1, added 2 more which should be
            // sufficient for most applications. for high chop values or really stormy conditions there may
            // be some error here. one could also terminate iteration based on the size of the error, this is
            // worth trying but is left as future work for now.
            Vector3 disp = Vector3.zero;
            for (int i = 0; i < 4 && SampleDisplacement(ref guess, out disp, minSpatialLength); i++)
            {
                Vector3 error = guess + disp - in__worldPos;
                guess.x -= error.x;
                guess.z -= error.z;
            }

            undisplacedWorldPos = guess;
            undisplacedWorldPos.y = OceanRenderer.Instance.SeaLevel;

            return true;
        }

        public bool PrewarmForSamplingArea(Rect areaXZ)
        {
            return PrewarmForSamplingArea(areaXZ, 0f);
        }

        public bool PrewarmForSamplingArea(Rect areaXZ, float minSpatialLength)
        {
            return (_areaData = GetData(areaXZ, minSpatialLength)) != null;
        }

        public bool SampleDisplacement(ref Vector3 in__worldPos, out Vector3 displacement)
        {
            var data = GetData(new Rect(in__worldPos.x, in__worldPos.z, 0f, 0f), 0f);
            if (data == null)
            {
                displacement = Vector3.zero;
                return false;
            }
            return data._resultData.InterpolateARGB16(ref in__worldPos, out displacement);
        }

        public bool SampleDisplacement(ref Vector3 in__worldPos, out Vector3 displacement, float minSpatialLength)
        {
            var data = GetData(new Rect(in__worldPos.x, in__worldPos.z, 0f, 0f), minSpatialLength);
            if (data == null)
            {
                displacement = Vector3.zero;
                return false;
            }
            return data._resultData.InterpolateARGB16(ref in__worldPos, out displacement);
        }

        public bool SampleDisplacementInArea(ref Vector3 in__worldPos, out Vector3 displacement)
        {
            return _areaData._resultData.InterpolateARGB16(ref in__worldPos, out displacement);
        }

        public void SampleDisplacementVel(ref Vector3 in__worldPos, out Vector3 displacement, out bool displacementValid, out Vector3 displacementVel, out bool velValid, float minSpatialLength)
        {
            if (!PrewarmForSamplingArea(new Rect(in__worldPos.x, in__worldPos.z, 0f, 0f), minSpatialLength))
            {
                displacement = Vector3.zero;
                displacementValid = false;
                displacementVel = Vector3.zero;
                velValid = false;
                return;
            }

            SampleDisplacementVelInArea(ref in__worldPos, out displacement, out displacementValid, out displacementVel, out velValid);
        }

        public void SampleDisplacementVelInArea(ref Vector3 in__worldPos, out Vector3 displacement, out bool displacementValid, out Vector3 displacementVel, out bool velValid)
        {
            displacementValid = _areaData._resultData.InterpolateARGB16(ref in__worldPos, out displacement);
            if (!displacementValid)
            {
                displacementVel = Vector3.zero;
                velValid = false;
                return;
            }

            // Check if this lod changed scales between result and previous result - if so can't compute vel. This should
            // probably go search for the results in the other LODs but returning 0 is easiest for now and should be ok-ish
            // for physics code.
            if (_areaData._resultDataPrevFrame._renderData._texelWidth != _areaData._resultData._renderData._texelWidth)
            {
                displacementVel = Vector3.zero;
                velValid = false;
                return;
            }

            Vector3 dispLast;
            velValid = _areaData._resultDataPrevFrame.InterpolateARGB16(ref in__worldPos, out dispLast);
            if (!velValid)
            {
                displacementVel = Vector3.zero;
                return;
            }

            Debug.Assert(_areaData._resultData.Valid && _areaData._resultDataPrevFrame.Valid);
            displacementVel = (displacement - dispLast) / Mathf.Max(0.0001f, _areaData._resultData._time - _areaData._resultDataPrevFrame._time);
        }

        public bool SampleHeight(ref Vector3 in__worldPos, out float height)
        {
            return SampleHeight(ref in__worldPos, out height, 0f);
        }

        public bool SampleHeight(ref Vector3 in__worldPos, out float height, float minSpatialLength)
        {
            var posFlatland = in__worldPos;
            posFlatland.y = OceanRenderer.Instance.transform.position.y;

            var undisplacedPos = GetPositionDisplacedToPosition(ref posFlatland, minSpatialLength);

            var disp = Vector3.zero;
            SampleDisplacement(ref undisplacedPos, out disp, minSpatialLength);

            height = posFlatland.y + disp.y;
            return true;
        }

        /// <summary>
        /// Get position on ocean plane that displaces horizontally to the given position.
        /// </summary>
        public Vector3 GetPositionDisplacedToPosition(ref Vector3 in__displacedWorldPos, float minSpatialLength)
        {
            // fixed point iteration - guess should converge to location that displaces to the target position

            var guess = in__displacedWorldPos;

            // 2 iterations was enough to get very close when chop = 1, added 2 more which should be
            // sufficient for most applications. for high chop values or really stormy conditions there may
            // be some error here. one could also terminate iteration based on the size of the error, this is
            // worth trying but is left as future work for now.
            for (int i = 0; i < 4; i++)
            {
                var disp = Vector3.zero;
                SampleDisplacement(ref guess, out disp, minSpatialLength);
                var error = guess + disp - in__displacedWorldPos;
                guess.x -= error.x;
                guess.z -= error.z;
            }
            guess.y = OceanRenderer.Instance.SeaLevel;
            return guess;
        }

        public bool SampleNormal(ref Vector3 in__undisplacedWorldPos, out Vector3 normal)
        {
            return SampleNormal(ref in__undisplacedWorldPos, out normal, 0f);
        }

        public bool SampleNormal(ref Vector3 in__undisplacedWorldPos, out Vector3 normal, float minSpatialLength)
        {
            // select lod. this now has a 1 texel buffer, so the finite differences below should all be valid.
            if (!PrewarmForSamplingArea(new Rect(in__undisplacedWorldPos.x, in__undisplacedWorldPos.z, 0f, 0f), minSpatialLength))
            {
                normal = Vector3.zero;
                return false;
            }

            return SampleNormalInArea(ref in__undisplacedWorldPos, out normal);
        }

        public bool SampleNormalInArea(ref Vector3 in__undisplacedWorldPos, out Vector3 normal)
        {
            float gridSize = _areaData._resultData._renderData._texelWidth;
            normal = Vector3.zero;
            Vector3 dispCenter = Vector3.zero;
            if (!SampleDisplacementInArea(ref in__undisplacedWorldPos, out dispCenter)) return false;
            Vector3 undisplacedWorldPosX = in__undisplacedWorldPos + Vector3.right * gridSize;
            Vector3 dispX = Vector3.zero;
            if (!SampleDisplacementInArea(ref undisplacedWorldPosX, out dispX)) return false;
            Vector3 undisplacedWorldPosZ = in__undisplacedWorldPos + Vector3.forward * gridSize;
            Vector3 dispZ = Vector3.zero;
            if (!SampleDisplacementInArea(ref undisplacedWorldPosZ, out dispZ)) return false;

            normal = Vector3.Cross(dispZ + Vector3.forward * gridSize - dispCenter, dispX + Vector3.right * gridSize - dispCenter).normalized;

            return true;
        }
        #endregion
    }
}
