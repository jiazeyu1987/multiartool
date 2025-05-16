using Microsoft.MixedReality.Toolkit;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace IRToolTrack
{
    public class IRToolController : MonoBehaviour
    {
        public string identifier;
        public GameObject[] spheres;
        public bool disableUntilDetection = false;
        public bool disableWhenTrackingLost = false;
        public float secondsLostUntilDisable = 3;
        public float sphere_radius = 7.3f;
        public int max_occluded_spheres = 0;
        public float lowpass_factor_rotation = 0.3f;
        public float lowpass_factor_position = 0.6f;

        private bool childrenActive = true;

        private IRToolTracking irToolTracking;
        private Int64 lastUpdate = 0;
        private float lastSpotted = 0;
        private Vector3 targetPosition = Vector3.zero;
        private Quaternion targetRotation = Quaternion.identity;
        private bool[] childAtIndexActive;
        private float trackingStableDuration = 0.5f; // 需要连续多少秒才算稳定变化
        private float trackingStateLastChangeTime = 0f;
        private bool _stableTrackingState = false; // 过滤后的稳定状态

        public bool StableTracking => _stableTrackingState;
        // 🔹 当前是否正在被追踪（识别出位姿）
        public bool isTracking { get; private set; } = false;

        public int sphere_count => spheres.Length;

        public float[] sphere_positions
        {
            get
            {
                float[] coordinates = new float[sphere_count * 3];
                int cur_coord = 0;
                for (int i = 0; i < sphere_count; i++)
                {
                    coordinates[cur_coord++] = spheres[i].transform.localPosition.x;
                    coordinates[cur_coord++] = spheres[i].transform.localPosition.y;
                    coordinates[cur_coord++] = spheres[i].transform.localPosition.z;
                }
                return coordinates;
            }
        }

        void Start()
        {
            childAtIndexActive = new bool[transform.childCount];
            irToolTracking = FindObjectOfType<IRToolTracking>();

#if !UNITY_EDITOR
            if (disableUntilDetection)
            {
                for (int i = 0; i < transform.childCount; i++)
                {
                    var curChild = transform.GetChild(i).gameObject;
                    if (curChild.activeSelf)
                    {
                        childAtIndexActive[i] = true;
                        curChild.SetActive(false);
                    }
                }
                childrenActive = false;
            }
#endif
        }

        public enum Status
        {
            Inactive,
            Active
        }
        private Status _subStatus = Status.Inactive;

        public void StartTracking()
        {
            if (_subStatus == Status.Active)
            {
                Debug.Log("Tool tracking already started.");
                return;
            }
            Debug.Log("Started tracking " + identifier);
            _subStatus = Status.Active;
        }

        public void StopTracking()
        {
            if (_subStatus == Status.Inactive)
            {
                Debug.Log("Tracking of " + identifier + " already stopped.");
                return;
            }
            Debug.Log("Stopped tracking " + identifier);
            _subStatus = Status.Inactive;
        }

        void Update()
        {
            if (_subStatus == Status.Inactive)
                return;

            Int64 trackingTimestamp = irToolTracking.GetTimestamp();
            float[] tool_transform = irToolTracking.GetToolTransform(identifier);

            bool validTracking = tool_transform != null &&
                                 !float.IsNaN(tool_transform[0]) &&
                                 tool_transform.Length >= 8 &&
                                 tool_transform[7] != 0 &&
                                 lastUpdate < trackingTimestamp;

            // 原始 tracking 状态赋值
            isTracking = validTracking;

            // 稳定 tracking 状态滤波逻辑
            if (isTracking != _stableTrackingState)
            {
                if (Time.time - trackingStateLastChangeTime >= trackingStableDuration)
                {
                    _stableTrackingState = isTracking;
                    trackingStateLastChangeTime = Time.time;
                }
            }
            else
            {
                trackingStateLastChangeTime = Time.time;
            }

            // 若 tracking 有效，更新目标位姿 + 激活可见子物体
            if (validTracking)
            {
                if (!childrenActive)
                {
                    for (int i = 0; i < transform.childCount; i++)
                    {
                        var curChild = transform.GetChild(i).gameObject;
                        if (childAtIndexActive[i])
                        {
                            curChild.SetActive(true);
                        }
                    }
                    childrenActive = true;
                }

                targetRotation = new Quaternion(tool_transform[3], tool_transform[4], tool_transform[5], tool_transform[6]);
                targetPosition = new Vector3(tool_transform[0], tool_transform[1], tool_transform[2]);
                lastSpotted = Time.time;
            }
            else
            {
                if (childrenActive && disableWhenTrackingLost && Time.time - lastSpotted > secondsLostUntilDisable)
                {
                    for (int i = 0; i < transform.childCount; i++)
                    {
                        transform.GetChild(i).gameObject.SetActive(false);
                    }
                    childrenActive = false;
                }
            }

            transform.position = targetPosition;
            transform.rotation = targetRotation;
            lastUpdate = trackingTimestamp;
        }
    }
}
