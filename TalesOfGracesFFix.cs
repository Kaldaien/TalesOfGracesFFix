﻿using BepInEx;
using BepInEx.Unity.Mono;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

using UnityEngine;
using System;

namespace TalesOfGracesFFix
{
    [BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
    public class ToGFFix : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        // Features
        public static ConfigEntry<bool> bFixAspectRatio;
        public static ConfigEntry<int> iMSAASamples;
        public static ConfigEntry<bool> bDepthOfField;
        public static ConfigEntry<bool> bBloom;

        // Aspect Ratio
        private const float fNativeAspect = (float)16 / 9;
        public static float fAspectRatio;
        public static float fAspectMultiplier;

        private void Awake()
        {
            // Plugin startup logic
            Log = base.Logger;
            Log.LogInfo($"{MyPluginInfo.PLUGIN_GUID} v{MyPluginInfo.PLUGIN_VERSION} is loaded!");

            // Aspect ratio
            bFixAspectRatio = Config.Bind("Ultrawide/Narrower",
                                "Fix Aspect Ratio",
                                true,
                                "Removes pillarboxing/letterboxing and centers the UI at 16:9.");

            // Graphical tweaks
            iMSAASamples = Config.Bind("Graphical Tweaks",
                                "MSAA Samples",
                                1,
                                new ConfigDescription("Set number of MSAA samples. 1 = off. Note that enabling MSAA will disable depth of field and FXAA.",
                                new AcceptableValueList<int>(1, 2, 4, 8)));

            bDepthOfField = Config.Bind("Graphical Tweaks",
                                "Depth Of Field",
                                true,
                                "Enable or disable depth of field.");

            bBloom = Config.Bind("Graphical Tweaks",
                                "Bloom",
                                true,
                                "Enable or disable bloom.");

            // Apply patches
            if (bFixAspectRatio.Value)
                Harmony.CreateAndPatchAll(typeof(AspectRatioPatches));

            if (!bDepthOfField.Value || iMSAASamples.Value >1)
                Harmony.CreateAndPatchAll(typeof(GraphicsPatches));
        }

        [HarmonyPatch]
        public class AspectRatioPatches
        {
            // Calculate aspect ratio
            [HarmonyPatch(typeof(Screen), nameof(Screen.SetResolution), new Type[] { typeof(int), typeof(int), typeof(FullScreenMode) })]
            [HarmonyPostfix]
            public static void CurrentResolution(ref int __0, ref int __1, ref FullScreenMode __2)
            {
                fAspectRatio = (float)__0 / __1;
                fAspectMultiplier = fAspectRatio / fNativeAspect;

                Log.LogInfo($"Current Resolution: {__0}x{__1}");
                Log.LogInfo($"Current Resolution: Aspect Ratio: {fAspectRatio}");
                Log.LogInfo($"Current Resolution: Aspect Multiplier: {fAspectMultiplier}");
            }

            // Camera rect
            [HarmonyPatch(typeof(Noble.CameraManager), nameof(Noble.CameraManager.SetCameraViewportRect))]
            [HarmonyPrefix]
            public static void CameraRect(Noble.CameraManager __instance, ref Rect __0)
            {
                if (fAspectRatio != fNativeAspect)
                {
                    __0 = new Rect(0f, 0f, 1f, 1f);
                }
            }

            // Camera aspect ratio
            [HarmonyPatch(typeof(Noble.CameraManager), nameof(Noble.CameraManager.SetCameraAspect))]
            [HarmonyPrefix]
            public static void CameraAspectRatio(Noble.CameraManager __instance, ref float __0)
            {
                if (fAspectRatio != fNativeAspect)
                {
                    __0 = fAspectRatio;
                }
            }

            // Set UI ortho matrix for 16:9
            [HarmonyPatch(typeof(Noble.PrimitiveManager), nameof(Noble.PrimitiveManager.CalcUIOrthoMatrix))]
            [HarmonyPostfix]
            public static void FixHUD(Noble.PrimitiveManager __instance)
            {
                if (fAspectRatio > fNativeAspect)
                    __instance.m_Viewport = new Rect(0f, 0f, (float)Screen.width / fAspectMultiplier, (float)Screen.height);
                else if (fAspectRatio < fNativeAspect)
                    __instance.m_Viewport = new Rect(0f, 0f, (float)Screen.width, (float)Screen.height * fAspectMultiplier);
            }

            // Fix movies
            [HarmonyPatch(typeof(NobleMovieRendereFeature), nameof(NobleMovieRendereFeature.Create))]
            [HarmonyPostfix]
            public static void FixMovies(NobleMovieRendereFeature __instance)
            {
                // TODO: There's probably a better way of fixing this.
                if (__instance._pass != null)
                {
                    if (fAspectRatio > fNativeAspect)
                        __instance._pass.cameraview.m33 = fAspectMultiplier;
                    else if (fAspectRatio < fNativeAspect)
                        __instance._pass.cameraview.m11 = fAspectMultiplier;
                }
            }       
        }

        [HarmonyPatch]
        public class GraphicsPatches
        {
            // Adjust graphical settings
            [HarmonyPatch(typeof(UnityEngine.Rendering.Volume), nameof(UnityEngine.Rendering.Volume.OnEnable))]
            [HarmonyPostfix]
            public static void GraphicalTweaks(UnityEngine.Rendering.Volume __instance)
            {
                // Get current render pipeline asset
                var urpAsset = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;

                // MSAA
                if (iMSAASamples.Value > 1)
                {
                    urpAsset.msaaSampleCount = iMSAASamples.Value;
                    Log.LogInfo($"Graphical Tweaks: Set MSAA sample count to {iMSAASamples.Value}.");
                }

                // FXAA
                __instance.profile.TryGet(out NoblePostEffectCustomFXAAParam fxaa);
                if (fxaa && iMSAASamples.Value > 1)
                {
                    fxaa.m_fxaaEnable.value = false;
                    Log.LogInfo($"Graphical Tweaks: Disabled FXAA on volume {__instance.gameObject.name}.");
                }

                // Depth of field
                __instance.profile.TryGet(out NoblePostEffectDepthOfFieldParam dof);
                if (dof && (!bDepthOfField.Value || iMSAASamples.Value > 1))
                {
                    dof.active = false;
                    Log.LogInfo($"Graphical Tweaks: Disabled depth of field on volume {__instance.gameObject.name}.");
                }

                // Bloom
                __instance.profile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom);
                if (bloom && !bBloom.Value)
                {
                    bloom.active = false;
                    Log.LogInfo($"Graphical Tweaks: Disabled bloom on volume {__instance.gameObject.name}.");
                }
            }
        }
    }
}