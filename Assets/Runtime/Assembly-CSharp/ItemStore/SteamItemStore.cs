////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using SDG.Provider;
using Steamworks;
using System.Collections.Generic;
using System.Globalization;

namespace SDG.Unturned
{
	internal class SteamItemStore : ItemStore
	{
		public const string NEW_ITEM_PROMOTION_KEY = "NewItemSeenPromotionId";

		public override event System.Action OnPricesReceived;
		public override event System.Action<EPurchaseResult> OnPurchaseResult;

		public override void ViewItem(int itemdefid)
		{
			if (listings != null && listings.Length > 0)
			{
				Listing listing;
				if (FindListing(itemdefid, out listing))
				{
					if (IsOverlayEnabledForCheckout)
					{
						UnturnedLog.info("Item store has listing for itemdefid {0}, using in-game menu", itemdefid);
						MenuUI.closeAll();
						ItemStoreDetailsMenu.instance.Open(listing);
						return;
					}
					else
					{
						UnturnedLog.warn("Would not be able to checkout because Steam overlay is disabled, using browser");
					}
				}
				else
				{
					UnturnedLog.warn("Item store does not have a listing for itemdefid {0}, using browser", itemdefid);
				}
			}
			else
			{
				UnturnedLog.warn("Item store unavailable for itemdefid {0}, using browser", itemdefid);
			}

			// Uses the Steam overlay if available, otherwise default system web browser.
			WebUtils.OpenURL("https://store.steampowered.com/itemstore/" + Provider.APP_ID + "/detail/" + itemdefid);
		}

		public override void ViewNewItems()
		{
			if (HasNewListings)
			{
				if (IsOverlayEnabledForCheckout)
				{
					MenuUI.closeAll();
					ItemStoreMenu.instance.OpenNewItems();
					return;
				}
				else
				{
					UnturnedLog.warn("Would not be able to checkout because Steam overlay is disabled, using browser");
				}
			}
			else
			{
				UnturnedLog.warn("Item store does not have listings for new items, using browser");
			}

			// Uses the Steam overlay if available, otherwise default system web browser.
			WebUtils.OpenURL("https://store.steampowered.com/itemstore/" + Provider.APP_ID + "/browse/?filter=New");
		}

		public override void ViewStore()
		{
			if (listings != null && listings.Length > 0)
			{
				if (IsOverlayEnabledForCheckout)
				{
					MenuUI.closeAll();
					ItemStoreMenu.instance.Open();
					return;
				}
				else
				{
					UnturnedLog.warn("Would not be able to checkout because Steam overlay is disabled, using browser");
				}
			}
			else
			{
				UnturnedLog.warn("Item store unavailable, using browser");
			}

			// Uses the Steam overlay if available, otherwise default system web browser.
			WebUtils.OpenURL("https://store.steampowered.com/itemstore/" + Provider.APP_ID);
		}

		public override void RequestPrices()
		{
			UnturnedLog.info("Requesting Steam item store prices");
			SteamAPICall_t handle = SteamInventory.RequestPrices();
			if (handle != SteamAPICall_t.Invalid)
			{
				requestPricesCallResult.Set(handle);
			}
			else
			{
				UnturnedLog.info("Steam internal problem requesting item store prices");
			}
		}

		public override void StartPurchase()
		{
			if (IsCartEmpty)
				throw new System.Exception("should not have been called with an empty cart");

			uint arrayLength = (uint) itemsInCart.Count;
			UnturnedLog.info("Requesting purchase of {0} item(s)", arrayLength);
			SteamItemDef_t[] arrayItemDefs = new SteamItemDef_t[arrayLength];
			uint[] arrayQuantity = new uint[arrayLength];
			for (int index = 0; index < arrayLength; ++index)
			{
				CartEntry entry = itemsInCart[index];
				arrayItemDefs[index] = new SteamItemDef_t(entry.itemdefid);
				arrayQuantity[index] = (uint) entry.quantity;
				UnturnedLog.info("[{0}]: {1} x {2}", index, entry.itemdefid, entry.quantity);
			}

			itemsInCart.Clear();

			SteamAPICall_t handle = SteamInventory.StartPurchase(arrayItemDefs, arrayQuantity, arrayLength);
			if (handle != SteamAPICall_t.Invalid)
			{
				startPurchaseCallResult.Set(handle);
			}
			else
			{
				UnturnedLog.info("Start purchase invalid input");
				OnPurchaseResult?.Invoke(EPurchaseResult.UnableToInitialize);
			}
		}

		/// <summary>
		/// Steam currency codes seem to be ISO 4217, however the documentation (as of 2021-01-29) does not say so.
		/// </summary>
		private NumberFormatInfo GetCurrencyFormatInfo(string threeLetterCode)
		{
			try
			{
				// Prefer default culture if possible. For example as a Canadian the search might find French Canadian
				// before English Canadian which has different formatting rules.
				CultureInfo currentCulture = CultureInfo.CurrentCulture;
				if (currentCulture != null)
				{
					// Should be same as RegionInfo.CurrentRegion, but we need the reverse (culture from region).
					RegionInfo currentRegion = new RegionInfo(currentCulture.LCID);
					if (string.Equals(currentRegion.ISOCurrencySymbol, threeLetterCode, System.StringComparison.OrdinalIgnoreCase))
					{
						UnturnedLog.info("Item store using current culture {0} for Steam currency code {1}", currentCulture.DisplayName, threeLetterCode);
						return currentCulture.NumberFormat;
					}
				}

				CultureInfo[] allCultures = CultureInfo.GetCultures(CultureTypes.SpecificCultures);
				foreach (CultureInfo culture in allCultures)
				{
					RegionInfo region = new RegionInfo(culture.LCID);
					if (string.Equals(region.ISOCurrencySymbol, threeLetterCode, System.StringComparison.OrdinalIgnoreCase))
					{
						UnturnedLog.info("Item store using fallback culture {0} for Steam currency code {1}", culture.DisplayName, threeLetterCode);
						return culture.NumberFormat;
					}
				}
			}
			catch (System.Exception e)
			{
				// Unfamiliar with the culture APIs, so who knows what might happen.
				UnturnedLog.exception(e, "Exception trying to find region for Steam currency code {0}:", threeLetterCode);
			}

			// Caller logs an error. Item store will be disabled.
			return null;
		}


		private void OnRequestPricesResultReady(SteamInventoryRequestPricesResult_t result, bool ioFailure)
		{
			if (ioFailure || result.m_result != EResult.k_EResultOK)
			{
				UnturnedLog.error("Request prices result: {0} I/O Failure: {1}", result.m_result, ioFailure);
				return;
			}

			numberFormatInfo = GetCurrencyFormatInfo(result.m_rgchCurrency);
			if (numberFormatInfo == null)
			{
				UnturnedLog.error("Unable to find currency format info for Steam currency code: {0}", result.m_rgchCurrency);
				return;
			}

			uint numItemsWithPrices = SteamInventory.GetNumItemsWithPrices();
			if (numItemsWithPrices < 1)
			{
				UnturnedLog.error("Steam returned zero items with prices");
				return;
			}

			SteamItemDef_t[] itemDefsWithPrices = new SteamItemDef_t[numItemsWithPrices];
			ulong[] currentPrices = new ulong[numItemsWithPrices];
			ulong[] basePrices = new ulong[numItemsWithPrices];
			if (!SteamInventory.GetItemsWithPrices(itemDefsWithPrices, currentPrices, basePrices, numItemsWithPrices))
			{
				UnturnedLog.error("Unable to get items with prices");
				return;
			}

			List<Listing> pendingListings = new List<Listing>((int) numItemsWithPrices);
			List<int> pendingDiscountedListings = new List<int>();
			for (uint index = 0; index < numItemsWithPrices; ++index)
			{
				int itemdefid = itemDefsWithPrices[index].m_SteamItemDef;

				if (!Provider.provider.economyService.IsItemKnown(itemdefid))
				{
					UnturnedLog.warn("Item store missing details for itemdefid {0}", itemdefid);
					continue;
				}

				/*UnturnedLog.info("{0} Current Price: {1} ({2}) Base Price: {3} ({4})",
					Provider.provider.economyService.getInventoryName(itemdefid),
					(currentPrices[index] / 100.0).ToString("C", numberFormatInfo),
					currentPrices[index],
					(basePrices[index] / 100.0).ToString("C", numberFormatInfo),
					basePrices[index]);*/

				if (Provider.provider.economyService.isItemHiddenByCountryRestrictions(itemdefid))
				{
					UnturnedLog.info("Item store hiding \"{0}\" due to country restrictions", Provider.provider.economyService.getInventoryName(itemdefid));
					continue;
				}

				Listing listing = new Listing();
				listing.itemdefid = itemdefid;
				listing.currentPrice = currentPrices[index];
				listing.basePrice = basePrices[index];
				int listingIndex = pendingListings.Count;
				pendingListings.Add(listing);
				if (listing.currentPrice < listing.basePrice)
				{
					pendingDiscountedListings.Add(listingIndex);
				}
			}

			if (pendingListings.Count < 1)
			{
				UnturnedLog.error("Item store has no valid listings");
				return;
			}

			listings = pendingListings.ToArray();
			discountedListingIndices = pendingDiscountedListings.ToArray();

			int numberOfDiscountedItems = HasDiscountedListings ? GetDiscountedListingIndices().Length : 0;
			int totalNumberOfItems = GetListings().Length;
			UnturnedLog.info($"Received Steam item store prices - Discounted: {numberOfDiscountedItems} All: {totalNumberOfItems}");

			if (!Provider.provider.economyService.isInventoryAvailable)
			{
				isWaitingForSteamInventory = true;
				Provider.provider.economyService.onInventoryRefreshed += OnInventoryRefreshed;
			}

#if !DEDICATED_SERVER
			if (!LiveConfig.WasPopulated)
			{
				isWaitingForLiveConfig = true;
				LiveConfig.OnRefreshed += OnLiveConfigRefreshed;
			}
#endif // !DEDICATED_SERVER

			MaybeFinalizePricesReceived();
		}

		private void OnStartPurchaseResultReady(SteamInventoryStartPurchaseResult_t result, bool ioFailure)
		{
			if (ioFailure || result.m_result != EResult.k_EResultOK)
			{
				UnturnedLog.error("Start purchase result: {0} I/O Failure: {1}", result.m_result, ioFailure);
				OnPurchaseResult?.Invoke(EPurchaseResult.UnableToInitialize);
			}
			else
			{
				UnturnedLog.info("Start purchase Order ID: {0} Transaction ID: {1}", result.m_ulOrderID, result.m_ulTransID);
				Provider.provider.economyService.isExpectingPurchaseResult = true;
			}
		}

		private void OnMicroTxnAuthorizationResponse(MicroTxnAuthorizationResponse_t responseData)
		{
			if (responseData.m_unAppID != Provider.APP_ID.m_AppId)
				return; // Not for us.

			if (responseData.m_bAuthorized > 0)
			{
				UnturnedLog.info("Purchase authorized Order ID: {0}", responseData.m_ulOrderID);
				// In this case OnInventoryResultReady will be called with an unknown handle,
				// so we guess according to isExpectingPurchaseResult.
			}
			else
			{
				Provider.provider.economyService.isExpectingPurchaseResult = false;
				UnturnedLog.info("Purchase denied Order ID: {0}", responseData.m_ulOrderID);
				OnPurchaseResult?.Invoke(EPurchaseResult.Denied);
			}
		}

		private void OnInventoryRefreshed()
		{
			Provider.provider.economyService.onInventoryRefreshed -= OnInventoryRefreshed;
			isWaitingForSteamInventory = false;
			MaybeFinalizePricesReceived();
		}

#if !DEDICATED_SERVER
		private void OnLiveConfigRefreshed()
		{
			LiveConfig.OnRefreshed -= OnLiveConfigRefreshed;
			isWaitingForLiveConfig = false;
			MaybeFinalizePricesReceived();
		}
#endif // !DEDICATED_SERVER

		private void RefreshOwnedItems()
		{
			HashSet<int> ownedItemDefIds = Provider.provider.economyService.GatherOwnedItemDefIds();

			List<int> pendingIndices = new List<int>();

			if (discountedListingIndices != null && discountedListingIndices.Length > 0)
			{
				foreach (int discountedListingIndex in discountedListingIndices)
				{
					Listing listing = listings[discountedListingIndex];
					if (!Provider.provider.economyService.IsItemBundle(listing.itemdefid))
						continue;

					List<int> containedItemDefIds = Provider.provider.economyService.GetBundleContents(listing.itemdefid);
					if (containedItemDefIds == null || containedItemDefIds.Count < 1)
						continue;

					bool ownsAnyItemInBundle = false;
					foreach (int containedItemDefId in containedItemDefIds)
					{
						if (ownedItemDefIds.Contains(containedItemDefId))
						{
							ownsAnyItemInBundle = true;
							break;
						}
					}

					//UnturnedLog.info($"Owns bundle {Provider.provider.economyService.getInventoryName(listing.itemdefid)}: {ownsAnyItemInBundle}");
					if (!ownsAnyItemInBundle)
					{
						pendingIndices.Add(discountedListingIndex);
					}
				}
			}

			unownedDiscountedBundleListingIndices = pendingIndices.ToArray();
		}

		private void MaybeFinalizePricesReceived()
		{
			if (isWaitingForSteamInventory || isWaitingForLiveConfig)
				return;

			RefreshNewItems();
			RefreshFeaturedItems();
			RefreshExcludedItems();
			RefreshOwnedItems();

			int numberOfNewItems = HasNewListings ? GetNewListingIndices().Length : 0;
			int numberOfFeaturedItems = HasFeaturedListings ? GetFeaturedListingIndices().Length : 0;
			UnturnedLog.info($"Received Steam item store live config - New: {numberOfNewItems} Featured: {numberOfFeaturedItems}");

			try
			{
				OnPricesReceived?.Invoke();
			}
			catch (System.Exception exception)
			{
				// Don't want to risk breaking UI if there's a bug in the store.
				UnturnedLog.exception(exception, "Caught exception during OnPricesReceived event:");
			}
		}

		public SteamItemStore()
		{
			requestPricesCallResult = CallResult<SteamInventoryRequestPricesResult_t>.Create(OnRequestPricesResultReady);
			startPurchaseCallResult = CallResult<SteamInventoryStartPurchaseResult_t>.Create(OnStartPurchaseResultReady);
			microTxnAuthCallback = Callback<MicroTxnAuthorizationResponse_t>.Create(OnMicroTxnAuthorizationResponse);
		}

		/// <summary>
		/// If overlay is disabled there is no point showing the in-game item store because the player will not be able
		/// to checkout. We request listings regardless in order to show the "sale" label automatically.
		/// </summary>
		private bool IsOverlayEnabledForCheckout =>
#if UNITY_EDITOR
				true; // We want to be able to test item store UIs in editor.
#else
				SteamUtils.IsOverlayEnabled();
#endif

		private bool isWaitingForSteamInventory;
		private bool isWaitingForLiveConfig;

#pragma warning disable
		private CallResult<SteamInventoryRequestPricesResult_t> requestPricesCallResult;
		private CallResult<SteamInventoryStartPurchaseResult_t> startPurchaseCallResult;
		private Callback<MicroTxnAuthorizationResponse_t> microTxnAuthCallback;
#pragma warning restore
	}
}
