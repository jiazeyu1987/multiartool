using UnityEngine;
using System;
using System.Collections;
using IRToolTrack;
using System.Linq;
using System.Runtime.InteropServices;
using static System.Net.Mime.MediaTypeNames;

#if ENABLE_WINMD_SUPPORT
using System.Threading.Tasks;
using HL2IRToolTracking;
using Windows.Perception.Spatial;
using Microsoft.MixedReality.OpenXR;
#endif

public class IRToolTracking : MonoBehaviour
{
#if ENABLE_WINMD_SUPPORT
    private HL2IRTracking toolTracking;
#endif

    private bool startToolTracking = false;
    private IRToolController[] tools = null;

    public float[] GetToolTransform(string identifier)
    {
        var toolTransform = Enumerable.Repeat<float>(0, 8).ToArray();
#if ENABLE_WINMD_SUPPORT
        toolTransform = toolTracking.GetToolTransform(identifier);
#endif
        return toolTransform;
    }

    public Int64 GetTimestamp()
    {
#if ENABLE_WINMD_SUPPORT
        return toolTracking.GetTrackingTimestamp();
#else
        return 0;
#endif
    }

    public void Start()
    {
        Debug.Log(" IRToolTracking Start() called");
        tools = FindObjectsOfType<IRToolController>();
        Debug.Log($"sssssssssssssssssssss {tools.Length}");
        StartCoroutine(DelayedStartTracking());
    }

    private IEnumerator DelayedStartTracking()
    {
        Debug.Log("⏳ Waiting for Spatial Coordinate System initialization...");
        yield return new WaitForSeconds(1.5f); // 给空间系统初始化时间
        StartToolTracking();
    }

    public void StartToolTracking()
    {
        Debug.Log("▶️ Start Tracking");
#if ENABLE_WINMD_SUPPORT
        if (!startToolTracking)
        {
            if (toolTracking == null)
            {
                toolTracking = new HL2IRTracking();
            }

            SetReferenceWorldCoordinateSystem();

            if (toolTracking == null)
            {
                Debug.LogError("❌ HL2IRTracking is null after initialization.");
                return;
            }

            toolTracking.RemoveAllToolDefinitions();

            foreach (IRToolController tool in tools)
            {
                Debug.Log($"[Tool Init] Identifier: {tool.identifier}");
                var positions = tool.sphere_positions;
                for (int i = 0; i < positions.Length; i += 3)
                {
                    Debug.Log($"  Sphere {i / 3}: ({positions[i]}, {positions[i + 1]}, {positions[i + 2]})");
                }

                int min_visible_spheres = tool.sphere_count;
                if (tool.max_occluded_spheres > 0 && (tool.sphere_count - tool.max_occluded_spheres) >= 3)
                {
                    min_visible_spheres = tool.sphere_count - tool.max_occluded_spheres;
                }

                toolTracking.AddToolDefinition(
                    tool.sphere_count,
                    tool.sphere_positions,
                    tool.sphere_radius,
                    tool.identifier,
                    min_visible_spheres,
                    tool.lowpass_factor_rotation,
                    tool.lowpass_factor_position
                );

                tool.StartTracking();
            }

            toolTracking.StartToolTracking();
            startToolTracking = true;
            Debug.Log("✅ Tool tracking started successfully.");
        }
#endif
    }

    public void StopToolTracking()
    {
        if (!startToolTracking)
        {
            Debug.Log("⏹ Tracking was not started, so cannot stop it");
            return;
        }

#if ENABLE_WINMD_SUPPORT
        var success = toolTracking.StopToolTracking();
        if (!success)
        {
            Debug.LogError("❌ Could not stop tracking.");
        }
        startToolTracking = false;

        foreach (IRToolController tool in tools)
        {
            tool.StopTracking();
        }
#endif
        Debug.Log("🛑 Stopped Tracking");
    }

    private void SetReferenceWorldCoordinateSystem()
    {
#if ENABLE_WINMD_SUPPORT
        Debug.Log("🔄 Setting World Coordinate System...");
        SpatialCoordinateSystem unityWorldOrigin = null;

#if UNITY_2021_2_OR_NEWER
        unityWorldOrigin = PerceptionInterop.GetSceneCoordinateSystem(UnityEngine.Pose.identity) as SpatialCoordinateSystem;
#elif UNITY_2020_1_OR_NEWER
        IntPtr worldOriginPtr = UnityEngine.XR.WindowsMR.WindowsMREnvironment.OriginSpatialCoordinateSystem;
        unityWorldOrigin = Marshal.GetObjectForIUnknown(worldOriginPtr) as SpatialCoordinateSystem;
#else
        IntPtr worldOriginPtr = UnityEngine.XR.WSA.WorldManager.GetNativeISpatialCoordinateSystemPtr();
        unityWorldOrigin = Marshal.GetObjectForIUnknown(worldOriginPtr) as SpatialCoordinateSystem;
#endif

        if (unityWorldOrigin == null)
        {
            Debug.LogError("❌ Failed to get Unity SpatialCoordinateSystem. Trans To World is NULL.");
            return;
        }

        toolTracking.SetReferenceCoordinateSystem(unityWorldOrigin);
        Debug.Log("✅ SpatialCoordinateSystem set successfully.");
#endif
    }

    public void ExitApplication()
    {
        StopToolTracking();
        UnityEngine.Application.Quit();
    }
}
