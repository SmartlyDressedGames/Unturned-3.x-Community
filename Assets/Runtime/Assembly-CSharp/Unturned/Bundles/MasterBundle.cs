////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	public struct DeferredMasterAsset<T> : IDeferredAsset<T> where T : UnityEngine.Object
	{
		public MasterBundle masterBundle;
		public string name;
		public LoadedAssetDeferredCallback<T> callback;
		public T loadedObject;
		public bool hasLoaded;

		public T getOrLoad()
		{
			if (!hasLoaded)
			{
				hasLoaded = true;
				loadedObject = masterBundle.load<T>(name);
				callback?.Invoke(loadedObject);
			}

			return loadedObject;
		}
	}

	/// <summary>
	/// Remaps asset load requests into a large asset bundle rather than small individual asset bundles.
	/// </summary>
	public class MasterBundle : Bundle
	{
		private static Dictionary<Type, string[]> typeExtensions = new Dictionary<Type, string[]>
		{
			{ typeof(Material), new string[] { ".mat"} },
			{ typeof(Texture2D), new string[] { ".png", ".jpg" } },
			{ typeof(GameObject), new string[] { ".prefab"} },
			{ typeof(AudioClip), new string[] { ".wav", ".ogg", ".mp3" } }
		};

		/// <summary>
		/// Config that contains the actual large AssetBundle.
		/// </summary>
		public MasterBundleConfig cfg
		{
			get;
			protected set;
		}

		/// <summary>
		/// Asset path relative to the master AssetBundle.
		/// </summary>
		public string relativePath
		{
			get;
			protected set;
		}

		protected override bool willBeUnloadedDuringUse => false;

		public override void loadDeferred<T>(string name, out IDeferredAsset<T> asset, LoadedAssetDeferredCallback<T> callback)
		{
			if (Assets.shouldDeferLoadingAssets)
			{
				DeferredMasterAsset<T> deferredAsset = new DeferredMasterAsset<T>();
				deferredAsset.masterBundle = this;
				deferredAsset.name = name;
				deferredAsset.callback = callback;
				asset = deferredAsset;
			}
			else
			{
				base.loadDeferred<T>(name, out asset, callback);
			}
		}

		public override T load<T>(string name)
		{
			if (cfg.assetBundle == null)
			{
				UnturnedLog.warn("Failed to load '{0}' from master bundle '{1}' because asset bundle was null", name, cfg.assetBundleName);
				return default;
			}

			string assetPath = cfg.formatAssetPath(relativePath + '/' + name);

			string[] extensions;
			if (!typeExtensions.TryGetValue(typeof(T), out extensions))
			{
				UnturnedLog.warn("Unknown extension for type: " + typeof(T));
				return null;
			}

			// Since we are loading by path it fails if we don't provide an extension,
			// so we retry with the most common extensions for T
			foreach (string extension in extensions)
			{
				T asset = cfg.assetBundle.LoadAsset<T>(assetPath + extension);
				if (asset != null)
				{
#if WITH_ASSET_CONSOLIDATION
					AssetConsolidation.OnLoadingPath(assetPath + extension);
#endif
					processLoadedObject(asset);
					return asset;
				}
			}

			//UnturnedLog.warn(assetPath);
			return null;
		}

		public override string WhereLoadLookedToString(string name)
		{
			if (cfg.assetBundle == null)
			{
				return $"{name} in null asset bundle";
			}

			return $"{cfg.formatAssetPath(relativePath + '/' + name)} in {cfg.assetBundleName}";
		}

		public MasterBundle(MasterBundleConfig cfg, string relativePath, string name) : base(name)
		{
			this.cfg = cfg;
			this.relativePath = relativePath;
		}
	}
}
