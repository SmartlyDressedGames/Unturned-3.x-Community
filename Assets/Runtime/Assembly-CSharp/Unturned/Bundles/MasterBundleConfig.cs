////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using global::Unturned.SystemEx;
using global::Unturned.UnityEx;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SDG.Unturned
{
	public class MasterBundleConfig
	{
		public MasterBundleConfig(string absoluteDirectory, IDatDictionary data, AssetOrigin origin)
		{
			directoryPath = absoluteDirectory;
			this.origin = origin;

			assetBundleName = data.GetString("Asset_Bundle_Name");
			if (string.IsNullOrEmpty(assetBundleName))
			{
				throw new System.Exception("Unspecified Asset_Bundle_Name! This should be the file name and extension of the master asset bundle exported from Unity.");
			}

			assetBundleNameWithoutExtension = Path.GetFileNameWithoutExtension(assetBundleName);

			assetPrefix = data.GetString("Asset_Prefix");
			if (string.IsNullOrEmpty(assetPrefix))
			{
				throw new System.Exception("Unspecified Asset_Prefix! This should be the portion of the Unity asset path prior to the /Bundles/ path, e.g. Assets/Bundles/");
			}

			if (data.ContainsKey("Master_Bundle_Version"))
			{
				version = data.ParseInt32("Master_Bundle_Version");
			}
			else
			{
				version = data.ParseInt32("Asset_Bundle_Version", defaultValue: AssetBundleVersion.UNITY_2017_LTS);
			}

			if (version < AssetBundleVersion.UNITY_2017_LTS)
			{
				throw new System.Exception("Lowest master bundle version is 2 (default), associated with 2017.4 LTS.");
			}
			else if (version > AssetBundleVersion.NEWEST)
			{
				throw new System.Exception("Highest master bundle version is 6, associated with 2022 LTS.");
			}

			string expectedPath = getAssetBundlePath();
			if (!File.Exists(expectedPath))
			{
				throw new System.Exception("Unable to find specified Asset_Bundle_Name next to the config file! Expected path: " + expectedPath);
			}

			doesHashFileExist = File.Exists(getHashFilePath());
		}

		/// <summary>
		/// Absolute path to directory containing bundle and .dat file.
		/// </summary>
		public string directoryPath
		{
			get;
			protected set;
		}

		/// <summary>
		/// Name of the actual asset bundle file, e.g. Hawaii.unity3d
		/// Asset bundle should be next to this config file.
		/// </summary>
		public string assetBundleName
		{
			get;
			protected set;
		}

		/// <summary>
		/// assetBundleName without final .* extension.
		/// </summary>
		public string assetBundleNameWithoutExtension
		{
			get;
			protected set;
		}

		/// <summary>
		/// Prefixed to all asset paths loaded from asset bundle.
		/// Final path is built from assetPrefix + pathRelativeToBundlesFolder + assetName,
		/// e.g. Assets/Hawaii/Bundles + /Objects/Large/House/ + Object.prefab
		/// </summary>
		public string assetPrefix
		{
			get;
			protected set;
		}

		/// <summary>
		/// Custom asset bundle version used by Unturned to detect whether imports need
		/// fixing up because they were exported from an older version of Unity.
		/// </summary>
		public int version
		{
			get;
			protected set;
		}

		internal AssetOrigin origin;

		/// <summary>
		/// Get absolute path to asset bundle file.
		/// </summary>
		public string getAssetBundlePath()
		{
			string platformBundleName;
#if UNITY_STANDALONE_WIN
			platformBundleName = assetBundleName;
#elif UNITY_STANDALONE_OSX
			platformBundleName = MasterBundleHelper.getMacAssetBundleName(assetBundleName);
#elif UNITY_STANDALONE_LINUX
			platformBundleName = MasterBundleHelper.getLinuxAssetBundleName(assetBundleName);
#endif // UNITY_STANDALONE_LINUX

#if UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST
			if (string.Equals(assetBundleName, "core.masterbundle"))
			{
				// If changing this implementation please make sure to update getHashFilePath() please!

				bool shouldLoadCoreAssetBundleFromSteamInstall =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					Assets.shouldLoadCoreAssetBundleFromSteamInstall;
#else
					false;
#endif

				if (!shouldLoadCoreAssetBundleFromSteamInstall && UnityPaths.ProjectDirectory != null)
				{
					string editorPath = Path.Combine(UnityPaths.ProjectDirectory.FullName, "Builds", "CoreAssetBundle", platformBundleName);
					if (File.Exists(editorPath))
					{
						return editorPath;
					}
				}

				if (Provider.steamAppInstallDirectory != null)
				{
					return PathEx.Join(Provider.steamAppInstallDirectory, "Bundles", platformBundleName);
				}
			}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST

#if UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX
			string platformPath = Path.Combine(directoryPath, platformBundleName);
			if(File.Exists(platformPath))
			{
				return platformPath;
			}
#endif // UNITY_STANDALONE_OSX || UNITY_STANDALONE_LINUX

			return Path.Combine(directoryPath, assetBundleName);
		}

		/// <summary>
		/// Get absolute path to file with per-platform hashes.
		/// </summary>
		public string getHashFilePath()
		{
			// 2022-11-16 this does not call getAssetBundlePath() because that has per-platform overrides!

#if UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST
			if (string.Equals(assetBundleName, "core.masterbundle"))
			{
				bool shouldLoadCoreAssetBundleFromSteamInstall =
#if UNITY_EDITOR || DEVELOPMENT_BUILD
					Assets.shouldLoadCoreAssetBundleFromSteamInstall;
#else
					false;
#endif

				if (!shouldLoadCoreAssetBundleFromSteamInstall && UnityPaths.ProjectDirectory != null)
				{
					string editorPath = MasterBundleHelper.getHashFileName(Path.Combine(UnityPaths.ProjectDirectory.FullName, "Builds", "CoreAssetBundle", assetBundleName));
					if (File.Exists(editorPath))
					{
						return editorPath;
					}
				}

				if (Provider.steamAppInstallDirectory != null)
				{
					return MasterBundleHelper.getHashFileName(PathEx.Join(Provider.steamAppInstallDirectory, "Bundles", assetBundleName));
				}
			}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST

			return MasterBundleHelper.getHashFileName(Path.Combine(directoryPath, assetBundleName));
		}

		/// <summary>
		/// Insert path prefix if set.
		/// </summary>
		public string formatAssetPath(string assetPath)
		{
			if (string.IsNullOrEmpty(assetPrefix))
			{
				return assetPath;
			}
			else
			{
				if (assetPrefix.EndsWith("/", System.StringComparison.Ordinal) || assetPath.StartsWith("/", System.StringComparison.Ordinal))
				{
					return assetPrefix + assetPath;
				}
				else
				{
					return $"{assetPrefix}/{assetPath}";
				}
			}
		}

		/// <summary>
		/// When to use this instead of formatAssetPath? MasterBundleReference and AudioReference repeatedly invoke
		/// this string formatting (e.g., footstep sounds) and benefit from not generating that garbage.
		/// </summary>
		public string FormatAssetPathAndCache(string assetPath)
		{
			if (!formattedPaths.TryGetValue(assetPath, out string resultPath))
			{
				resultPath = formatAssetPath(assetPath);
				formattedPaths[assetPath] = resultPath;
			}
			return resultPath;
		}

		/// <summary>
		/// Loaded asset bundle.
		/// </summary>
		public AssetBundle assetBundle
		{
			get;
			protected set;
		}

		/// <summary>
		/// Hash of loaded asset bundle file.
		/// This is per-platform, so the server loads a hash file with all platform hashes.
		/// </summary>
		public byte[] hash
		{
			get;
			protected set;
		}

		/// <summary>
		/// True if the server .hash file exists.
		/// Hash file is not used by client, but client uses whether it exists to decide whether to include asset bundle hash in asset hash.
		/// </summary>
		internal bool doesHashFileExist;

		/// <summary>
		/// Hashes for Windows, Linux, and Mac asset bundles.
		/// Only loaded on the dedicated server. Null otherwise.
		/// </summary>
		internal MasterBundleHash serverHashes;

		internal AssetBundleCreateRequest assetBundleCreateRequest;
		private double loadStartTime;

		/// <summary>
		/// On the surface level this is rather silly.
		/// The primary reason for it is reducing garbage created by repeated calls to formatAssetPath.
		/// Theoretically we could use this for caching redirected paths if/when that feature is added.
		/// </summary>
		private Dictionary<string, string> formattedPaths = new Dictionary<string, string>();

		internal void CopyAssetBundleFromDuplicateConfig(MasterBundleConfig otherConfig)
		{
			sourceConfig = otherConfig;
			version = otherConfig.version;
			assetBundle = otherConfig.assetBundle;
			hash = otherConfig.hash;
			doesHashFileExist = otherConfig.doesHashFileExist;
			serverHashes = otherConfig.serverHashes;
			assetBundleCreateRequest = null;
			CheckOwnerCustomDataAndMaybeUnload();
		}

		/// <summary>
		/// Load the underlying asset bundle.
		/// </summary>
		public void StartLoad(byte[] inputData, byte[] inputHash)
		{
			UnturnedLog.info($"Loading asset bundle \"{assetBundleName}\" from \"{directoryPath}\"...");
			assetBundleCreateRequest = AssetBundle.LoadFromMemoryAsync(inputData);
			hash = inputHash;
			loadStartTime = Time.realtimeSinceStartupAsDouble;
		}

		public void FinishLoad()
		{
			assetBundle = assetBundleCreateRequest.assetBundle;
			CheckOwnerCustomDataAndMaybeUnload();

			if (assetBundle != null)
			{
				double duration = Time.realtimeSinceStartupAsDouble - loadStartTime;
				UnturnedLog.info($"Loading asset bundle \"{assetBundleName}\" from \"{directoryPath}\" took {duration}s");
			}
			else
			{
				UnturnedLog.warn($"Failed to load asset bundle \"{assetBundleName}\" from \"{directoryPath}\"");
			}
		}

		public void unload()
		{
			if (sourceConfig != null)
			{
				// Don't unload because we don't own this asset bundle.
				assetBundle = null;
				return;
			}

			if (assetBundle != null)
			{
				assetBundle.Unload(false);
				assetBundle = null;
			}
		}

		private void CheckOwnerCustomDataAndMaybeUnload()
		{
			if (assetBundle == null)
				return;

			string customDataPath = formatAssetPath("AssetBundleCustomData.asset");
			AssetBundleCustomData customData = assetBundle.LoadAsset<AssetBundleCustomData>(customDataPath);
			if (customData == null)
			{
				// UnturnedLog.info($"Tried loading \"{assetBundleName}\" optional custom data from \"{customDataPath}\"");
				return;
			}

			UnturnedLog.info($"Loaded \"{assetBundleName}\" custom data from \"{customDataPath}\"");
			bool hasIdsList = customData.ownerWorkshopFileIds != null && customData.ownerWorkshopFileIds.Count > 0;
			if (origin.workshopFileId > 0 && (customData.ownerWorkshopFileId > 0 || hasIdsList))
			{
				bool isAllowed = origin.workshopFileId == customData.ownerWorkshopFileId || (hasIdsList && customData.ownerWorkshopFileIds.Contains(origin.workshopFileId));
				if (!isAllowed)
				{
					string allowedIdsString;
					if (hasIdsList)
					{
						allowedIdsString = string.Join(", ", customData.ownerWorkshopFileIds);
						if (customData.ownerWorkshopFileId > 0)
						{
							allowedIdsString += ", ";
							allowedIdsString += customData.ownerWorkshopFileId;
						}
					}
					else
					{
						allowedIdsString = customData.ownerWorkshopFileId.ToString();
					}

					UnturnedLog.warn($"Unloading \"{assetBundle}\" because source workshop file ID ({origin.workshopFileId}) does not match owner workshop file ID(s) ({allowedIdsString})");
					unload();
				}
			}
		}

		public T LoadAsset<T>(string name) where T : UnityEngine.Object
		{
			string formattedPath = formatAssetPath(name);
			return assetBundle.LoadAsset<T>(formattedPath);
		}

		/// <summary>
		/// TODO: if adding additional calls, result should ideally wrap AssetBundleRequest so that
		/// bundle.processLoadedObject runs before returning the result. Should be consolidated with
		/// MasterBundleReference.LoadAssetAsync, too.
		/// </summary>
		public AssetBundleRequest LoadAssetAsync<T>(string name) where T : UnityEngine.Object
		{
			string formattedPath = formatAssetPath(name);
			return assetBundle.LoadAssetAsync<T>(formattedPath);
		}

		public override string ToString()
		{
			return string.Format("{0} in {1}", assetBundleNameWithoutExtension, directoryPath);
		}

		/// <summary>
		/// If true, the associated asset bundle couldn't be loaded and was instead copied from another config.
		/// </summary>
		internal MasterBundleConfig sourceConfig;
	}
}
