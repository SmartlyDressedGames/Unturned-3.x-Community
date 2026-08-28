////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using UnityEngine.Networking;
using Steamworks;

namespace SDG.Unturned
{
	internal static class WebUtils
	{
		/// <summary>
		/// The game uses Process.Start to open web links when the Steam overlay is unavailable, which could be exploited
		/// to e.g. download and execute files. To prevent this we only allow valid http or https urls.
		/// </summary>
		/// <param name="autoPrefix">If true, prefix with https:// if neither http:// or https:// is specified.</param>
		internal static bool ParseThirdPartyUrl(string uriString, out string result, bool autoPrefix = true, bool useLinkFiltering = true)
		{
			if (string.IsNullOrEmpty(uriString))
			{
				result = null;
				return false;
			}

			uriString = uriString.Trim();

			if (autoPrefix)
			{
				// If a human-readable uri without scheme was provided then default to https.
				if (!uriString.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
					!uriString.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
				{
					uriString = "https://" + uriString;
				}
			}

			Uri uriObject;
			if (Uri.TryCreate(uriString, UriKind.Absolute, out uriObject))
			{
				if (uriObject.Scheme == Uri.UriSchemeHttp || uriObject.Scheme == Uri.UriSchemeHttps)
				{
#if !DEDICATED_SERVER
					if (useLinkFiltering)
					{
						LinkFilteringLiveConfig liveConfig = LiveConfig.Get().linkFiltering;
						ELinkFilteringAction action = liveConfig.Match(uriObject.Host, uriObject.AbsolutePath);
						if (action == ELinkFilteringAction.Deny)
						{
							result = null;
							return false;
						}

						if (action == ELinkFilteringAction.UseSteamLinkFilter)
						{
							string encodedUri = UnityWebRequest.EscapeURL(uriObject.AbsoluteUri);
							result = $"https://steamcommunity.com/linkfilter/?u={encodedUri}";
							return true;
						}
					}
#endif // !DEDICATED_SERVER

					result = uriObject.AbsoluteUri;
					return true;
				}
				else
				{
					result = null;
					return false;
				}
			}
			else
			{
				result = null;
				return false;
			}
		}

		/// <summary>
		/// This version just doesn't return the parsed URL.
		/// </summary>
		internal static bool CanParseThirdPartyUrl(string uriString, bool autoPrefix = true, bool useLinkFiltering = true)
		{
			return ParseThirdPartyUrl(uriString, out string unusedResult, autoPrefix, useLinkFiltering);
		}

		/// <summary>
		/// Open URL in the Steam overlay, or if disabled use the default browser instead.
		/// </summary>
		public static void OpenURL(string url)
		{
			// 2026-08-28: pre-existing code already passes third-party URLs through
			// WebUtils.ParseThirdPartyUrl, but to be safe we're going to enforce it in here as
			// well. In practice we never used non-HTTP/HTTPS URLs (e.g., steam://) anyway.
			string webUrl;
			if (!ParseThirdPartyUrl(url, out webUrl))
			{
				UnturnedLog.error($"Ignoring call to WebUtils.OpenURL, malicious URL? {url}");
				return;
			}

			// Previously game code would only open the steam overlay and show an error if disabled,
			// so the most straightforward option was to report that the URL can be opened and route
			// those requests through here instead.
			if (SteamUtils.IsOverlayEnabled())
			{
				SteamFriends.ActivateGameOverlayToWebPage(webUrl);
			}
			else
			{
				System.Diagnostics.Process.Start(webUrl);
			}
		}
	}
}
