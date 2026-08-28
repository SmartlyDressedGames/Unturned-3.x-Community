////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using SDG.Provider.Services;
using SDG.Provider.Services.Economy;
using Steamworks;
using System.Collections.Generic;

namespace SDG.SteamworksProvider.Services.Economy
{
	public class SteamworksEconomyService : Service, IEconomyService
	{
		public bool canOpenInventory => true;// SteamUtils.IsOverlayEnabled();

		private List<SteamworksEconomyRequestHandle> steamworksEconomyRequestHandles;

		public IEconomyRequestHandle requestInventory(EconomyRequestReadyCallback inventoryRequestReadyCallback)
		{
			SteamInventoryResult_t steamInventoryResult;
			SteamInventory.GetAllItems(out steamInventoryResult);

			IEconomyRequestHandle inventoryRequestHandle = addInventoryRequestHandle(steamInventoryResult, inventoryRequestReadyCallback);
			return inventoryRequestHandle;
		}

		public IEconomyRequestHandle requestPromo(EconomyRequestReadyCallback inventoryRequestReadyCallback)
		{
			SteamInventoryResult_t steamInventoryResult;
			SteamInventory.GrantPromoItems(out steamInventoryResult);

			IEconomyRequestHandle inventoryRequestHandle = addInventoryRequestHandle(steamInventoryResult, inventoryRequestReadyCallback);
			return inventoryRequestHandle;
		}

		public IEconomyRequestHandle exchangeItems(IEconomyItemInstance[] inputItemInstanceIDs, uint[] inputItemQuantities, IEconomyItemDefinition[] outputItemDefinitionIDs, uint[] outputItemQuantities, EconomyRequestReadyCallback inventoryRequestReadyCallback)
		{
			if (inputItemInstanceIDs.Length != inputItemQuantities.Length)
			{
				throw new System.ArgumentException("Input item arrays need to be the same length.", "inputItemQuantities");
			}

			if (outputItemDefinitionIDs.Length != outputItemQuantities.Length)
			{
				throw new System.ArgumentException("Output item arrays need to be the same length.", "outputItemQuantities");
			}

			SteamItemInstanceID_t[] inputSteamItemInstanceIDs = new SteamItemInstanceID_t[inputItemInstanceIDs.Length];
			for (int inputItemInstanceIDIndex = 0; inputItemInstanceIDIndex < inputItemInstanceIDs.Length; inputItemInstanceIDIndex++)
			{
				SteamworksEconomyItemInstance steamworksItemInstanceID = (SteamworksEconomyItemInstance) inputItemInstanceIDs[inputItemInstanceIDIndex];

				inputSteamItemInstanceIDs[inputItemInstanceIDIndex] = steamworksItemInstanceID.steamItemInstanceID;
			}

			SteamItemDef_t[] outputSteamItemDefs = new SteamItemDef_t[outputItemDefinitionIDs.Length];
			for (int outputItemDefinitionIDIndex = 0; outputItemDefinitionIDIndex < outputItemDefinitionIDs.Length; outputItemDefinitionIDIndex++)
			{
				SteamworksEconomyItemDefinition steamworksItemDefinitionID = (SteamworksEconomyItemDefinition) outputItemDefinitionIDs[outputItemDefinitionIDIndex];

				outputSteamItemDefs[outputItemDefinitionIDIndex] = steamworksItemDefinitionID.steamItemDef;
			}

			SteamInventoryResult_t steamInventoryResult;
			SteamInventory.ExchangeItems(out steamInventoryResult, outputSteamItemDefs, outputItemQuantities, (uint) outputSteamItemDefs.Length, inputSteamItemInstanceIDs, inputItemQuantities, (uint) inputSteamItemInstanceIDs.Length);

			IEconomyRequestHandle inventoryRequestHandle = addInventoryRequestHandle(steamInventoryResult, inventoryRequestReadyCallback);
			return inventoryRequestHandle;
		}

		public void open(ulong id)
		{
			SDG.Unturned.WebUtils.OpenURL("https://steamcommunity.com/profiles/" + SteamUser.GetSteamID() + "/inventory/?sellOnLoad=1#" + SteamUtils.GetAppID() + "_2_" + id);
		}

		private SteamworksEconomyRequestHandle findSteamworksEconomyRequestHandles(SteamInventoryResult_t steamInventoryResult)
		{
			return steamworksEconomyRequestHandles.Find(handle => handle.steamInventoryResult == steamInventoryResult);
		}

		private IEconomyRequestHandle addInventoryRequestHandle(SteamInventoryResult_t steamInventoryResult, EconomyRequestReadyCallback inventoryRequestReadyCallback)
		{
			SteamworksEconomyRequestHandle inventoryRequestHandle = new SteamworksEconomyRequestHandle(steamInventoryResult, inventoryRequestReadyCallback);
			steamworksEconomyRequestHandles.Add(inventoryRequestHandle);

			return inventoryRequestHandle;
		}

		private IEconomyRequestResult createInventoryRequestResult(SteamInventoryResult_t steamInventoryResult)
		{
			SteamworksEconomyItem[] steamworksItems;

			uint steamItemDetailsLength = 0;
			if (SteamGameServerInventory.GetResultItems(steamInventoryResult, null, ref steamItemDetailsLength) && steamItemDetailsLength > 0)
			{
				SteamItemDetails_t[] steamItemDetails = new SteamItemDetails_t[steamItemDetailsLength];
				SteamGameServerInventory.GetResultItems(steamInventoryResult, steamItemDetails, ref steamItemDetailsLength);

				steamworksItems = new SteamworksEconomyItem[steamItemDetailsLength];
				for (uint steamworksItemIndex = 0; steamworksItemIndex < steamItemDetailsLength; steamworksItemIndex++)
				{
					SteamItemDetails_t steamItemDetail = steamItemDetails[steamworksItemIndex];
					SteamworksEconomyItem steamworksItem = new SteamworksEconomyItem(steamItemDetail);

					steamworksItems[steamworksItemIndex] = steamworksItem;
				}
			}
			else
			{
				steamworksItems = new SteamworksEconomyItem[0];
			}

			IEconomyRequestResult inventoryRequestResult = new EconomyRequestResult(EEconomyRequestState.SUCCESS, steamworksItems);
			return inventoryRequestResult;
		}

#pragma warning disable
		private Callback<SteamInventoryResultReady_t> steamInventoryResultReady;
#pragma warning restore
		private void onSteamInventoryResultReady(SteamInventoryResultReady_t callback)
		{
			SteamworksEconomyRequestHandle steamworksInventoryRequestHandle = this.findSteamworksEconomyRequestHandles(callback.m_handle);

			if (steamworksInventoryRequestHandle == null)
			{
				return;
			}

			UnityEngine.Profiling.Profiler.BeginSample("onSteamInventoryResultReady");
			IEconomyRequestResult inventoryRequestResult = createInventoryRequestResult(steamworksInventoryRequestHandle.steamInventoryResult);
			steamworksInventoryRequestHandle.triggerInventoryRequestReadyCallback(inventoryRequestResult);

			SteamInventory.DestroyResult(steamworksInventoryRequestHandle.steamInventoryResult);
			UnityEngine.Profiling.Profiler.EndSample();
		}

		public SteamworksEconomyService() : base()
		{
			steamworksEconomyRequestHandles = new List<SteamworksEconomyRequestHandle>();
			steamInventoryResultReady = Callback<SteamInventoryResultReady_t>.Create(onSteamInventoryResultReady);
		}
	}
}
