////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace SDG.Unturned
{
	public delegate void LoadedAssetDeferredCallback<T>(T asset);

	/// <summary>
	/// Struct interface so that for transient asset bundles (older workshop mods) they can be preloaded
	/// and retrieved as-needed, but for master bundles the asset loading can be deferred until needed.
	/// </summary>
	public interface IDeferredAsset<T> where T : Object
	{
		T getOrLoad();
	}

	/// <summary>
	/// Legacy implementation that preloads assets.
	/// </summary>
	public struct NonDeferredAsset<T> : IDeferredAsset<T> where T : Object
	{
		public T loadedObject;
		public T getOrLoad() { return loadedObject; }

		public NonDeferredAsset(T loadedObject)
		{
			this.loadedObject = loadedObject;
		}
	}

	public class Bundle
	{
		private static List<AudioSource> audioSources = new List<AudioSource>();
		private static List<Renderer> renderers = new List<Renderer>();

		public AssetBundle asset
		{
			get;
			protected set;
		}

		public string resource
		{
			get;
			protected set;
		}

		public string name
		{
			get;
			protected set;
		}

		[System.Obsolete]
		public bool hasResource => asset == null;

		public bool convertShadersToStandard;
		public bool consolidateShaders = true;

		protected virtual bool willBeUnloadedDuringUse => true;

		protected void fixupMaterialForRenderer(Transform rootTransform, Renderer renderer, Material sharedMaterial)
		{
			Shader sharedShader = sharedMaterial.shader;

			if (convertShadersToStandard || sharedShader == null)
			{
				sharedMaterial.shader = Shader.Find("Standard");
			}
			else if (consolidateShaders)
			{
				Shader consolidatedShader = ShaderConsolidator.findConsolidatedShader(sharedShader);
				if (consolidatedShader != null)
				{
					sharedMaterial.shader = consolidatedShader;
				}
				else
				{
					Transform child = renderer.transform;
					string pathToChild = child.name;
					while (child != rootTransform)
					{
						child = child.parent;
						pathToChild = child.name + "/" + pathToChild;
					}

					UnturnedLog.warn("Unable to find consolidated version of shader {0} for material {1} in {2} {3}", sharedShader.name, sharedMaterial.name, name, pathToChild);
				}
			}
			else
			{
				UnturnedLog.error("fixupMaterialForRenderer should not have been called for {0}", name);
			}

			StandardShaderUtils.maybeFixupMaterial(sharedMaterial);
		}

#if !DEDICATED_SERVER
		internal static void FixupGameObjectAudio(GameObject gameObject)
		{
			if (!Dedicator.IsDedicatedServer)
			{
				audioSources.Clear();
				gameObject.GetComponentsInChildren(true, audioSources);
				foreach (AudioSource audioSource in audioSources)
				{
					if (audioSource.outputAudioMixerGroup == null)
					{
						audioSource.outputAudioMixerGroup = UnturnedAudioMixer.GetDefaultGroup();
					}
					if (audioSource.dopplerLevel > 0.0001f)
					{
						if (audioSource.GetComponent<EnableDopplerEffect>() == null)
						{
							audioSource.dopplerLevel = 0.0f;
						}
					}
				}
			}
		}
#endif // !DEDICATED_SERVER

		protected virtual void processLoadedGameObject(ref GameObject gameObject)
		{
			if (!StaticUnityEventPrevention.Validate(gameObject))
			{
				// Prevent load caller from instantiating gameObject.
				// Helps track down the offending asset, as it should log a missing game object error.
				gameObject = null;
				return;
			}

#if !DEDICATED_SERVER
			if (!Dedicator.IsDedicatedServer)
			{
				FixupGameObjectAudio(gameObject);
			}
#endif // !DEDICATED_SERVER

			if (!convertShadersToStandard && !consolidateShaders)
				return;

			if (Dedicator.IsDedicatedServer)
				return; // No need for proper shaders.

			renderers.Clear();
			gameObject.GetComponentsInChildren(true, renderers);

			foreach (Renderer renderer in renderers)
			{
				foreach (Material sharedMaterial in renderer.sharedMaterials)
				{
					if (sharedMaterial == null)
						continue;

					fixupMaterialForRenderer(gameObject.transform, renderer, sharedMaterial);
				}
			}
		}

		protected virtual void processLoadedMaterial(Material material)
		{
			if (!convertShadersToStandard && !consolidateShaders)
				return;

			Shader originalShader = material.shader;
			if (convertShadersToStandard || originalShader == null)
			{
				material.shader = Shader.Find("Standard");
			}
			else if (consolidateShaders)
			{
				Shader consolidatedShader = ShaderConsolidator.findConsolidatedShader(originalShader);
				if (consolidatedShader != null)
				{
					material.shader = consolidatedShader;
				}
				else
				{
					UnturnedLog.warn("Unable to find consolidated version of shader {0} for material {1} in {2}", originalShader.name, material.name, name);
				}
			}

			StandardShaderUtils.maybeFixupMaterial(material);
		}

		protected virtual void processLoadedObject<T>(ref T loadedObject) where T : Object
		{
			if (typeof(T) == typeof(GameObject))
			{
				GameObject loadedGameObject = loadedObject as GameObject;
				processLoadedGameObject(ref loadedGameObject);
				loadedObject = loadedGameObject as T;
			}
			else if (typeof(T) == typeof(AudioClip))
			{
				if (willBeUnloadedDuringUse && Dedicator.IsDedicatedServer == false)
				{
					AudioClip clip = loadedObject as AudioClip;
					if (clip && !clip.preloadAudioData)
					{
						// Load audio data, otherwise unavailable after unload.
						clip.LoadAudioData();
					}
				}
			}
			else if (typeof(T) == typeof(Material))
			{
				// e.g. skins

				if (Dedicator.IsDedicatedServer)
					return; // No need for proper materials.

				processLoadedMaterial(loadedObject as Material);
			}
		}

		/// <summary>
		/// Save a reference to an object in the asset bundle, but defer loading it until requested by game code.
		/// </summary>
		public virtual void loadDeferred<T>(string name, out IDeferredAsset<T> asset, LoadedAssetDeferredCallback<T> callback = null) where T : Object
		{
			T loadedObject = load<T>(name);

			NonDeferredAsset<T> newAsset;
			newAsset.loadedObject = loadedObject;
			asset = newAsset;

			callback?.Invoke(loadedObject);
		}

		public virtual T load<T>(string name) where T : Object
		{
			if (asset == null)
			{
				return Resources.Load<T>(resource + "/" + name);
			}

			if (asset.Contains(name))
			{
				T file = asset.LoadAsset<T>(name);
				processLoadedObject(ref file);

				return file;
			}
			else
			{
				return null;
			}
		}

		public virtual string WhereLoadLookedToString(string name)
		{
			if (asset == null)
			{
				return $"{resource}/{name} in built-in resources";
			}
			else
			{
				return $"{name} in .unity3d asset bundle";
			}
		}

		public T[] loadAll<T>() where T : Object
		{
			return asset != null ? asset.LoadAllAssets<T>() : null;
		}

		public void unload()
		{
			if (asset != null)
			{
				asset.Unload(false);
			}
		}

		protected Bundle(string name) // MasterBundle
		{
			asset = null;
			resource = null;
			this.name = name;
		}

		public Bundle(string path, bool usePath, string nameOverride = null)
		{
			if (ReadWrite.fileExists(path, false, usePath)) // bundled object
			{
#if UNITY_STANDALONE_OSX
				string macPath = path.Replace(".unity3d", "_Mac.unity3d");
				if(ReadWrite.fileExists(macPath, false, usePath))
				{
					asset = AssetBundle.LoadFromFile(usePath ? ReadWrite.PATH + macPath : macPath);
				}
#endif

#if UNITY_STANDALONE_LINUX
				string linuxPath = path.Replace(".unity3d", "_Linux.unity3d");
				if(ReadWrite.fileExists(linuxPath, false, usePath))
				{
					asset = AssetBundle.LoadFromFile(usePath ? ReadWrite.PATH + linuxPath : linuxPath);
				}
#endif

				if (asset == null)
				{
					asset = AssetBundle.LoadFromFile(usePath ? ReadWrite.PATH + path : path);
				}
			}
			else // resource object
			{
				asset = null;
			}

			name = nameOverride != null ? nameOverride : ReadWrite.fileName(path);
			if (asset == null)
			{
				resource = ReadWrite.folderPath(path).Substring(1);
			}
		}

		[System.Obsolete]
		public Bundle()
		{
			asset = null;

			name = "#NAME";
		}

		[System.Obsolete]
		public Object[] load()
		{
			if (asset != null)
			{
				return asset.LoadAllAssets();
			}
			else
			{
				return null;
			}
		}

		[System.Obsolete]
		public Object[] load(System.Type type)
		{
			if (asset != null)
			{
				return asset.LoadAllAssets(type);
			}
			else
			{
				return null;
			}
		}
	}
}
