////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;
using UnityEngine.UI;

namespace SDG.Unturned
{
	public class WebLink : MonoBehaviour
	{
		public Button targetButton;
		public string url;

		private void onClick()
		{
			string parsedUrl;
			if (WebUtils.ParseThirdPartyUrl(url, out parsedUrl))
			{
				WebUtils.OpenURL(parsedUrl);
			}
			else
			{
				UnturnedLog.warn("Ignoring potentially unsafe web link component url {0}", url);
			}
		}

		private void Start()
		{
			targetButton.onClick.AddListener(onClick);
		}
	}
}
