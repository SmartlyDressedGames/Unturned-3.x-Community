////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
namespace SDG.Unturned
{
	public class SleekWebLinkButton : SleekWrapper
	{
		public string Text
		{
			get => internalButton.Text;
			set => internalButton.Text = value;
		}

		private string _url;
		public string Url
		{
			get => _url;
			set
			{
				_url = value;
				internalButton.TooltipText = _url;
			}
		}

		public override bool UseManualLayout
		{
			set
			{
				base.UseManualLayout = value;
				internalButton.UseManualLayout = value;
				internalButton.UseChildAutoLayout = value ? ESleekChildLayout.None : ESleekChildLayout.Horizontal;
				internalButton.ExpandChildren = !value;
			}
		}

		public bool useLinkFiltering = true;

		public SleekWebLinkButton()
		{
			internalButton = Glazier.Get().CreateButton();
			internalButton.SizeScale_X = 1.0f;
			internalButton.SizeScale_Y = 1.0f;
			internalButton.OnClicked += OnClicked;
			AddChild(internalButton);
		}

		private void OnClicked(ISleekElement button)
		{
			const string itemStoreDetailUrl = "store.steampowered.com/itemstore/304930/detail/";
			int itemStoreDetailUrlIndex = _url.IndexOf(itemStoreDetailUrl, System.StringComparison.OrdinalIgnoreCase);
			if (itemStoreDetailUrlIndex >= 0)
			{
				int itemdefidIndex = itemStoreDetailUrlIndex + itemStoreDetailUrl.Length;
				int trailingSlashIndex = _url.IndexOf('/', itemdefidIndex + 1);
				string itemdefidSubstring;
				if (trailingSlashIndex >= 0)
				{
					itemdefidSubstring = _url.Substring(itemdefidIndex, trailingSlashIndex - itemdefidIndex);
				}
				else
				{
					itemdefidSubstring = _url.Substring(itemdefidIndex);
				}

				int itemdefid;
				if (int.TryParse(itemdefidSubstring, out itemdefid))
				{
					UnturnedLog.info($"Parsed itemdefid {itemdefid} from web link url \"{_url}\"");
					ItemStoreSavedata.MarkNewListingSeen(itemdefid);
					ItemStore.Get().ViewItem(itemdefid);
					return;
				}
			}

			string parsedUrl;
			if (WebUtils.ParseThirdPartyUrl(_url, out parsedUrl, useLinkFiltering: useLinkFiltering))
			{
				WebUtils.OpenURL(parsedUrl);
			}
			else
			{
				UnturnedLog.warn("Ignoring potentially unsafe web link button url {0}", _url);
			}
		}

		private ISleekButton internalButton;
	}
}
