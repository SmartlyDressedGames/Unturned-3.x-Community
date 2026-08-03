////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	public class SleekBoomboxSong : SleekWrapper
	{
		public StereoSongAsset songAsset;

		public SleekBoomboxSong( StereoSongAsset songAsset, PlayerBarricadeStereoUI owningUI) : base()
		{
			this.songAsset = songAsset;
			this.owningUI = owningUI;

			ISleekButton button = Glazier.Get().CreateButton();
			button.SizeOffset_Y = 30;
			button.SizeScale_X = 1;
			button.OnClicked += OnClickedPlayButton;
			button.TextColor = ESleekTint.RICH_TEXT_DEFAULT;
			button.TextContrastContext = ETextContrastContext.InconspicuousBackdrop;
			button.AllowRichText = true;
			AddChild(button);

			if (!string.IsNullOrEmpty(songAsset.titleText))
			{
				button.Text = songAsset.titleText;
			}
			else
			{
				button.Text = songAsset.name;
			}

			if (!string.IsNullOrEmpty(songAsset.linkURL))
			{
				// Nelson 2024-08-12: With the addition of URL filtering we don't want to show the link if it isn't
				// approved.
				if (WebUtils.CanParseThirdPartyUrl(songAsset.linkURL))
				{
					button.SizeOffset_X -= 30;

					SleekButtonIcon linkButton = new SleekButtonIcon(MenuDashboardUI.icons.load<Texture2D>("External_Link"));
					linkButton.PositionOffset_X = -30;
					linkButton.PositionScale_X = 1.0f;
					linkButton.SizeOffset_X = 30;
					linkButton.SizeOffset_Y = 30;
					linkButton.tooltip = songAsset.linkURL;
					linkButton.onClickedButton += OnClickedLinkButton;
					AddChild(linkButton);
				}
			}
		}

		private void OnClickedPlayButton(ISleekElement button)
		{
			if (owningUI.stereo != null)
			{
				owningUI.stereo.ClientSetTrack(songAsset.GUID);
			}
		}

		private void OnClickedLinkButton(ISleekElement button)
		{
			string parsedLink;
			if (WebUtils.ParseThirdPartyUrl(songAsset.linkURL, out parsedLink))
			{
				Provider.openURL(parsedLink);
			}
			else
			{
				UnturnedLog.warn("Ignoring potentially unsafe song link url {0}", songAsset.linkURL);
			}
		}

		private PlayerBarricadeStereoUI owningUI;
	}

	public class PlayerBarricadeStereoUI : SleekFullscreenBox
	{
		private List<StereoSongAsset> songs = new List<StereoSongAsset>();
		private Local localization;

		public bool active;
		internal InteractableStereo stereo;

		/// <summary>
		/// Hack to prevent hitting volume rate limit because (at least as of 2022-05-24) we do not have an event for finished dragging.
		/// </summary>
		private double lastUpdateVolumeRealtime;
		private bool hasPendingVolumeUpdate;

		private int assetListChangeCounter;

		private ISleekButton stopButton;
		private ISleekButton closeButton;
		private ISleekSlider volumeSlider;
		private SleekList<StereoSongAsset> songsBox;

		public void open(InteractableStereo newStereo)
		{
			if (active)
			{
				close();
				return;
			}

			active = true;
			stereo = newStereo;
			hasPendingVolumeUpdate = false;

			refreshSongs();

			if (stereo != null)
			{
				volumeSlider.Value = stereo.volume;
			}

			updateVolumeSliderLabel();

			AnimateIntoView();
		}

		public void close()
		{
			if (!active)
			{
				return;
			}

			if (stereo != null && hasPendingVolumeUpdate)
			{
				hasPendingVolumeUpdate = false;
				stereo.ClientSetVolume(stereo.compressedVolume);
			}

			active = false;
			stereo = null;

			AnimateOutOfView(0, 1);
		}

		private void refreshSongs()
		{
			if (Assets.HasDefaultAssetMappingChanged(ref assetListChangeCounter))
			{
				songs.Clear();
				Assets.FindAssetsByType_UseDefaultAssetMapping(songs);

				songsBox.NotifyDataChanged();
			}
		}

		private void updateVolumeSliderLabel()
		{
			if (stereo != null)
			{
				volumeSlider.UpdateLabel(localization.format("Volume_Slider_Label", stereo.compressedVolume));
			}
		}

		private void onDraggedVolumeSlider(ISleekSlider slider, float state)
		{
			if (stereo != null)
			{
				stereo.volume = state;
				hasPendingVolumeUpdate = true;
				updateVolumeSliderLabel();
			}
		}

		private void onClickedStopButton(ISleekElement button)
		{
			if (stereo != null)
			{
				stereo.ClientSetTrack(Guid.Empty);
			}
		}

		private void onClickedCloseButton(ISleekElement button)
		{
			PlayerLifeUI.open();
			close();
		}

		public override void OnUpdate()
		{
			base.OnUpdate();

			if (stereo != null && hasPendingVolumeUpdate)
			{
				double realtime = Time.realtimeSinceStartupAsDouble;
				if (realtime - lastUpdateVolumeRealtime > 0.2f)
				{
					lastUpdateVolumeRealtime = realtime;
					stereo.ClientSetVolume(stereo.compressedVolume);
					hasPendingVolumeUpdate = false;
				}
			}
		}

		private ISleekElement OnCreateSongElement(StereoSongAsset songAsset)
		{
			return new SleekBoomboxSong(songAsset, this);
		}

		public PlayerBarricadeStereoUI() : base()
		{
			localization = Localization.read("/Player/PlayerBarricadeStereo.dat");

			PositionScale_Y = 1;
			PositionOffset_X = 10;
			PositionOffset_Y = 10;
			SizeOffset_X = -20;
			SizeOffset_Y = -20;
			SizeScale_X = 1;
			SizeScale_Y = 1;

			active = false;
			stereo = null;

			stopButton = Glazier.Get().CreateButton();
			stopButton.PositionOffset_X = -200;
			stopButton.PositionOffset_Y = 5;
			stopButton.PositionScale_X = 0.5f;
			stopButton.PositionScale_Y = 0.9f;
			stopButton.SizeOffset_X = 195;
			stopButton.SizeOffset_Y = 30;
			stopButton.Text = localization.format("Stop_Button");
			stopButton.TooltipText = localization.format("Stop_Button_Tooltip");
			stopButton.OnClicked += onClickedStopButton;
			AddChild(stopButton);

			closeButton = Glazier.Get().CreateButton();
			closeButton.PositionOffset_X = 5;
			closeButton.PositionOffset_Y = 5;
			closeButton.PositionScale_X = 0.5f;
			closeButton.PositionScale_Y = 0.9f;
			closeButton.SizeOffset_X = 195;
			closeButton.SizeOffset_Y = 30;
			closeButton.Text = localization.format("Close_Button");
			closeButton.TooltipText = localization.format("Close_Button_Tooltip");
			closeButton.OnClicked += onClickedCloseButton;
			AddChild(closeButton);

			volumeSlider = Glazier.Get().CreateSlider();
			volumeSlider.PositionOffset_X = -200;
			volumeSlider.PositionOffset_Y = -25;
			volumeSlider.PositionScale_X = 0.5f;
			volumeSlider.PositionScale_Y = 0.1f;
			volumeSlider.SizeOffset_X = 250;
			volumeSlider.SizeOffset_Y = 20;
			volumeSlider.Orientation = ESleekOrientation.HORIZONTAL;
			volumeSlider.OnValueChanged += onDraggedVolumeSlider;
			volumeSlider.AddLabel("", ESleekSide.RIGHT);
			AddChild(volumeSlider);

			songsBox = new SleekList<StereoSongAsset>();
			songsBox.PositionOffset_X = -200;
			songsBox.PositionScale_X = 0.5f;
			songsBox.PositionScale_Y = 0.1f;
			songsBox.SizeOffset_X = 400;
			songsBox.SizeScale_Y = 0.8f;
			songsBox.itemHeight = 30;
			songsBox.onCreateElement = OnCreateSongElement;
			songsBox.SetData(songs);
			AddChild(songsBox);
		}
	}
}
