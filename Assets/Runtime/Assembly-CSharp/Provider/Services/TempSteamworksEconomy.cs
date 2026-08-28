////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
// Prior to the server-side "auto_stack" property the client was responsible for stack consolidation using TransferItemQuantity.
//#define CLIENT_CONSOLIDATE_STACKS
#if UNITY_EDITOR || DEVELOPMENT_BUILD
#define ENABLE_TEST_INVENTORY
#endif

// Optionally enable test inventory outside development builds.
// #define ENABLE_TEST_INVENTORY

using global::Unturned.SystemEx;
using global::Unturned.UnityEx;
using SDG.Unturned;
using Steamworks;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace SDG.Provider
{
	public struct EconExchangePair
	{
		public ulong instanceId;
		public ushort quantity;

		public EconExchangePair(ulong newInstanceId, ushort newQuantity)
		{
			this.instanceId = newInstanceId;
			this.quantity = newQuantity;
		}
	}

	public struct StatTrackerTotalKillsJson
	{
		public int total_kills;
	}

	public struct StatTrackerPlayerKillsJson
	{
		public int player_kills;
	}

	public enum EStatTrackerType
	{
		NONE,
		TOTAL,
		PLAYER
	}

	public struct DynamicEconDetails
	{
		public string tags;
		public string dynamic_props;

		public bool getStatTrackerType(out EStatTrackerType type)
		{
			type = EStatTrackerType.NONE;

			if (tags.Contains("stat_tracker:total_kills"))
			{
				type = EStatTrackerType.TOTAL;
				return true;
			}
			else if (tags.Contains("stat_tracker:player_kills"))
			{
				type = EStatTrackerType.PLAYER;
				return true;
			}

			return false;
		}

		public bool getRagdollEffect(out ERagdollEffect effect)
		{
			const string RAGDOLL_EFFECT_TAG = "ragdoll_effect:";
			int tagIndex = tags.IndexOf(RAGDOLL_EFFECT_TAG);
			if (tagIndex >= 0)
			{
				tagIndex += RAGDOLL_EFFECT_TAG.Length;
				if (tagIndex < tags.Length - 1)
				{
					System.ReadOnlySpan<char> tagSpan = tags.AsSpan(tagIndex, tags.Length - tagIndex);
					if (tagSpan.StartsWith("zero_kelvin", System.StringComparison.Ordinal))
					{
						effect = ERagdollEffect.Zero_Kelvin;
						return true;
					}
					else if (tagSpan.StartsWith("jaded", System.StringComparison.Ordinal))
					{
						effect = ERagdollEffect.Jaded;
						return true;
					}
					else if (tagSpan.StartsWith("soulcrystal_", System.StringComparison.Ordinal))
					{
						tagIndex += "soulcrystal_".Length;
						tagSpan = tags.AsSpan(tagIndex, tags.Length - tagIndex);

						if (tagSpan.StartsWith("green", System.StringComparison.Ordinal))
						{
							effect = ERagdollEffect.SoulCrystal_Green;
							return true;
						}
						else if (tagSpan.StartsWith("magenta", System.StringComparison.Ordinal))
						{
							effect = ERagdollEffect.SoulCrystal_Magenta;
							return true;
						}
						else if (tagSpan.StartsWith("red", System.StringComparison.Ordinal))
						{
							effect = ERagdollEffect.SoulCrystal_Red;
							return true;
						}
						else if (tagSpan.StartsWith("yellow", System.StringComparison.Ordinal))
						{
							effect = ERagdollEffect.SoulCrystal_Yellow;
							return true;
						}
					}
					else if (tagSpan.StartsWith("rosegold", System.StringComparison.Ordinal))
					{
						effect = ERagdollEffect.Rosegold;
						return true;
					}
					else if (tagSpan.StartsWith("void", System.StringComparison.Ordinal))
					{
						effect = ERagdollEffect.Void;
						return true;
					}
					else if (tagSpan.StartsWith("rainbow", System.StringComparison.Ordinal))
					{
						effect = ERagdollEffect.Rainbow;
						return true;
					}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
					UnturnedLog.warn($"Unable to parse unknown ragdoll effect from tags \"{tags}\"");
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
				}
			}

			effect = ERagdollEffect.None;
			return false;
		}

		/// <summary>
		/// Parse dynamic tag mythic effect.
		/// </summary>
		/// <returns>ID of mythical asset, or zero if not in tags.</returns>
		public ushort getParticleEffect()
		{
			const string key = "particle_effect:";
			int keyIndex = tags.IndexOf(key);

			if (keyIndex >= 0)
			{
				int valueStartIndex = keyIndex + key.Length;
				if (valueStartIndex < tags.Length)
				{
					int valueEndIndex = tags.IndexOf(';', valueStartIndex);
					if (valueEndIndex < 0)
					{
						// Value goes to the end of tags string.
						valueEndIndex = tags.Length;
					}

					int valueLength = valueEndIndex - valueStartIndex;
					string valueStr = tags.Substring(valueStartIndex, valueLength);

					ushort value;
					if (ushort.TryParse(valueStr, out value))
					{
						return value;
					}
					else
					{
						return 0;
					}
				}
				else
				{
					return 0;
				}
			}
			else
			{
				return 0;
			}
		}

		public bool getStatTrackerValue(out EStatTrackerType type, out int kills)
		{
			kills = -1;

			if (!getStatTrackerType(out type))
			{
				return false;
			}

			switch (type)
			{
				case EStatTrackerType.TOTAL:
					if (string.IsNullOrEmpty(dynamic_props))
					{
						kills = 0;
					}
					else
					{
						StatTrackerTotalKillsJson json = JsonUtility.FromJson<StatTrackerTotalKillsJson>(dynamic_props);
						kills = json.total_kills;
					}
					return true;

				case EStatTrackerType.PLAYER:
					if (string.IsNullOrEmpty(dynamic_props))
					{
						kills = 0;
					}
					else
					{
						StatTrackerPlayerKillsJson json = JsonUtility.FromJson<StatTrackerPlayerKillsJson>(dynamic_props);
						kills = json.player_kills;
					}
					return true;
			}

			return false;
		}

		public string getPredictedDynamicPropsJsonForStatTracker(EStatTrackerType type, int kills)
		{
			switch (type)
			{
				case EStatTrackerType.TOTAL:
				{
					StatTrackerTotalKillsJson json = new StatTrackerTotalKillsJson();
					json.total_kills = kills;
					return JsonUtility.ToJson(json);
				}

				case EStatTrackerType.PLAYER:
				{
					StatTrackerPlayerKillsJson json = new StatTrackerPlayerKillsJson();
					json.player_kills = kills;
					return JsonUtility.ToJson(json);
				}
			}

			return string.Empty;
		}

		public DynamicEconDetails(string tags, string dynamic_props)
		{
			this.tags = string.IsNullOrEmpty(tags) ? string.Empty : tags;
			this.dynamic_props = string.IsNullOrEmpty(dynamic_props) ? string.Empty : dynamic_props;
		}
	}

	public class TempSteamworksEconomy
	{
		public bool canOpenInventory => true;// SteamUtils.IsOverlayEnabled();

		public void open(ulong id)
		{
			SDG.Unturned.WebUtils.OpenURL("https://steamcommunity.com/profiles/" + SteamUser.GetSteamID() + "/inventory/?sellOnLoad=1#" + SteamUtils.GetAppID() + "_2_" + id);
		}

		// old code:

		internal static Dictionary<int, UnturnedEconInfo> econInfo
		{
			get;
			private set;
		}

		public static byte[] econInfoHash
		{
			get;
			private set;
		}

		/// <summary>
		/// For purchasable box and bundle itemdefs this maps their itemdefid to the list of itemdefids in their desc.
		/// </summary>
		private static Dictionary<int, List<int>> bundleContents;

		public delegate void InventoryRefreshed();
		public delegate void InventoryDropped(int item, ushort quantity, ulong instance);
		public delegate void InventoryExchanged(List<SteamItemDetails_t> grantedItems);
		public delegate void InventoryExchangeFailed();

		public InventoryRefreshed onInventoryRefreshed;
		public InventoryDropped onInventoryDropped;

		/// <summary>
		/// Invoked after a successful exchange with the newly granted items.
		/// </summary>
		public InventoryExchanged onInventoryExchanged;

		/// <summary>
		/// Invoke after a succesful purchase from the item store.
		/// </summary>
		public InventoryExchanged onInventoryPurchased;

		public InventoryExchangeFailed onInventoryExchangeFailed;

		private SteamInventoryResult_t promoResult = SteamInventoryResult_t.Invalid;
		public SteamInventoryResult_t exchangeResult = SteamInventoryResult_t.Invalid;
		public SteamInventoryResult_t dropResult = SteamInventoryResult_t.Invalid;
		public SteamInventoryResult_t wearingResult = SteamInventoryResult_t.Invalid;
		public SteamInventoryResult_t inventoryResult = SteamInventoryResult_t.Invalid;
		public SteamInventoryResult_t commitResult = SteamInventoryResult_t.Invalid;
#if CLIENT_CONSOLIDATE
		public SteamInventoryResult_t stackResult = SteamInventoryResult_t.Invalid;
#endif
		public List<SteamItemDetails_t> inventoryDetails = new List<SteamItemDetails_t>(0);
		public List<SteamItemDetails_t> inventory => inventoryDetails;
		public Dictionary<ulong, DynamicEconDetails> dynamicInventoryDetails = new Dictionary<ulong, DynamicEconDetails>();
		public bool isInventoryAvailable;

		/// <summary>
		/// Purchase result does not have a handle, so we guess based on when it arrives.
		/// </summary>
		public bool isExpectingPurchaseResult;

		private SDG.SteamworksProvider.SteamworksAppInfo appInfo;

		/// <summary>
		/// Find the first instanceId of a given itemDefId.
		/// </summary>
		public ulong getInventoryPackage(int item)
		{
			if (inventoryDetails != null)
			{
				for (int index = 0; index < inventoryDetails.Count; ++index)
				{
					if (inventoryDetails[index].m_iDefinition.m_SteamItemDef == item)
					{
						return inventoryDetails[index].m_itemId.m_SteamItemInstanceID;
					}
				}
			}

			return 0;
		}

		/// <summary>
		/// Count quantity of a given itemDefId.
		/// </summary>
		public int countInventoryPackages(int item)
		{
			int count = 0;

			if (inventoryDetails != null)
			{
				for (int index = 0; index < inventoryDetails.Count; ++index)
				{
					if (inventoryDetails[index].m_iDefinition.m_SteamItemDef == item)
					{
						count += inventoryDetails[index].m_unQuantity;
					}
				}
			}

			return count;
		}

		/// <summary>
		/// Find certain quantity of given itemDefId.
		/// </summary>
		public bool getInventoryPackages(int item, ushort requiredQuantity, out List<EconExchangePair> pairs)
		{
			ushort foundQuantity = 0;
			pairs = new List<EconExchangePair>();

			if (inventoryDetails != null)
			{
				for (int index = 0; index < inventoryDetails.Count; ++index)
				{
					if (inventoryDetails[index].m_iDefinition.m_SteamItemDef != item)
						continue;

					ushort itemQuantity = inventoryDetails[index].m_unQuantity;
					if (itemQuantity < 1)
						continue;

					ushort idealQuantity = (ushort) (requiredQuantity - foundQuantity);

					if (itemQuantity >= idealQuantity)
					{
						itemQuantity = idealQuantity;
					}

					EconExchangePair pair = new EconExchangePair(inventoryDetails[index].m_itemId.m_SteamItemInstanceID, itemQuantity);
					pairs.Add(pair);

					foundQuantity += itemQuantity;
					if (foundQuantity == requiredQuantity)
						return true;
				}
			}

			return false;
		}

		public int getInventoryItem(ulong package)
		{
			if (inventoryDetails != null)
			{
				for (int index = 0; index < inventoryDetails.Count; ++index)
				{
					if (inventoryDetails[index].m_itemId.m_SteamItemInstanceID == package)
					{
						return inventoryDetails[index].m_iDefinition.m_SteamItemDef;
					}
				}
			}

			return 0;
		}

		//public string getInventoryProperty(int item, string property)
		//{
		//string value;
		//uint length = 1024;

		//SteamInventory.GetItemDefinitionProperty((SteamItemDef_t) item, property, out value, ref length);

		//return value;
		//}

		public string getInventoryTags(ulong instance)
		{
			DynamicEconDetails details;
			if (!dynamicInventoryDetails.TryGetValue(instance, out details))
			{
				return string.Empty;
			}

			return details.tags;
		}

		public string getInventoryDynamicProps(ulong instance)
		{
			DynamicEconDetails details;
			if (!dynamicInventoryDetails.TryGetValue(instance, out details))
			{
				return string.Empty;
			}

			return details.dynamic_props;
		}

		public bool getInventoryStatTrackerValue(ulong instance, out EStatTrackerType type, out int kills)
		{
			DynamicEconDetails details;
			if (!dynamicInventoryDetails.TryGetValue(instance, out details))
			{
				type = EStatTrackerType.NONE;
				kills = -1;
				return false;
			}

			return details.getStatTrackerValue(out type, out kills);
		}

		public bool getInventoryRagdollEffect(ulong instance, out ERagdollEffect effect)
		{
			DynamicEconDetails details;
			if (!dynamicInventoryDetails.TryGetValue(instance, out details))
			{
				effect = ERagdollEffect.None;
				return false;
			}

			return details.getRagdollEffect(out effect);
		}

		public ushort getInventoryParticleEffect(ulong instance)
		{
			DynamicEconDetails details;
			if (dynamicInventoryDetails.TryGetValue(instance, out details))
			{
				return details.getParticleEffect();
			}
			else
			{
				return 0;
			}
		}

		private UnturnedEconInfo FindEconInfo(int itemdefid)
		{
			if (econInfo.TryGetValue(itemdefid, out UnturnedEconInfo info))
			{
				return info;
			}
			else
			{
				return null;
			}
		}

		/// <summary>
		/// Does itemdefid exist in the EconInfo.json file?
		/// </summary>
		public bool IsItemKnown(int item)
		{
			return FindEconInfo(item) != null;
		}

		public string getInventoryName(int item)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				return "";
			}

			return info.name;
			//return getInventoryProperty(item, "name");
		}

		internal System.DateTime GetCreationTime(int itemdefid)
		{
			UnturnedEconInfo info = FindEconInfo(itemdefid);
			return info != null ? info.creationTimeUtc : DateTime.MinValue;
		}

		public string getInventoryType(int item)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				return "";
			}

			return info.display_type;
			//return getInventoryProperty(item, "type");
		}

		public bool IsItemBundle(int itemdefid)
		{
			UnturnedEconInfo info = FindEconInfo(itemdefid);
			if (info != null)
			{
				return info.econ_type == 13; // EItemType.Bundle
			}
			else
			{
				return false;
			}
		}

		public bool IsItemEligibleForPromotion(int itemdefid)
		{
			UnturnedEconInfo info = FindEconInfo(itemdefid);
			if (info != null)
			{
				return info.isEligibleForPromotion;
			}
			else
			{
				return false;
			}
		}

		public string getInventoryDescription(int item)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				return "";
			}

			return info.description;
			//return getInventoryProperty(item, "description");
		}

		public bool getInventoryMarketable(int item)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				return false;
			}

			return info.marketable;
			//string value = getInventoryProperty(item, "marketable");

			//return value == "1";
		}

		public int getInventoryScraps(int item)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				return 0;
			}

			return info.scraps;
		}

		/// <summary>
		/// Get item with an exchange recipe for the appropriate number of scraps.
		/// </summary>
		public int getScrapExchangeItem(int item)
		{
			int scraps = getInventoryScraps(item);
			switch (scraps)
			{
				default:
				case 0:
					return 0;
				case 1:
					return 19006;
				case 2:
					return 19007;
				case 3:
					return 19008;
				case 4:
					return 19009;
				case 5:
					return 19010;
				case 10:
					return 19011;
			}
		}

		public Color getInventoryColor(int item)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				return Color.white;
			}

			uint data;
			if (info.name_color != null && info.name_color.Length > 0 && uint.TryParse(info.name_color, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.CurrentCulture, out data))
			{
				uint r = (data >> 16) & 0xFF;
				uint g = (data >> 8) & 0xFF;
				uint b = data & 0xFF;

				return new Color(r / 255f, g / 255f, b / 255f);
			}
			else
			{
				return Color.white;
			}
		}

		public UnturnedEconInfo.EQuality getInventoryQuality(int item)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				return UnturnedEconInfo.EQuality.None;
			}
			else
			{
				return info.quality;
			}
		}

		public UnturnedEconInfo.ERarity getInventoryRarity(int item)
		{
			UnturnedEconInfo.EQuality quality = getInventoryQuality(item);
			switch (quality)
			{
				default:
				case UnturnedEconInfo.EQuality.None:
					return UnturnedEconInfo.ERarity.Unknown;

				case UnturnedEconInfo.EQuality.Common:
					return UnturnedEconInfo.ERarity.Common;
				case UnturnedEconInfo.EQuality.Uncommon:
					return UnturnedEconInfo.ERarity.Uncommon;
				case UnturnedEconInfo.EQuality.Gold:
					return UnturnedEconInfo.ERarity.Gold;
				case UnturnedEconInfo.EQuality.Rare:
					return UnturnedEconInfo.ERarity.Rare;
				case UnturnedEconInfo.EQuality.Epic:
					return UnturnedEconInfo.ERarity.Epic;
				case UnturnedEconInfo.EQuality.Legendary:
					return UnturnedEconInfo.ERarity.Legendary;
				case UnturnedEconInfo.EQuality.Mythical:
					return UnturnedEconInfo.ERarity.Mythical;
				case UnturnedEconInfo.EQuality.Premium:
					return UnturnedEconInfo.ERarity.Premium;
				case UnturnedEconInfo.EQuality.Achievement:
					return UnturnedEconInfo.ERarity.Achievement;
			}
		}

		public EItemRarity getGameRarity(int item)
		{
			UnturnedEconInfo.EQuality quality = getInventoryQuality(item);
			switch (quality)
			{
				default:
				case UnturnedEconInfo.EQuality.None:
				case UnturnedEconInfo.EQuality.Premium:
				case UnturnedEconInfo.EQuality.Achievement:
				case UnturnedEconInfo.EQuality.Common:
					return EItemRarity.COMMON;

				case UnturnedEconInfo.EQuality.Uncommon:
					return EItemRarity.UNCOMMON;
				case UnturnedEconInfo.EQuality.Rare:
					return EItemRarity.RARE;
				case UnturnedEconInfo.EQuality.Epic:
					return EItemRarity.EPIC;
				case UnturnedEconInfo.EQuality.Legendary:
					return EItemRarity.LEGENDARY;
				case UnturnedEconInfo.EQuality.Mythical:
					return EItemRarity.MYTHICAL;
			}
		}

		public Color getStatTrackerColor(EStatTrackerType type)
		{
			switch (type)
			{
				case EStatTrackerType.NONE:
					return Color.white;

				case EStatTrackerType.TOTAL:
					return new Color(1.0f, 0.5f, 0.0f);

				case EStatTrackerType.PLAYER:
					return new Color(1.0f, 0.0f, 0.0f);
			}

			return Color.black;
		}

		public string getStatTrackerPropertyName(EStatTrackerType type)
		{
			switch (type)
			{
				case EStatTrackerType.TOTAL:
					return "total_kills";

				case EStatTrackerType.PLAYER:
					return "player_kills";
			}

			return null;
		}

		public ushort getInventoryMythicID(int item)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				return 0;
			}

			return (ushort) info.item_effect;
			//string value = getInventoryProperty(item, "item_effect");

			//ushort mythicID;
			//if(value != null && value.Length > 0 && ushort.TryParse(value, out mythicID))
			//{
			//	return mythicID;
			//}
			//else
			//{
			//	return 0;
			//}
		}

		public void getInventoryTargetID(int item, out System.Guid item_guid, out System.Guid vehicle_guid)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				item_guid = default;
				vehicle_guid = default;
				return;
			}

			item_guid = info.target_game_asset_guid;
			vehicle_guid = info.target_game_asset_guid;
		}

		public System.Guid getInventoryItemGuid(int item)
		{
			System.Guid item_guid;
			System.Guid vehicle_guid;
			getInventoryTargetID(item, out item_guid, out vehicle_guid);
			return item_guid;
		}

		public ushort getInventorySkinID(int item)
		{
			UnturnedEconInfo info = FindEconInfo(item);
			if (info == null)
			{
				return 0;
			}

			return (ushort) info.item_skin;
			//string value = getInventoryProperty(item, "item_skin");

			//ushort skinID;
			//if(value != null && value.Length > 0 && ushort.TryParse(value, out skinID))
			//{
			//	return skinID;
			//}
			//else
			//{
			//	return 0;
			//}
		}

		public Texture2D LoadItemIcon(int itemdefid)
		{
			UnturnedEconInfo info = FindEconInfo(itemdefid);
			if (info == null)
				return null;

			if (info.target_game_asset_guid != default)
			{
				ItemAsset itemAsset = Assets.find<ItemAsset>(info.target_game_asset_guid);
				if (itemAsset == null)
					return null;

				if (itemAsset.econIconUseId)
				{
					// Hack for Kuwait aura icons.
					return Resources.Load<Texture2D>("Economy/Item/" + itemdefid + "/Icon_Large");
				}
				else if (itemAsset.type == EItemType.SHIRT || itemAsset.type == EItemType.PANTS || itemAsset.type == EItemType.HAT || itemAsset.type == EItemType.BACKPACK || itemAsset.type == EItemType.VEST || itemAsset.type == EItemType.GLASSES || itemAsset.type == EItemType.MASK)
				{
					return Resources.Load<Texture2D>("Economy/CosmeticPreviews/" + itemAsset.GUID.ToString("N"));
				}
				else if (itemAsset.proPath == null || itemAsset.proPath.Length == 0)
				{
					// Previously loaded item skins from resources. As of 2023-08-24 these are generated on demand instead.
					return null;
				}
				else
				{
					return Resources.Load<Texture2D>("Economy" + itemAsset.proPath + "/Icon_Large");
				}
			}
			else
			{
				// Fallback for "bundle" item icons.
				return Resources.Load<Texture2D>("Economy/Item/" + itemdefid + "/Icon_Large");
			}
		}

		/// <summary>
		/// Get list of itemdefids mentioned in purchasable box or bundle item description.
		/// </summary>
		internal List<int> GetBundleContents(int itemdefid)
		{
			if (bundleContents.TryGetValue(itemdefid, out List<int> containedItemDefIds))
			{
				return containedItemDefIds;
			}
			else
			{
				return null;
			}
		}

		internal HashSet<int> GatherOwnedItemDefIds()
		{
			HashSet<int> ownedItemDefIds = new HashSet<int>(inventoryDetails.Count);
			foreach (SteamItemDetails_t itemDetails in inventoryDetails)
			{
				ownedItemDefIds.Add(itemDetails.m_iDefinition.m_SteamItemDef);
			}
			return ownedItemDefIds;
		}

		public void consumeItem(ulong instance, uint quantity)
		{
			UnturnedLog.info("Consume item: {0} x{1}", instance, quantity);

			SteamInventoryResult_t result;
			Steamworks.SteamInventory.ConsumeItem(out result, (SteamItemInstanceID_t) instance, quantity);
		}

		public void exchangeInventory(int generate, List<EconExchangePair> destroy)
		{
			UnturnedLog.info("Exchange these item instances for " + generate);
			for (int destroyIndex = 0; destroyIndex < destroy.Count; ++destroyIndex)
			{
				ulong instance = destroy[destroyIndex].instanceId;

				int foundDetailsIndex = -1;
				for (int detailsIndex = 0; detailsIndex < inventoryDetails.Count; ++detailsIndex)
				{
					if (inventoryDetails[detailsIndex].m_itemId.m_SteamItemInstanceID == instance)
					{
						foundDetailsIndex = detailsIndex;
						break;
					}
				}

				if (foundDetailsIndex == -1)
				{
					UnturnedLog.error("Unable to find item for exchange: {0}", instance);
					return;
				}

				SteamItemDetails_t details = inventoryDetails[foundDetailsIndex];
				UnturnedLog.info(details.m_iDefinition.m_SteamItemDef + " [" + instance + "] x" + destroy[destroyIndex].quantity);

				if (destroy[destroyIndex].quantity >= details.m_unQuantity)
				{
					UnturnedLog.info("Locally removed item - Instance: {0} Definition: {1}", instance, details.m_iDefinition.m_SteamItemDef);

					inventoryDetails.RemoveAtFast(foundDetailsIndex);

					// Remove any dynamic info as well.
					dynamicInventoryDetails.Remove(instance);
				}
			}

			SteamItemDef_t[] generateArray = new SteamItemDef_t[1];
			uint[] generateQuantity = new uint[1];

			generateArray[0] = (SteamItemDef_t) generate;
			generateQuantity[0] = 1;

			SteamItemInstanceID_t[] destroyArray = new SteamItemInstanceID_t[destroy.Count];
			uint[] destroyQuantity = new uint[destroy.Count];

			for (int index = 0; index < destroy.Count; index++)
			{
				destroyArray[index] = (SteamItemInstanceID_t) destroy[index].instanceId;
				destroyQuantity[index] = destroy[index].quantity;
			}

			SteamInventory.ExchangeItems(out exchangeResult, generateArray, generateQuantity, (uint) generateArray.Length, destroyArray, destroyQuantity, (uint) destroyArray.Length);
		}

#if CLIENT_CONSOLIDATE_STACKS
		/// <summary>
		/// If two separate stacks of a given item are found then merge them into the larger stack.
		/// </summary>
		public void consolidateStacksByItemDefinition(int itemdefid, out bool busy)
		{
			if(stackResult != SteamInventoryResult_t.Invalid)
			{
				busy = true;
				return; // Waiting on a previous result.
			}

			SteamItemInstanceID_t sourceStackInstance = SteamItemInstanceID_t.Invalid;
			ushort sourceStackQuantity = 0;

			SteamItemInstanceID_t destinationStackInstance = SteamItemInstanceID_t.Invalid;
			ushort destinationStackQuantity = 0;

			foreach(SteamItemDetails_t item in inventoryDetails)
			{
				if(item.m_iDefinition.m_SteamItemDef != itemdefid)
					continue;
				
				if(item.m_unQuantity > destinationStackQuantity)
				{
					sourceStackInstance = destinationStackInstance;
					sourceStackQuantity = destinationStackQuantity;

					destinationStackInstance = item.m_itemId;
					destinationStackQuantity = item.m_unQuantity;
				}
				else
				{
					sourceStackInstance = item.m_itemId;
					sourceStackQuantity = item.m_unQuantity;
				}
			}

			if(sourceStackInstance == destinationStackInstance || sourceStackQuantity < 1 || destinationStackQuantity < 1)
			{
				busy = false;
				return;
			}

			UnturnedLog.info("Merging stack {0} x{1} into {2} x{3}", sourceStackInstance, sourceStackQuantity, destinationStackInstance, destinationStackQuantity);
			SteamInventory.TransferItemQuantity(out stackResult, sourceStackInstance, sourceStackQuantity, destinationStackInstance);
			busy = true;
		}

		public void consolidateStacks()
		{
			bool busy;
			consolidateStacksByItemDefinition(19000, out busy); // Crafting Materials
		}
#endif // CLIENT_CONSOLIDATE_STACKS

		private float lastHeartbeat;
		public void updateInventory()
		{
			if (Time.realtimeSinceStartup - lastHeartbeat < 30.0f)
			{
				return;
			}
			lastHeartbeat = Time.realtimeSinceStartup;

			SteamInventory.SendItemDropHeartbeat();
		}

		public void dropInventory()
		{
#if !DEDICATED_SERVER
			SteamItemDef_t dropListDef = new SteamItemDef_t(LiveConfig.Get().playtimeGeneratorItemDefId);
			if (dropListDef.m_SteamItemDef > 0)
			{
				UnturnedLog.info($"Requesting playtime drop ({dropListDef})");
				SteamInventory.TriggerItemDrop(out dropResult, dropListDef);
			}

			// Refresh promo items in case player completed an achievement.
			GrantPromoItems();
#endif // !DEDICATED_SERVER
		}

		public void GrantPromoItems()
		{
			if (promoResult == SteamInventoryResult_t.Invalid)
			{
				UnturnedLog.info("Requesting promo item grant");
				SteamInventory.GrantPromoItems(out promoResult);
			}
		}

#if ENABLE_TEST_INVENTORY
		private bool hasLoadedInventoryFromTestFile;
		private void LoadInventoryFromTestFile(string filePath)
		{
			if (hasLoadedInventoryFromTestFile)
				return;
			hasLoadedInventoryFromTestFile = true;
			UnturnedLog.info("Loading Steam inventory from test file: {0}", filePath);

			dynamicInventoryDetails.Clear();
			inventoryDetails = new List<SteamItemDetails_t>();

			DatParser parser = new DatParser();
			parser.EnableMetadata = true;
			IDatDictionary data;
			using (FileStream fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
			using (StreamReader streamReader = new StreamReader(fileStream))
			{
				data = parser.Parse(streamReader);
			}

			Dictionary<int, uint> itemdefInstanceCounter = new Dictionary<int, uint>();
			System.Func<int, ulong> CreateInstanceId = (int itemdefid) =>
			{
				uint count;
				if (itemdefInstanceCounter.TryGetValue(itemdefid, out count))
				{
					++count;
				}
				else
				{
					count = 1;
				}
				itemdefInstanceCounter[itemdefid] = count;
				return (((ulong) itemdefid) << 32) | count;
			};

			if (data.TryGetList("Items", out IDatList itemsList))
			{
				foreach (IDatNode itemNode in itemsList)
				{
					if (itemNode is IDatValue itemValue)
					{
						if (itemValue.TryParseInt32(out int itemdefid))
						{
							ulong iteminstanceid = CreateInstanceId(itemdefid);
							UnturnedLog.info($"Adding itemdef ID {itemdefid} (fake instance ID: {iteminstanceid})");

							SteamItemDetails_t fakeItem = new SteamItemDetails_t();
							fakeItem.m_iDefinition = new SteamItemDef_t(itemdefid);
							fakeItem.m_itemId = new SteamItemInstanceID_t(iteminstanceid);
							fakeItem.m_unFlags = 0;
							fakeItem.m_unQuantity = 1;
							inventoryDetails.Add(fakeItem);
						}
					}
					else if (itemNode is IDatDictionary itemDict)
					{
						if (itemDict.TryParseInt32("DefinitionId", out int itemdefid))
						{
							// Nelson 2025-02-28: This option makes it easier to find a specific mythical variant.
							if (itemDict.TryParseInt32("MythicSearchId", out int mythicSearchId))
							{
								bool found = false;
								for (int offset = 0; offset < 90; ++offset)
								{
									int testItemDefId = itemdefid + offset;
									if (FindEconInfo(testItemDefId)?.item_effect == mythicSearchId)
									{
										itemdefid = testItemDefId;
										found = true;
										break;
									}
								}

								if (!found)
								{
									Asset asset = Assets.find(EAssetType.MYTHIC, (ushort) mythicSearchId);
									UnturnedLog.warn($"Mythic effect {mythicSearchId} ({asset?.name ?? "null"}) is not compatible with {itemdefid}");
									continue;
								}
							}

							int quantity = itemDict.ParseInt32("Quantity", 1);
							ulong instanceId = itemDict.ParseUInt64("InstanceId");
							if (instanceId == 0)
							{
								instanceId = CreateInstanceId(itemdefid);
							}
							if (!itemDict.TryGetString("Tags", out string tags))
							{
								tags = string.Empty;

								ERagdollEffect ragdollEffect = itemDict.ParseEnum<ERagdollEffect>("RagdollEffect");
								if (ragdollEffect != ERagdollEffect.None)
								{
									if (tags.Length > 0)
									{
										tags += ";";
									}
									tags += "ragdoll_effect:";
									tags += ragdollEffect.ToString().ToLower();
								}

								if (itemDict.TryParseUInt16("ParticleEffect", out ushort mythicId))
								{
									if (tags.Length > 0)
									{
										tags += ";";
									}
									tags += $"particle_effect:{mythicId}";
								}

								EStatTrackerType statTracker = itemDict.ParseEnum<EStatTrackerType>("KillCounter");
								if (statTracker != EStatTrackerType.NONE)
								{
									if (tags.Length > 0)
									{
										tags += ";";
									}
									switch (statTracker)
									{
										case EStatTrackerType.PLAYER:
											tags += "stat_tracker:player_kills";
											break;

										case EStatTrackerType.TOTAL:
											tags += "stat_tracker:total_kills";
											break;
									}
								}
							}

							UnturnedLog.info($"Adding itemdef ID {itemdefid} x {quantity} (fake instance ID: {instanceId} tags: \"{tags}\")");
							SteamItemDetails_t fakeItem = new SteamItemDetails_t();
							fakeItem.m_iDefinition = new SteamItemDef_t(itemdefid);
							fakeItem.m_itemId = new SteamItemInstanceID_t(instanceId);
							fakeItem.m_unFlags = 0;
							fakeItem.m_unQuantity = (ushort) quantity;
							inventoryDetails.Add(fakeItem);

							if (!string.IsNullOrEmpty(tags))
							{
								dynamicInventoryDetails.Add(instanceId, new DynamicEconDetails()
								{
									tags = tags,
									dynamic_props = string.Empty,
								});
							}

							string name = getInventoryName(itemdefid);
							if (!string.IsNullOrEmpty(name))
							{
								itemDict.Edit().TryGetValue("DefinitionId", out IDatValue defIdNode);
								defIdNode.Edit().Comment = name;
							}
						}
					}
				}
			}

			onInventoryRefreshed?.Invoke();
			isInventoryAvailable = true;

			SDG.Unturned.Provider.isLoadingInventory = false;

			const bool append = false;
			using (StreamWriter fileStreamWriter = new StreamWriter(filePath, append, System.Text.Encoding.UTF8))
			{
				DatWriter datWriter = new DatWriter();
				MetadataPreservingDatWriter metadataPreservingDatWriter = new MetadataPreservingDatWriter();
				datWriter.SetOutput(fileStreamWriter);
				metadataPreservingDatWriter.WriteRootDictionary(data, datWriter);
			}
		}
#endif // ENABLE_TEST_INVENTORY

		/// <summary>
		/// One player's inventory became so large that the Steam client's built-in GetInventory fails,
		/// so as temporary fix we can send them a json file with their inventory.
		/// </summary>
		private void loadInventoryFromResponseFile(string filePath)
		{
			UnturnedLog.info("Loading Steam inventory from GetInventory response file: {0}", filePath);
			List<SteamGetInventoryResponse.Item> responseItems = SteamGetInventoryResponse.parse(filePath);

			dynamicInventoryDetails.Clear();
			inventoryDetails = new List<SteamItemDetails_t>(responseItems.Count);

			foreach (SteamGetInventoryResponse.Item responseItem in responseItems)
			{
				SteamItemDetails_t fakeItem = new SteamItemDetails_t();
				fakeItem.m_iDefinition = new SteamItemDef_t(responseItem.itemdefid);
				fakeItem.m_itemId = new SteamItemInstanceID_t(responseItem.itemid);
				fakeItem.m_unFlags = 0;
				fakeItem.m_unQuantity = responseItem.quantity;
				inventoryDetails.Add(fakeItem);
			}

			onInventoryRefreshed?.Invoke();
			isInventoryAvailable = true;

			SDG.Unturned.Provider.isLoadingInventory = false;

#if CLIENT_CONSOLIDATE_STACKS
			consolidateStacks();
#endif
		}

		public void refreshInventory()
		{
			UnturnedLog.info("Refreshing Steam inventory");

#if ENABLE_TEST_INVENTORY
			string testFilePath = Path.Join(ReadWrite.PATH, "TestInventory.dat");
			if (File.Exists(testFilePath))
			{
				try
				{
					LoadInventoryFromTestFile(testFilePath);
					return;
				}
				catch (System.Exception e)
				{
					UnturnedLog.exception(e);
				}
			}
#endif // ENABLE_TEST_INVENTORY

			string responseFilePath = Path.Combine(ReadWrite.PATH, "SteamInventory.json");
			if (File.Exists(responseFilePath))
			{
				try
				{
					loadInventoryFromResponseFile(responseFilePath);
				}
				catch (System.Exception e)
				{
					UnturnedLog.exception(e);
				}
			}
			else if (!SteamInventory.GetAllItems(out inventoryResult))
			{
				SDG.Unturned.Provider.isLoadingInventory = false;
			}
		}

		/// <summary>
		/// Add an item locally that we know exists in the online inventory, but is just a matter of waiting for it.
		/// </summary>
		private void addLocalItem(SteamItemDetails_t item, string tags, string dynamic_props)
		{
			inventoryDetails.Add(item);

			// Remove any existing record e.g. prior to attaching stat-counter.
			dynamicInventoryDetails.Remove(item.m_itemId.m_SteamItemInstanceID);

			DynamicEconDetails details = new DynamicEconDetails();
			details.tags = string.IsNullOrEmpty(tags) ? string.Empty : tags;
			details.dynamic_props = string.IsNullOrEmpty(dynamic_props) ? string.Empty : dynamic_props;
			dynamicInventoryDetails.Add(item.m_itemId.m_SteamItemInstanceID, details);
		}

		/// <summary>
		/// Remove an item locally that we know no longer exists in the online inventory.
		/// </summary>
		private void removeLocalItem(SteamItemDetails_t item)
		{
			for (int index = 0; index < inventoryDetails.Count; ++index)
			{
				// Match by unique instance id.
				if (inventoryDetails[index].m_itemId == item.m_itemId)
				{
					inventoryDetails.RemoveAtFast(index);
					break;
				}
			}

			// Remove any dynamic info as well.
			dynamicInventoryDetails.Remove(item.m_itemId.m_SteamItemInstanceID);
		}

		/// <summary>
		/// Update our local version of an item that we know has changed, but we are waiting for a full refresh.
		/// </summary>
		private bool updateLocalItem(SteamItemDetails_t item, SteamInventoryResult_t resultHandle, uint resultIndex)
		{
			// Remove prior to adding new copy.
			removeLocalItem(item);

			bool wasRemovedAccordingToFlags = (item.m_unFlags & ((ushort) ESteamItemFlags.k_ESteamItemRemoved)) != 0;
			bool wasDepletedToZero = item.m_unQuantity < 1;
			if (wasRemovedAccordingToFlags || wasDepletedToZero)
			{
				UnturnedLog.info("Locally removed item - Instance: {0} Definition: {1}", item.m_itemId, item.m_iDefinition);
				return false;
			}

			string tags;
			uint tagsSize = 1024;
			SteamInventory.GetResultItemProperty(resultHandle, resultIndex, "tags", out tags, ref tagsSize);

			string dynamic_props;
			uint dynamic_propsSize = 1024;
			SteamInventory.GetResultItemProperty(resultHandle, resultIndex, "dynamic_props", out dynamic_props, ref dynamic_propsSize);

			addLocalItem(item, tags, dynamic_props);
			UnturnedLog.info("Locally added or updated item - Instance: {0} Definition: {1} Tags: {2} Props: {3}", item.m_itemId, item.m_iDefinition, tags, dynamic_props);
			return true;
		}

		private void handleServerResultReady(SteamInventoryResultReady_t callback)
		{
			SteamPending player = null;

			for (int index = 0; index < SDG.Unturned.Provider.pending.Count; index++)
			{
				if (SDG.Unturned.Provider.pending[index].inventoryResult == callback.m_handle)
				{
					player = SDG.Unturned.Provider.pending[index];
					break;
				}
			}

			if (player == null)
			{
				return;
			}

			if (callback.m_result != EResult.k_EResultOK || !SteamGameServerInventory.CheckResultSteamID(callback.m_handle, player.playerID.steamID))
			{
				UnturnedLog.info("inventory auth: " + callback.m_result + " " + SteamGameServerInventory.CheckResultSteamID(callback.m_handle, player.playerID.steamID));
				SDG.Unturned.Provider.reject(player.playerID.steamID, ESteamRejection.AUTH_ECON_VERIFY);
				return;
			}

			uint size = 0;
			if (SteamGameServerInventory.GetResultItems(callback.m_handle, null, ref size) && size > 0)
			{
				player.inventoryDetails = new SteamItemDetails_t[size];

				SteamGameServerInventory.GetResultItems(callback.m_handle, player.inventoryDetails, ref size);

				for (uint itemIndex = 0; itemIndex < size; itemIndex++)
				{
					string tags;
					uint tagsSize = 1024;
					SteamGameServerInventory.GetResultItemProperty(callback.m_handle, itemIndex, "tags", out tags, ref tagsSize);

					string dynamic_props;
					uint dynamic_propsSize = 1024;
					SteamGameServerInventory.GetResultItemProperty(callback.m_handle, itemIndex, "dynamic_props", out dynamic_props, ref dynamic_propsSize);

					DynamicEconDetails details = new DynamicEconDetails();
					details.tags = string.IsNullOrEmpty(tags) ? string.Empty : tags;
					details.dynamic_props = string.IsNullOrEmpty(dynamic_props) ? string.Empty : dynamic_props;
					player.dynamicInventoryDetails.Add(player.inventoryDetails[itemIndex].m_itemId.m_SteamItemInstanceID, details);
				}
			}

			player.inventoryDetailsReady();
		}

		/// <summary>
		/// Callback when client knows which items were crafted or exchanged.
		/// </summary>
		private void handleClientExchangeResultReady(SteamInventoryResultReady_t callback)
		{
			SteamInventoryResult_t resultHandle = callback.m_handle;

			List<SteamItemDetails_t> grantedItems = new List<SteamItemDetails_t>();

			uint numResultItems = 0;
			if (SteamInventory.GetResultItems(resultHandle, null, ref numResultItems) && numResultItems > 0)
			{
				UnturnedLog.info("Exchange result items: {0}", numResultItems);

				SteamItemDetails_t[] exchangeDetails = new SteamItemDetails_t[numResultItems];
				if (SteamInventory.GetResultItems(resultHandle, exchangeDetails, ref numResultItems))
				{
					for (uint resultIndex = 0; resultIndex < numResultItems; ++resultIndex)
					{
						SteamItemDetails_t itemDetails = exchangeDetails[resultIndex];
						bool bAdded = updateLocalItem(itemDetails, resultHandle, resultIndex);
						if (bAdded)
						{
							grantedItems.Add(itemDetails);
						}
					}
				}
			}

			if (grantedItems.Count > 0)
			{
#if CLIENT_CONSOLIDATE_STACKS
				consolidateStacks();
#endif

				onInventoryExchanged?.Invoke(grantedItems);

				onInventoryRefreshed?.Invoke();
			}
			else
			{
				onInventoryExchangeFailed?.Invoke();
			}
		}

		/// <summary>
		/// Callback when client thinks result was from purchase.
		/// </summary>
		private void handleClientPurchaseResultReady(SteamInventoryResultReady_t callback)
		{
			SteamInventoryResult_t resultHandle = callback.m_handle;

			List<SteamItemDetails_t> grantedItems = new List<SteamItemDetails_t>();

			uint numResultItems = 0;
			if (SteamInventory.GetResultItems(resultHandle, null, ref numResultItems) && numResultItems > 0)
			{
				UnturnedLog.info("Purchase result items: {0}", numResultItems);

				SteamItemDetails_t[] exchangeDetails = new SteamItemDetails_t[numResultItems];
				if (SteamInventory.GetResultItems(resultHandle, exchangeDetails, ref numResultItems))
				{
					for (uint resultIndex = 0; resultIndex < numResultItems; ++resultIndex)
					{
						SteamItemDetails_t itemDetails = exchangeDetails[resultIndex];
						bool bAdded = updateLocalItem(itemDetails, resultHandle, resultIndex);
						if (bAdded)
						{
							grantedItems.Add(itemDetails);
						}
					}
				}
			}

			if (grantedItems.Count > 0)
			{
				onInventoryPurchased(grantedItems);
			}

			onInventoryRefreshed?.Invoke();
		}

		/// <summary>
		/// 2022-01-01 it does not seem to be documented by Steam, but we get SteamInventoryResultReady callbacks
		/// for external events like AddItem calls, so we may as well handle them.
		/// </summary>
		private void UpdateLocalItemsFromUnknownResult(SteamInventoryResult_t resultHandle)
		{
			uint numResultItems = 0;
			if (SteamInventory.GetResultItems(resultHandle, null, ref numResultItems) && numResultItems > 0)
			{
				SteamItemDetails_t[] details = new SteamItemDetails_t[numResultItems];
				if (SteamInventory.GetResultItems(resultHandle, details, ref numResultItems))
				{
					for (uint resultIndex = 0; resultIndex < numResultItems; ++resultIndex)
					{
						updateLocalItem(details[resultIndex], resultHandle, resultIndex);
					}
				}
			}

			onInventoryRefreshed?.Invoke();
		}

		private void DumpInventoryResult(SteamInventoryResult_t handle)
		{
			uint size = 0;
			if (!SteamInventory.GetResultItems(handle, null, ref size))
			{
				UnturnedLog.warn("Unable to get result items length from handle {0}", handle);
				return;
			}

			if (size < 1)
			{
				UnturnedLog.info("Handle {0} result items empty", handle);
				return;
			}

			UnturnedLog.info("Handle {0} contains {1} result item(s)", handle, size);
			SteamItemDetails_t[] resultItems = new SteamItemDetails_t[size];

			if (!SteamInventory.GetResultItems(handle, resultItems, ref size))
			{
				UnturnedLog.warn("Unable to get result items from handle {0}", handle);
				return;
			}

			for (uint index = 0; index < size; ++index)
			{
				SteamItemDetails_t details = resultItems[index];
				UnturnedLog.info("[{0}] Instance: {1} Def: {2} Quantity: {3} Flags: {4}",
					index,
					details.m_itemId,
					details.m_iDefinition,
					details.m_unQuantity,
					(ESteamItemFlags) details.m_unFlags);
			}
		}

		private void handleClientResultReady(SteamInventoryResultReady_t callback)
		{
			if (promoResult != SteamInventoryResult_t.Invalid && callback.m_handle == promoResult)
			{
				SteamInventory.DestroyResult(promoResult);
				promoResult = SteamInventoryResult_t.Invalid;
			}
			else if (exchangeResult != SteamInventoryResult_t.Invalid && callback.m_handle == exchangeResult)
			{
				handleClientExchangeResultReady(callback);

				SteamInventory.DestroyResult(exchangeResult);
				exchangeResult = SteamInventoryResult_t.Invalid;
			}
			else if (dropResult != SteamInventoryResult_t.Invalid && callback.m_handle == dropResult)
			{
				SteamItemDetails_t[] dropDetails = null;
				string tags = null;
				string dynamic_props = null;

				uint size = 0;
				if (SteamInventory.GetResultItems(dropResult, null, ref size) && size > 0)
				{
					dropDetails = new SteamItemDetails_t[size];

					SteamInventory.GetResultItems(dropResult, dropDetails, ref size);

					uint tagsSize = 1024;
					SteamInventory.GetResultItemProperty(dropResult, 0, "tags", out tags, ref tagsSize);

					uint dynamic_propsSize = 1024;
					SteamInventory.GetResultItemProperty(dropResult, 0, "dynamic_props", out dynamic_props, ref dynamic_propsSize);
				}

				UnturnedLog.info("onInventoryResultReady: Drop {0}", size);

#if CLIENT_CONSOLIDATE_STACKS
				consolidateStacks();
#endif

				if (dropDetails != null && size > 0)
				{
					SteamItemDetails_t newItem = dropDetails[0];
					addLocalItem(newItem, tags, dynamic_props);

					onInventoryDropped?.Invoke(newItem.m_iDefinition.m_SteamItemDef, newItem.m_unQuantity, newItem.m_itemId.m_SteamItemInstanceID);

					onInventoryRefreshed?.Invoke();
				}

				SteamInventory.DestroyResult(dropResult);
				dropResult = SteamInventoryResult_t.Invalid;
			}
			else if (wearingResult != SteamInventoryResult_t.Invalid && callback.m_handle == wearingResult)
			{
				// Do nothing. Provider class uses and destroys later.
			}
			else if (inventoryResult != SteamInventoryResult_t.Invalid && callback.m_handle == inventoryResult)
			{
				dynamicInventoryDetails.Clear();
				uint size = 0;
				if (SteamInventory.GetResultItems(inventoryResult, null, ref size) && size > 0)
				{
					SteamItemDetails_t[] newInventoryDetails = new SteamItemDetails_t[size];

					SteamInventory.GetResultItems(inventoryResult, newInventoryDetails, ref size);

					for (uint itemIndex = 0; itemIndex < size; ++itemIndex)
					{
						string tags;
						uint tagsSize = 1024;
						SteamInventory.GetResultItemProperty(inventoryResult, itemIndex, "tags", out tags, ref tagsSize);

						string dynamic_props;
						uint dynamic_propsSize = 1024;
						SteamInventory.GetResultItemProperty(inventoryResult, itemIndex, "dynamic_props", out dynamic_props, ref dynamic_propsSize);

						DynamicEconDetails details = new DynamicEconDetails();
						details.tags = string.IsNullOrEmpty(tags) ? string.Empty : tags;
						details.dynamic_props = string.IsNullOrEmpty(dynamic_props) ? string.Empty : dynamic_props;
						dynamicInventoryDetails.Add(newInventoryDetails[itemIndex].m_itemId.m_SteamItemInstanceID, details);

						if (newInventoryDetails[itemIndex].m_unQuantity < 1)
						{
							string quantityString;
							uint quantityStringSize = 64;
							if (SteamInventory.GetResultItemProperty(inventoryResult, itemIndex, "quantity", out quantityString, ref quantityStringSize))
							{
								ulong actualQuantity;
								if (ulong.TryParse(quantityString, out actualQuantity))
								{
									ushort clampedQuantity;
									if (actualQuantity > ushort.MaxValue)
									{
										clampedQuantity = ushort.MaxValue;
									}
									else
									{
										clampedQuantity = (ushort) actualQuantity;
									}

									UnturnedLog.info($"Used quantity string fallback for itemdefid {newInventoryDetails[itemIndex].m_iDefinition} (actual: {actualQuantity} clamped: {clampedQuantity})");
								}
								else
								{
									UnturnedLog.warn($"Tried using quantity string fallback for itemdefid {newInventoryDetails[itemIndex].m_iDefinition} but unable to parse \"{quantityString}\"");
								}
							}
							else
							{
								UnturnedLog.warn($"Tried using quantity string fallback for itemdefid {newInventoryDetails[itemIndex].m_iDefinition} but GetResultItemProperty returned false");
							}
						}
					}

					inventoryDetails = new List<SteamItemDetails_t>(newInventoryDetails);
				}

#if CLIENT_CONSOLIDATE_STACKS
				consolidateStacks();
#endif

				onInventoryRefreshed?.Invoke();
				isInventoryAvailable = true;

				SDG.Unturned.Provider.isLoadingInventory = false;

				SteamInventory.DestroyResult(inventoryResult);
				inventoryResult = SteamInventoryResult_t.Invalid;
			}
			else if (commitResult != SteamInventoryResult_t.Invalid && callback.m_handle == commitResult)
			{
				UnturnedLog.info("Commit dynamic properties result: " + callback.m_result);

				SteamInventory.DestroyResult(commitResult);
				commitResult = SteamInventoryResult_t.Invalid;
			}
#if CLIENT_CONSOLIDATE
			else if(stackResult != SteamInventoryResult_t.Invalid && callback.m_handle == stackResult)
			{
				UnturnedLog.info("Stack result: " + callback.m_result);

				bool modifiedLocalInventory = false;

				// Result items should contain the source item with quantity 0, and the destination item with final quantity.
				uint size = 0;
				if(SteamInventory.GetResultItems(stackResult, null, ref size) && size > 0)
				{
					SteamItemDetails_t[] stackedItems = new SteamItemDetails_t[size];
					if(SteamInventory.GetResultItems(stackResult, stackedItems, ref size))
					{
						for(uint resultIndex = 0; resultIndex < size; ++resultIndex)
						{
							SteamItemDetails_t itemDetails = stackedItems[resultIndex];
							updateLocalItem(itemDetails, stackResult, resultIndex);
						}

						modifiedLocalInventory = true;
					}
				}

				if(onInventoryRefreshed != null && modifiedLocalInventory)
				{
					onInventoryRefreshed();
				}

				SteamInventory.DestroyResult(stackResult);
				stackResult = SteamInventoryResult_t.Invalid;
			}
#endif // CLIENT_CONSOLIDATE
			else if (isExpectingPurchaseResult)
			{
				isExpectingPurchaseResult = false;
				handleClientPurchaseResultReady(callback);
				SteamInventory.DestroyResult(callback.m_handle);
			}
			else
			{
				UnturnedLog.info("Unexpected client inventory result ready callback  - Handle: {0} Result: {1}", callback.m_handle, callback.m_result);
				UpdateLocalItemsFromUnknownResult(callback.m_handle);
				DumpInventoryResult(callback.m_handle);
				SteamInventory.DestroyResult(callback.m_handle);
			}
		}

#pragma warning disable
		private Callback<SteamInventoryResultReady_t> inventoryResultReady;
#pragma warning restore
		private void onInventoryResultReady(SteamInventoryResultReady_t callback)
		{
			UnityEngine.Profiling.Profiler.BeginSample("onInventoryResultReady");
			if (appInfo.isDedicated)
			{
				handleServerResultReady(callback);
			}
			else
			{
				handleClientResultReady(callback);
			}
			UnityEngine.Profiling.Profiler.EndSample();
		}

		public void loadTranslationEconInfo()
		{
			if (SDG.Unturned.Provider.language == "English")
			{
				// Default values are English.
				return;
			}

			string translationEconInfoPath = SDG.Unturned.Provider.localizationRoot + "/EconInfo.json";
			if (!ReadWrite.fileExists(translationEconInfoPath, false, false))
			{
				UnturnedLog.warn("Looked for economy translation at: {0}", translationEconInfoPath);
				return;
			}

			SDG.Framework.IO.Deserialization.IDeserializer deserializer = new SDG.Framework.IO.Deserialization.JSONDeserializer();
			List<UnturnedEconInfo> translationEconInfo = new List<UnturnedEconInfo>();
			translationEconInfo = deserializer.deserialize<List<UnturnedEconInfo>>(translationEconInfoPath);

			foreach (UnturnedEconInfo translatedItem in translationEconInfo)
			{
				UnturnedEconInfo untranslatedItem = FindEconInfo(translatedItem.itemdefid);
				if (untranslatedItem != null)
				{
					untranslatedItem.name = translatedItem.name;
					untranslatedItem.display_type = translatedItem.display_type;
					untranslatedItem.description = translatedItem.description;
				}
			}
		}

		/// <summary>
		/// Do we know the player's region?
		/// If not, default to not allowing random items.
		/// </summary>
		public bool hasCountryDetails
		{
			get;
			protected set;
		}

		/// <summary>
		/// Does the player's region allow crates and keys to be used?
		/// Similar to TF2 and other Valve games we disable unboxing in certain regions.
		/// </summary>
		public bool doesCountryAllowRandomItems
		{
			get;
			protected set;
		}

		/// <summary>
		/// If player's region does not allow crates and keys to be used, return the country code.
		/// </summary>
		public string getCountryWarningId()
		{
			return SteamUtils.GetIPCountry(); // 2 digit ISO 3166-1-alpha-2 format country code which client is running in.
		}

		/// <summary>
		/// Similar to TF2 and other Valve games we disable unboxing in certain regions, so hide those items.
		/// </summary>
		public bool isItemHiddenByCountryRestrictions(int itemdefid)
		{
			if (doesCountryAllowRandomItems)
			{
				// Allows unrestricted items to use the same code path, avoiding bugs.
				return false;
			}

			System.Guid gameItemId = getInventoryItemGuid(itemdefid);
			ItemAsset asset = Assets.find<ItemAsset>(gameItemId);
			if (asset != null)
			{
				return asset.type == EItemType.KEY || asset.type == EItemType.BOX;
			}
			else
			{
				// Probably a bundle of items.
				return false;
			}
		}

		/// <summary>
		/// Similar to TF2 and other Valve games we disable unboxing in certain regions.
		/// </summary>
		private void initCountryRestrictions()
		{
			string regionCode = SteamUtils.GetIPCountry(); // 2 digit ISO 3166-1-alpha-2 format country code which client is running in.
			if (string.IsNullOrWhiteSpace(regionCode))
			{
				// Default to not showing random items in case Steam is unable to determine country.
				hasCountryDetails = false;
				doesCountryAllowRandomItems = false;
				UnturnedLog.warn("Unable to determine country/region");
			}
			else
			{
				hasCountryDetails = true;
				doesCountryAllowRandomItems = true;
				doesCountryAllowRandomItems &= !string.Equals(regionCode, "BE"); // Belgium
				doesCountryAllowRandomItems &= !string.Equals(regionCode, "NL"); // Netherlands
																				 //UnturnedLog.info("Region \"{0}\" allows random items: {1}", regionCode, doesCountryAllowRandomItems);
			}
		}

		/// <summary>
		/// Not called on dedicated server.
		/// </summary>
		public void initializeClient()
		{
			initCountryRestrictions();
		}

		/// <summary>
		/// Moved out of constructor so that it can access Provider.steamAppInstallDirectory.
		/// </summary>
		public void initialize()
		{
			string econInfoPath;
			if (UnityPaths.ProjectDirectory != null)
			{
				// Running builds from the project (e.g. development builds) do not have a copy of the non-Unity assets.
#if EXPERIMENTAL
				econInfoPath = PathEx.Join(UnityPaths.ProjectDirectory, "Builds", "Shared_Experimental", "EconInfo.bin");
#else
				econInfoPath = PathEx.Join(UnityPaths.ProjectDirectory, "Builds", "Shared_Release", "EconInfo.bin");
#endif
			}
			else
			{
				econInfoPath = PathEx.Join(UnturnedPaths.RootDirectory, "EconInfo.bin");
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST
			if (!File.Exists(econInfoPath) && SDG.Unturned.Provider.steamAppInstallDirectory != null)
			{
				econInfoPath = PathEx.Join(SDG.Unturned.Provider.steamAppInstallDirectory, "EconInfo.bin");
			}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST

			econInfo = new Dictionary<int, UnturnedEconInfo>();
			bundleContents = new Dictionary<int, List<int>>();

			try
			{
				using (FileStream fileStream = new FileStream(econInfoPath, FileMode.Open, FileAccess.Read, FileShare.Read))
				using (SHA1Stream hashStream = new SHA1Stream(fileStream))
				{
					const int VERSION_ADDED_CREATION_TIME = 2;
					const int VERSION_ADDED_ELIGIBLE_FOR_PROMOTION = 3;
					const int VERSION_NEWEST = VERSION_ADDED_ELIGIBLE_FOR_PROMOTION;

					using (BinaryReader binaryReader = new BinaryReader(fileStream))
					{
						int version = binaryReader.ReadInt32();
						if (version <= VERSION_NEWEST)
						{
							int itemCount = binaryReader.ReadInt32();
							for (int itemIndex = 0; itemIndex < itemCount; ++itemIndex)
							{
								UnturnedEconInfo info = new UnturnedEconInfo();
								info.name = binaryReader.ReadString();
								info.display_type = binaryReader.ReadString();
								info.description = binaryReader.ReadString();
								info.name_color = binaryReader.ReadString();
								info.itemdefid = binaryReader.ReadInt32();
								info.marketable = binaryReader.ReadBoolean();
								info.scraps = binaryReader.ReadInt32();
								info.target_game_asset_guid = new System.Guid(binaryReader.ReadBytes(16));
								info.item_skin = binaryReader.ReadInt32();
								info.item_effect = binaryReader.ReadInt32();
								info.quality = (UnturnedEconInfo.EQuality) binaryReader.ReadInt32();
								info.econ_type = binaryReader.ReadInt32();
								if (version >= VERSION_ADDED_CREATION_TIME)
								{
									info.creationTimeUtc = System.DateTime.FromBinary(binaryReader.ReadInt64());
								}
								if (version >= VERSION_ADDED_ELIGIBLE_FOR_PROMOTION)
								{
									info.isEligibleForPromotion = binaryReader.ReadBoolean();
								}
								else
								{
									info.isEligibleForPromotion = true;
								}
								if (!econInfo.TryAdd(info.itemdefid, info))
								{
									UnturnedLog.error($"Duplicate itemdefid {info.itemdefid} name: \"{info.name}\"");
								}
							}

							int bundleCount = binaryReader.ReadInt32();
							for (int bundleIndex = 0; bundleIndex < bundleCount; ++bundleIndex)
							{
								int bundleItemDefId = binaryReader.ReadInt32();
								int containedItemCount = binaryReader.ReadInt32();
								List<int> containedItemDefIds = new List<int>(containedItemCount);
								for (int containedItemIndex = 0; containedItemIndex < containedItemCount; ++containedItemIndex)
								{
									int containedItemDefId = binaryReader.ReadInt32();
									containedItemDefIds.Add(containedItemDefId);
								}
								if (!bundleContents.TryAdd(bundleItemDefId, containedItemDefIds))
								{
									UnturnedLog.error($"Duplicate bundle contents itemdefid {bundleItemDefId}");
								}
							}
						}
						else
						{
							UnturnedLog.warn($"Unable to load future EconInfo.bin version ({version})");
						}
					}
					econInfoHash = hashStream.Hash;
				}
			}
			catch (System.Exception exception)
			{
				UnturnedLog.exception(exception, $"Caught exception loading EconInfo.bin:");
			}

			if (appInfo.isDedicated)
			{
				inventoryResultReady = Callback<SteamInventoryResultReady_t>.CreateGameServer(onInventoryResultReady);
			}
			else
			{
				inventoryResultReady = Callback<SteamInventoryResultReady_t>.Create(onInventoryResultReady);
			}
		}

		public TempSteamworksEconomy(SDG.SteamworksProvider.SteamworksAppInfo newAppInfo)
		{
			appInfo = newAppInfo;
		}
	}
}
