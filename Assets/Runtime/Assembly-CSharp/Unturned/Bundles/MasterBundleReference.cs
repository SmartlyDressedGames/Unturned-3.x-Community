////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using SDG.Framework.IO.FormattedFiles;
using UnityEngine;
using UnityEngine.Profiling;

namespace SDG.Unturned
{
	/// <summary>
	/// Essentially identical to ContentReference, but MasterBundle is more convenient.
	/// Perhaps in the future all asset/content systems will be consolidated.
	/// </summary>
	public struct MasterBundleReference<T> : IFormattedFileReadable, IFormattedFileWritable, IDatParseable where T : UnityEngine.Object
	{
		public static MasterBundleReference<T> invalid = new MasterBundleReference<T>(null, null);

		public MasterBundleReference(string name, string path)
		{
			this.name = name;
			this.path = path;
		}

		public bool TryParse(IDatNode node)
		{
			if (node is IDatValue value)
			{
				if (string.IsNullOrEmpty(value.Value))
				{
					return false;
				}

				if (value.Value.Length < 2)
				{
					// 2023-04-17: there seem to be a lot of copy-pasted material palette assets used
					// on curated maps with the same typo of missing a closing ']', so the final '}'
					// is getting parsed as a content reference. As a workaround we ignore 1-char strings.
					return false;
				}

				int delimiterIndex = value.Value.IndexOf(':');
				if (delimiterIndex < 0)
				{
					// Ideally we should have a warning if null. :(
					if (Assets.currentMasterBundle != null)
					{
						name = Assets.currentMasterBundle.assetBundleName;
					}
					path = value.Value;
				}
				else
				{
					name = value.Value.Substring(0, delimiterIndex);
					path = value.Value.Substring(delimiterIndex + 1);
				}
				return true;
			}
			else if (node is IDatDictionary dictionary)
			{
				name = dictionary.GetString("MasterBundle");
				path = dictionary.GetString("AssetPath");
				return true;
			}
			else
			{
				return false;
			}
		}

		public void read(IFormattedFileReader reader)
		{
			IFormattedFileReader nestedReader = reader.readObject();
			if (nestedReader == null)
			{
				if (Assets.currentMasterBundle != null)
				{
					name = Assets.currentMasterBundle.assetBundleName;
				}
				path = reader.readValue();
				return;
			}

			name = nestedReader.readValue("MasterBundle");
			path = nestedReader.readValue("AssetPath");
		}

		public void write(IFormattedFileWriter writer)
		{
			writer.beginObject();

			writer.writeValue("MasterBundle", name);
			writer.writeValue("AssetPath", path);

			writer.endObject();
		}

		public T loadAsset(bool logWarnings = true)
		{
			Profiler.BeginSample("MasterBundleReference.loadAsset");
			T asset;
			if (isNull)
			{
				asset = null;
			}
			else
			{
				MasterBundleConfig config = Assets.findMasterBundleByName(name);
				if (config == null || config.assetBundle == null)
				{
					if (logWarnings)
					{
						UnturnedLog.warn("Unable to find master bundle '{0}' when loading asset '{1}' as {2}", name, path, typeof(T).Name);
					}
					asset = null;
				}
				else
				{
					string formattedPath = config.FormatAssetPathAndCache(path);
					asset = config.assetBundle.LoadAsset<T>(formattedPath);
					if (asset != null)
					{
						if (asset is GameObject gameObject)
						{
#if !DEDICATED_SERVER
							Bundle.FixupGameObjectAudio(gameObject);
#endif // !DEDICATED_SERVER

							if (!StaticUnityEventPrevention.Validate(gameObject))
							{
								UnturnedLog.warn("Canceling load asset '{0}' from master bundle '{1}' because it failed UnityEvent checks", formattedPath, name);
								asset = null;
							}
						}
					}
					else if (logWarnings)
					{
						UnturnedLog.warn("Failed to load asset '{0}' from master bundle '{1}' as {2}", formattedPath, name, typeof(T).Name);
					}
				}
			}
			Profiler.EndSample(); // MasterBundleReference.loadAsset
			return asset;
		}

		public AssetBundleRequest LoadAssetAsync(bool logWarnings = true)
		{
			if (isNull)
			{
				return null;
			}

			MasterBundleConfig config = Assets.findMasterBundleByName(name);
			if (config == null || config.assetBundle == null)
			{
				if (logWarnings)
				{
					UnturnedLog.warn("Unable to find master bundle '{0}' when async loading asset '{1}' as {2}", name, path, typeof(T).Name);
				}

				return null;
			}

			string formattedPath = config.FormatAssetPathAndCache(path);
			return config.assetBundle.LoadAssetAsync<T>(formattedPath);
		}

		/// <summary>
		/// Are name or path null or empty?
		/// </summary>
		public bool isNull => string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path);

		/// <summary>
		/// Are both name and path non-null and non-empty?
		/// </summary>
		public bool isValid => !isNull;

		/// <summary>
		/// Name of master bundle file.
		/// </summary>
		public string name;

		/// <summary>
		/// Path to Unity asset within asset bundle.
		/// </summary>
		public string path;

		public override string ToString()
		{
			return string.Format("{0}:{1}", name, path);
		}
	}
}
