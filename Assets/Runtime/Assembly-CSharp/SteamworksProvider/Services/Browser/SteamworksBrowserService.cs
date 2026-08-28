////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using SDG.Provider.Services;
using SDG.Provider.Services.Browser;

namespace SDG.SteamworksProvider.Services.Browser
{
	public class SteamworksBrowserService : Service, IBrowserService
	{
		public bool canOpenBrowser => true;// SteamUtils.IsOverlayEnabled();

		public void open(string url)
		{
			SDG.Unturned.WebUtils.OpenURL(url);// SteamFriends.ActivateGameOverlayToWebPage(url);
		}
	}
}
