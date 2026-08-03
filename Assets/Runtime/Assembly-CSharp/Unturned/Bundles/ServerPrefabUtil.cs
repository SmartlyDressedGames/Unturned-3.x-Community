////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
#if UNITY_EDITOR || DEVELOPMENT_BUILD
//#define LOG_SERVER_PREFAB_CLEANUP
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Collections.Generic;
using UnityEngine;
using Unturned.SystemEx;

namespace SDG.Unturned
{
	/// <summary>
	/// Helpers on the dedicated server to optimize client prefabs for server usage.
	/// </summary>
	internal static class ServerPrefabUtil
	{
		/// <summary>
		/// Optimize client prefab for server usage.
		/// </summary>
		public static void RemoveClientComponents(GameObject gameObject, Asset context)
		{
			gameObject.GetComponentsInChildren(true, workingComponents);

			// Filter workingComponents down to only components that *should* be removed.
			workingComponents.RemoveSwap((Component component) =>
			{
				if (component == null)
				{
					// Got an email report with a null reference exception inside this lambda,
					// so remove any null components from the list. Maybe because references
					// to Unity objects become null when Destroyed and it somehow got destroyed
					// earlier?
					return true;
				}

				if (typesToRemove.Contains(component.GetType()))
				{
					// Do *not* remove from list, will be destroyed later.
					return false;
				}

				// Kind of hacky but this function is already being called for every component...
				if (component is Animation animationComponent)
				{
					// Otherwise important animations like door open/close would not play on server. (issue #3362)
					animationComponent.cullingType = AnimationCullingType.AlwaysAnimate;

					if (shouldLogAutoPlayAnimsInServerPrefabs && animationComponent.playAutomatically && animationComponent.clip != null)
					{
						UnturnedLog.info($"AutoPlay {animationComponent.clip.wrapMode} Anim: {context.AssetErrorPrefix} {animationComponent.GetSceneHierarchyPath()}");
					}
				}

				// Yes remove from list so that it is *not* destroyed.
				return true;
			});

			// Sort textmesh components which depend on meshrenderers to the front so that
			// Unity does not complain when we remove them. Same for LODGroupAdditionalData vs LODGroup.
			workingComponents.Sort(CompareRemovedComponents);

#if LOG_SERVER_PREFAB_CLEANUP
			UnturnedLog.info($"{context.FriendlyNameWithFriendlyType} removing {workingComponents.Count} component(s) from {gameObject.name}");
			foreach (Component component in workingComponents)
			{
				UnturnedLog.info($"{component.GetType().Name} at {component.GetSceneHierarchyPath()}");
			}
#endif

			foreach (Component component in workingComponents)
			{
				// Immediate because otherwise Instantiating a just-loaded prefab includes all of the destroyed components.
				// allowDestroyingAssets is required for destroying components without making an instantiated duplicate.
				Object.DestroyImmediate(component, /*allowDestroyingAssets*/ true);
			}

			workingComponents.Clear();
		}

		private static int CompareRemovedComponents(Component lhs, Component rhs)
		{
			if (lhs.GetType() == rhs.GetType())
			{
				// Avoid inconsistent ordering exception if object has multiple components of the
				// same type. (public issue #5525)
				return lhs.GetInstanceID().CompareTo(rhs.GetInstanceID());
			}

			// Nelson 2025-12-04: if adding more dependencies here please make sure the dependency is ALSO in the typesToRemove set. ;)
			return (lhs is TMPro.TextMeshPro || lhs is TextMesh || lhs is LODGroupAdditionalData || lhs is EnableDopplerEffect || lhs is MusicAudioSource) ? -1 : 0;
		}

		private static List<Component> workingComponents = new List<Component>();
		private static HashSet<System.Type> typesToRemove = new HashSet<System.Type>()
		{
			typeof(LODGroup),
			typeof(LODGroupAdditionalData),
			typeof(MeshFilter),
			typeof(Cloth),
			typeof(TextMesh),
			typeof(TMPro.TextMeshPro),
			typeof(TMPro.TextMeshProUGUI),
			typeof(WindZone),
			typeof(LensFlare),
			// 2026-04-13: replacing ParticleSystemRenderer removal with ParticleSystem removal because some maps have a huge number of
			// particle systems on the server. (100k+) This *may* have unintended side effects for mods using particle system collision
			// for gameplay purposes (highly uncommon?), in which case a server-specific prefab can be used (or likely already is).
			typeof(ParticleSystem),
			typeof(Projector),
			typeof(Camera),
			typeof(Skybox),
			typeof(FlareLayer),
			typeof(Light),
			typeof(LightProbeGroup),
			typeof(LightProbeProxyVolume),
			typeof(ReflectionProbe),
			typeof(Tree), // SpeedTree

			// uGUI components which ideally should not even be used in this way,
			// but lots of barricades have them already.
			typeof(CanvasRenderer),
			typeof(UnityEngine.UI.Button),
			typeof(UnityEngine.UI.CanvasScaler),
			typeof(UnityEngine.UI.Dropdown),
			typeof(UnityEngine.UI.Graphic),
			typeof(UnityEngine.UI.GridLayoutGroup),
			typeof(UnityEngine.UI.HorizontalLayoutGroup),
			typeof(UnityEngine.UI.Image),
			typeof(UnityEngine.UI.InputField),
			typeof(UnityEngine.UI.LayoutElement),
			typeof(UnityEngine.UI.LayoutGroup),
			typeof(UnityEngine.UI.Mask),
			typeof(UnityEngine.UI.MaskableGraphic),
			typeof(UnityEngine.UI.RawImage),
			typeof(UnityEngine.UI.RectMask2D),
			typeof(UnityEngine.UI.Scrollbar),
			typeof(UnityEngine.UI.ScrollRect),
			typeof(UnityEngine.UI.Slider),
			typeof(UnityEngine.UI.Text),
			typeof(UnityEngine.UI.Toggle),
			typeof(UnityEngine.UI.ToggleGroup),
			typeof(UnityEngine.UI.VerticalLayoutGroup),

			// Alphabetically sorted list of audio-related components.
			typeof(MusicAudioSource),
			typeof(EnableDopplerEffect),
			typeof(AudioChorusFilter),
			typeof(AudioDistortionFilter),
			typeof(AudioEchoFilter),
			typeof(AudioHighPassFilter),
			typeof(AudioListener),
			typeof(AudioLowPassFilter),
			typeof(AudioReverbFilter),
			typeof(AudioReverbZone),
			typeof(AudioSource),

			// Alphabetically sorted list of renderer subclasses.
			typeof(Renderer),
			typeof(BillboardRenderer),
			typeof(LineRenderer),
			typeof(MeshRenderer),
			//typeof(ParticleSystemRenderer), // See comment about ParticleSystem above.
			typeof(SkinnedMeshRenderer),
			typeof(SpriteMask),
			typeof(SpriteRenderer),
			typeof(TrailRenderer),
		};

		static ServerPrefabUtil()
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			// Sanity-check list is valid.
			foreach (System.Type componentType in typesToRemove)
			{
				if (!typeof(Component).IsAssignableFrom(componentType))
				{
					Debug.LogWarning($"ServerPrefabUtil type \"{componentType}\" is not a component");
				}
			}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
		}

		private static CommandLineFlag shouldLogAutoPlayAnimsInServerPrefabs = new CommandLineFlag(false, "-LogAutoPlayAnimsInServerPrefabs");
	}
}
