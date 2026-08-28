////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using SDG.Provider.Services;
using SDG.Provider.Services.Store;
using Steamworks;

namespace SDG.SteamworksProvider.Services.Store
{
	public class SteamworksStoreService : Service, IStoreService
	{
		private SteamworksAppInfo appInfo;

		public void open(IStorePackageID packageID)
		{
			SteamworksStorePackageID steamworksStorePackageID = (SteamworksStorePackageID) packageID;
			AppId_t appID = steamworksStorePackageID.appID;

			if (SteamUtils.IsOverlayEnabled())
			{
				SteamFriends.ActivateGameOverlayToStore(appID, EOverlayToStoreFlag.k_EOverlayToStoreFlag_None);
			}
			else
			{
				// Ideally not hard-coded, but if overlay is disabled we have no choice.
				SDG.Unturned.WebUtils.OpenURL("https://store.steampowered.com/app/" + appID.m_AppId);
			}
		}

		public SteamworksStoreService(SteamworksAppInfo newAppInfo)
		{
			appInfo = newAppInfo;
		}
	}
}
