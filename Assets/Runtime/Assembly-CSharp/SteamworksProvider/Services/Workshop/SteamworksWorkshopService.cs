////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using SDG.Provider.Services;
using SDG.Provider.Services.Workshop;

namespace SDG.SteamworksProvider.Services.Workshop
{
	public class SteamworksWorkshopService : Service, IWorkshopService
	{
		public bool canOpenWorkshop => true;// SteamUtils.IsOverlayEnabled();

		public void open(Steamworks.PublishedFileId_t id)
		{
			SDG.Unturned.WebUtils.OpenURL("https://steamcommunity.com/sharedfiles/filedetails/?id=" + id.m_PublishedFileId);
		}
	}
}
