////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using UnityEngine;
using System.Collections.Generic;
using Steamworks;
using SDG.Provider;
using System.Reflection;

#if !DEDICATED_SERVER
using SDG.HostBans;
#endif // !DEDICATED_SERVER
using Unturned.SystemEx;

namespace SDG.Unturned
{
	public class MenuPlayServerInfoUI
	{
		public enum EServerInfoOpenContext
		{
			CONNECT,
			SERVERS,
			BOOKMARKS,
		}

		private class ServerInfoViewWorkshopButton : SleekWrapper
		{
			public PublishedFileId_t fileId;

			public ServerInfoViewWorkshopButton(PublishedFileId_t fileId, string name)
			{
				this.fileId = fileId;

				SizeOffset_X = 20;
				SizeOffset_Y = 20;

				ISleekButton button = Glazier.Get().CreateButton();
				button.SizeScale_X = 1.0f;
				button.SizeScale_Y = 1.0f;
				button.OnClicked += onClickedButton;
				button.TooltipText = MenuWorkshopSubscriptionsUI.localization.format("View_Tooltip", name);
				AddChild(button);

				ISleekSprite sprite = Glazier.Get().CreateSprite();
				sprite.PositionOffset_X = 5;
				sprite.PositionOffset_Y = 5;
				sprite.SizeOffset_X = 10;
				sprite.SizeOffset_Y = 10;
				sprite.Sprite = MenuDashboardUI.icons.load<Sprite>("External_Link_Sprite");
				sprite.DrawMethod = ESleekSpriteType.Regular;
				button.AddChild(sprite);
			}

			private void onClickedButton(ISleekElement button)
			{
				string url = "https://steamcommunity.com/sharedfiles/filedetails/?id=" + fileId;
				Provider.provider.browserService.open(url);
			}
		}

		internal static Local localization;
		private static SleekFullscreenBox container;
		public static bool active;

		private static ISleekElement infoContainer;
		private static ISleekElement playersContainer;
		private static ISleekElement detailsContainer;
		private static ISleekElement mapContainer;
		private static ISleekElement buttonsContainer;

		private static ISleekBox titleBox;
		private static SleekWebImage titleIconImage;
		private static ISleekLabel titleNameLabel;
		private static ISleekLabel titleDescriptionLabel;
		private static ISleekLabel titleCurationLabelsLabel;

		private static ISleekBox playerCountBox;
		private static ISleekScrollView playersScrollBox;

		private static ISleekBox detailsBox;
		private static ISleekScrollView detailsScrollBox;
		private static ISleekButton hostBanWarningButton;
		private static ISleekButton notLoggedInWarningButton;
		private static ISleekElement linksFrame;
		private static ISleekBox serverTitle;
		private static ISleekBox serverBox;
		private static ISleekLabel serverWorkshopLabel;
		private static ISleekLabel serverCombatLabel;
		private static ISleekLabel serverPerspectiveLabel;
		private static ISleekLabel serverSecurityLabel;
		private static ISleekLabel serverModeLabel;
		private static ISleekLabel serverCheatsLabel;
		private static ISleekLabel serverMonetizationLabel;
		private static ISleekLabel serverPingLabel;
		private static ISleekBox ugcTitle;
		private static ISleekBox ugcBox;
		private static ISleekBox configTitle;
		private static ISleekBox configBox;
		private static ISleekBox rocketTitle;
		private static ISleekBox rocketBox;

		private static ISleekBox mapNameBox;
		private static ISleekBox mapPreviewBox;
		private static ISleekImage mapPreviewImage;
		private static ISleekBox mapDescriptionBox;
		private static ISleekBox serverDescriptionBox;

		private static ISleekButton joinButton;
		private static ISleekBox joinDisabledBox;
		private static ISleekButton favoriteButton;
		private static SleekButtonIcon bookmarkButton;
		private static ISleekButton refreshButton;
		private static SleekButtonIcon copyServerCodeButton;
		private static ISleekButton cancelButton;

		private static SteamServerAdvertisement serverInfo;
		private static string serverPassword;
		private static bool serverFavorited;
#if !DEDICATED_SERVER
		/// <summary>
		/// Null if not bookmarked.
		/// </summary>
		private static ServerBookmarkDetails bookmarkDetails;
#endif // !DEDICATED_SERVER
		/// <summary>
		/// DNS entry to use if adding a bookmark for this server.
		/// </summary>
		private static string serverBookmarkHost;
		private static List<PublishedFileId_t> expectedWorkshopItems;
		private static List<string> linkUrls;

		private static int playersOffset = 0;
		private static int playerCount = 0;
#if !DEDICATED_SERVER
		private static EHostBanFlags banFlags;
#endif // !DEDICATED_SERVER

		private static UGCQueryHandle_t detailsHandle;
#pragma warning disable
		private static CallResult<SteamUGCQueryCompleted_t> ugcQueryCompleted;
#pragma warning restore
		private static void onUGCQueryCompleted(SteamUGCQueryCompleted_t callback, bool io)
		{
			if (callback.m_eResult != EResult.k_EResultOK || io)
			{
				return;
			}

			for (uint index = 0; index < callback.m_unNumResultsReturned; ++index)
			{
				CachedUGCDetails details;
				if (TempSteamworksWorkshop.cacheDetails(callback.m_handle, index, out details))
				{
					string name = details.GetTitle();

					// Prior to uGUI refactor these had 5px padding at the top and were 20px tall,
					// but TMP CJK characters did not have enough vertical space and were getting truncated.
					ISleekLabel line = Glazier.Get().CreateLabel();
					line.PositionOffset_X = 5;
					line.PositionOffset_Y = (int) index * 20;
					line.SizeOffset_Y = 30;
					line.SizeScale_X = 1;
					line.TextAlignment = TextAnchor.MiddleLeft;
					line.Text = name;
					line.TextColor = details.isBannedOrPrivate ? ESleekTint.BAD : ESleekTint.FONT;
					ugcBox.AddChild(line);

					ServerInfoViewWorkshopButton viewButton = new ServerInfoViewWorkshopButton(details.fileId, name);
					viewButton.PositionOffset_X = -45;
					viewButton.PositionOffset_Y = line.PositionOffset_Y + 5;
					viewButton.PositionScale_X = 1.0f;
					ugcBox.AddChild(viewButton);

					SleekWorkshopSubscriptionButton manageSubscription = new SleekWorkshopSubscriptionButton();
					manageSubscription.PositionOffset_X = -25;
					manageSubscription.PositionOffset_Y = line.PositionOffset_Y + 5;
					manageSubscription.PositionScale_X = 1.0f;
					manageSubscription.SizeOffset_X = 20;
					manageSubscription.SizeOffset_Y = 20;
					manageSubscription.subscribeText = localization.format("Subscribe");
					manageSubscription.unsubscribeText = localization.format("Unsubscribe");
					manageSubscription.subscribeTooltip = localization.format("Subscribe_Tooltip", name);
					manageSubscription.unsubscribeTooltip = localization.format("Unsubscribe_Tooltip", name);
					manageSubscription.fileId = details.fileId;
					manageSubscription.synchronizeText();
					ugcBox.AddChild(manageSubscription);
				}
			}

			ugcBox.SizeOffset_Y = ((int) callback.m_unNumResultsReturned * 20) + 10;
			ugcTitle.IsVisible = true;
			ugcBox.IsVisible = true;
			updateDetails();
		}

		public static EServerInfoOpenContext openContext
		{
			get;
			private set;
		}

		public static string GetClipboardData()
		{
			System.Text.StringBuilder result = new System.Text.StringBuilder();
			result.AppendLine($"Name: {serverInfo.name}");
			result.AppendLine($"Description: {serverInfo.descText}");
			result.AppendLine($"Thumbnail: {serverInfo.thumbnailURL}");
			result.AppendLine($"Address: {Parser.getIPFromUInt32(serverInfo.ip)}");
			result.AppendLine($"Connection Port: {serverInfo.connectionPort}");
			result.AppendLine($"Query Port: {serverInfo.queryPort}");
			result.AppendLine($"SteamId: {serverInfo.steamID} ({serverInfo.steamID.GetEAccountType()})");
			result.AppendLine($"Ping: {serverInfo.PingMs}ms");

			if (expectedWorkshopItems == null)
			{
				result.AppendLine("Workshop files unknown");
			}
			else
			{
				result.AppendLine($"{expectedWorkshopItems.Count} workshop file(s):");
				for (int index = 0; index < expectedWorkshopItems.Count; ++index)
				{
					result.AppendLine($"{index}: {expectedWorkshopItems[index]}");
				}
			}

			return result.ToString();
		}

		public static void OpenWithoutRefresh()
		{
			if (active)
			{
				return;
			}

			active = true;
			container.AnimateIntoView();
		}

		public static void open(SteamServerAdvertisement newServerInfo, string newServerPassword, EServerInfoOpenContext newOpenContext)
		{
			if (active)
			{
				return;
			}

			active = true;
			openContext = newOpenContext;

			serverInfo = newServerInfo;
			serverPassword = newServerPassword;

			// Workshop items unknown until rules query is complete.
			expectedWorkshopItems = null;

			linkUrls = null;

			bool isJoiningBlockedByHostBans = false;

			IPv4Address ipv4Address = new IPv4Address(serverInfo.ip);
			serverBookmarkHost = null;

			bool isJoiningBlockedByAnonInternetListing = !serverInfo.steamID.BPersistentGameServerAccount() && ipv4Address.IsWideAreaNetwork;

			// Joining through LAN list bypasses WAN IP check because Steam may return public IP for a local server.
			isJoiningBlockedByAnonInternetListing &= serverInfo.infoSource != SteamServerAdvertisement.EInfoSource.LanServerList;

#if !DEDICATED_SERVER
			isJoiningBlockedByAnonInternetListing &= !LiveConfig.Get().shouldAllowJoiningInternetServersWithoutGslt;
#endif // !DEDICATED_SERVER
			if (isJoiningBlockedByAnonInternetListing)
			{
				UnturnedLog.info($"{serverInfo.name} is not logged in ({serverInfo.steamID}) and IP ({ipv4Address}) is WAN");
			}
			notLoggedInWarningButton.IsVisible = isJoiningBlockedByAnonInternetListing;

#if !DEDICATED_SERVER
			banFlags = HostBansManager.Get().MatchBasicDetails(ipv4Address, serverInfo.queryPort, serverInfo.name, serverInfo.steamID.m_SteamID);
			if (banFlags == EHostBanFlags.None)
			{
				banFlags = HostBansManager.Get().MatchExtendedDetails(serverInfo.descText, serverInfo.thumbnailURL);
			}
			UnturnedLog.info($"{serverInfo.name} host ban flags: {banFlags}");
			isJoiningBlockedByHostBans = banFlags.HasFlag(EHostBanFlags.Blocked);
			hostBanWarningButton.IsVisible = false;
			hostBanWarningButton.Text = string.Empty;
			if (banFlags.HasFlag(EHostBanFlags.MonetizationWarning))
			{
				hostBanWarningButton.IsVisible = true;
				hostBanWarningButton.Text += localization.format("HostBan_MonetizationWarning");
			}
			if (banFlags.HasFlag(EHostBanFlags.WorkshopWarning))
			{
				if (hostBanWarningButton.IsVisible)
				{
					hostBanWarningButton.Text += '\n';
				}
				else
				{
					hostBanWarningButton.IsVisible = true;
				}
				hostBanWarningButton.Text += localization.format("HostBan_WorkshopWarning");
			}
			if (banFlags.HasFlag(EHostBanFlags.IncorrectMonetizationTagWarning))
			{
				if (hostBanWarningButton.IsVisible)
				{
					hostBanWarningButton.Text += '\n';
				}
				else
				{
					hostBanWarningButton.IsVisible = true;
				}
				hostBanWarningButton.Text += localization.format("HostBan_IncorrectMonetizationTagWarning");
			}
			if (banFlags.HasFlag(EHostBanFlags.HostingProvider))
			{
				if (hostBanWarningButton.IsVisible)
				{
					hostBanWarningButton.Text += '\n';
				}
				else
				{
					hostBanWarningButton.IsVisible = true;
				}
				hostBanWarningButton.Text += localization.format("HostBan_HostingProvider");
			}
			if (banFlags.HasFlag(EHostBanFlags.Apology))
			{
				if (hostBanWarningButton.IsVisible)
				{
					hostBanWarningButton.Text += '\n';
				}
				else
				{
					hostBanWarningButton.IsVisible = true;
				}
				hostBanWarningButton.Text += localization.format("HostBan_Apology");

				hostBanWarningButton.TextColor = ESleekTint.FONT;
				hostBanWarningButton.IsRaycastTarget = false; // Disable clicking.
			}
			else
			{
				hostBanWarningButton.TextColor = ESleekTint.BAD;
				hostBanWarningButton.IsRaycastTarget = true;
			}
#endif // !DEDICATED_SERVER

			if (isJoiningBlockedByAnonInternetListing)
			{
				joinButton.IsVisible = false;
				joinDisabledBox.IsVisible = true;
				joinDisabledBox.Text = localization.format("NotLoggedInBlock_Label");
				joinDisabledBox.TooltipText = localization.format("NotLoggedInBlock_Tooltip");
			}
			else if (isJoiningBlockedByHostBans)
			{
				// Player may have manually entered IP address bypassing server list blacklist.
				// We disable in this case e.g. due to repeated DMCA violations.
				joinButton.IsVisible = false;
				joinDisabledBox.IsVisible = true;
				joinDisabledBox.Text = localization.format("ServerBlacklisted_Label");
				joinDisabledBox.TooltipText = localization.format("ServerBlacklisted_Tooltip");
			}
			else
			{
				joinButton.IsVisible = true;
				joinDisabledBox.IsVisible = false;
			}

			reset();

			serverFavorited = Provider.GetServerIsFavorited(serverInfo.ip, serverInfo.queryPort);
			updateFavorite();

#if !DEDICATED_SERVER
			bookmarkDetails = ServerBookmarksManager.FindBookmarkDetails(serverInfo);
			if (bookmarkDetails != null)
			{
				bookmarkDetails.UpdateFromAdvertisement(serverInfo);
				ServerBookmarksManager.MarkDirty();
			}
#endif
			UpdateBookmarkButton();

			updatePlayers();
			Provider.provider.matchmakingService.refreshPlayers(serverInfo.ip, serverInfo.queryPort);
			Provider.provider.matchmakingService.refreshPlayers(serverInfo.ip, serverInfo.queryPort);

			updateRules();
			Provider.provider.matchmakingService.refreshRules(serverInfo.ip, serverInfo.queryPort);

			updateServerInfo();

			UpdateVisibleButtons();

			container.AnimateIntoView();
		}

		public static void close()
		{
			if (!active)
			{
				return;
			}

			active = false;

			container.AnimateOutOfView(0, 1);
		}

		private static void onClickedJoinButton(ISleekElement button)
		{
			IPv4Address ipv4Address = new IPv4Address(serverInfo.ip);

			// serverPassword may already be filled out from the connect menu, joining a friend, auto-join, etc.
			if (serverInfo.isPassworded && string.IsNullOrEmpty(serverPassword))
			{
				MenuServerPasswordUI.open(serverInfo, expectedWorkshopItems);
				close();
			}
			else
			{
				ServerConnectParameters connectParameters = new ServerConnectParameters(ipv4Address, serverInfo.queryPort, serverInfo.connectionPort, serverPassword);
				Provider.connect(connectParameters, serverInfo, expectedWorkshopItems);
			}
		}

		private static void onClickedFavoriteButton(ISleekElement button)
		{
			serverFavorited = !serverFavorited;
			Provider.SetServerIsFavorited(serverInfo.ip, serverInfo.connectionPort, serverInfo.queryPort, serverFavorited);

			updateFavorite();
		}

		private static void OnClickedBookmarkButton(ISleekElement button)
		{
#if !DEDICATED_SERVER
			if (bookmarkDetails != null)
			{
				bookmarkDetails.isBookmarked = !bookmarkDetails.isBookmarked;
				if (bookmarkDetails.isBookmarked)
				{
					ServerBookmarksManager.AddBookmark(bookmarkDetails);
				}
				else
				{
					ServerBookmarksManager.RemoveBookmark(serverInfo.steamID);
				}
			}
			else
			{
				bookmarkDetails = ServerBookmarksManager.AddBookmark(serverInfo, serverBookmarkHost);
				Debug.Assert(bookmarkDetails.isBookmarked);
			}
#endif // !DEDICATED_SERVER

			UpdateBookmarkButton();
		}

		private static void onClickedRefreshButton(ISleekElement button)
		{
			updatePlayers();
			Provider.provider.matchmakingService.refreshPlayers(serverInfo.ip, serverInfo.queryPort);
		}

		private static void OnCopyServerCodeClicked(ISleekElement button)
		{
			GUIUtility.systemCopyBuffer = serverInfo.steamID.ToString();
		}

		private static void onClickedCancelButton(ISleekElement button)
		{
			switch (openContext)
			{
				case EServerInfoOpenContext.CONNECT:
					MenuPlayConnectUI.open();
					break;

				case EServerInfoOpenContext.SERVERS:
					MenuPlayUI.serverListUI.open(false);
					break;

				case EServerInfoOpenContext.BOOKMARKS:
					MenuPlayUI.serverBookmarksUI.open();
					break;
			}

			close();
		}

		private static void onMasterServerQueryRefreshed(SteamServerAdvertisement server)
		{
			serverInfo = server;
			updateServerInfo();
		}

		private static void reset()
		{
			titleDescriptionLabel.Text = string.Empty;
			titleIconImage.Clear();
			serverDescriptionBox.Text = string.Empty;
			titleCurationLabelsLabel.Text = string.Empty;
		}

		private static void updateServerInfo()
		{
			titleNameLabel.TextColor = serverInfo.isPro ? new SleekColor(Palette.PRO) : new SleekColor(ESleekTint.FONT);
			titleNameLabel.Text = serverInfo.name;
			titleCurationLabelsLabel.Text = serverInfo.serverCurationLabels;

			int serverBoxOffset = 0;

			serverWorkshopLabel.Text = localization.format("Workshop", localization.format(serverInfo.isWorkshop ? "Yes" : "No"));
			serverBoxOffset += 20;

			serverCombatLabel.Text = localization.format("Combat", localization.format(serverInfo.isPvP ? "PvP" : "PvE"));
			serverBoxOffset += 20;

			string cameraMode;
			switch (serverInfo.cameraMode)
			{
				case ECameraMode.FIRST:
					cameraMode = localization.format("First");
					break;
				case ECameraMode.THIRD:
					cameraMode = localization.format("Third");
					break;
				case ECameraMode.BOTH:
					cameraMode = localization.format("Both");
					break;
				case ECameraMode.VEHICLE:
					cameraMode = localization.format("Vehicle");
					break;
				default:
					cameraMode = string.Empty;
					break;
			}

			serverPerspectiveLabel.Text = localization.format("Perspective", cameraMode);
			serverPerspectiveLabel.IsVisible = !string.IsNullOrEmpty(cameraMode);
			serverBoxOffset += serverPerspectiveLabel.IsVisible ? 20 : 0;

			string security;
			if (serverInfo.IsVACSecure)
			{
				security = localization.format("VAC_Secure");
			}
			else
			{
				security = localization.format("VAC_Insecure");
			}

#if WITH_THIRDPARTYAC
			if (serverInfo.IsThirdpartyAntiCheatEnabled)
			{
				security += " + " + localization.format(ThirdpartyAntiCheat.SecureLocalizationKey);
			}
			else
			{
				security += " + " + localization.format(ThirdpartyAntiCheat.InsecureLocalizationKey);
			}
#endif

			serverSecurityLabel.PositionOffset_Y = serverBoxOffset;
			serverSecurityLabel.Text = localization.format("Security", security);
			serverBoxOffset += 20;

			string mode;
			switch (serverInfo.mode)
			{
				case EGameMode.EASY:
					mode = localization.format("Easy");
					break;
				case EGameMode.NORMAL:
					mode = localization.format("Normal");
					break;
				case EGameMode.HARD:
					mode = localization.format("Hard");
					break;
				default:
					mode = string.Empty;
					break;
			}

			serverModeLabel.PositionOffset_Y = serverBoxOffset;
			serverModeLabel.Text = localization.format("Mode", mode);
			serverBoxOffset += 20;

			serverCheatsLabel.PositionOffset_Y = serverBoxOffset;
			serverCheatsLabel.Text = localization.format("Cheats", localization.format(serverInfo.hasCheats ? "Yes" : "No"));
			serverBoxOffset += 20;

			if (serverInfo.monetization != EServerMonetizationTag.Unspecified)
			{
				serverMonetizationLabel.IsVisible = true;
				serverMonetizationLabel.PositionOffset_Y = serverBoxOffset;
				switch (serverInfo.monetization)
				{
					case EServerMonetizationTag.None:
						serverMonetizationLabel.Text = localization.format("Monetization_None");
						break;

					case EServerMonetizationTag.NonGameplay:
						serverMonetizationLabel.Text = localization.format("Monetization_NonGameplay");
						break;

					case EServerMonetizationTag.Monetized:
						serverMonetizationLabel.Text = localization.format("Monetization_Monetized");
						break;

					default:
						serverMonetizationLabel.Text = "unknown: " + serverInfo.monetization;
						break;
				}
				serverBoxOffset += 20;
			}
			else
			{
				serverMonetizationLabel.IsVisible = false;
			}

			serverPingLabel.Text = localization.format("QueryPing", serverInfo.PingMs);
			serverPingLabel.PositionOffset_Y = serverBoxOffset;
			serverBoxOffset += 20;
			if (serverInfo.anycastProxyMode != SteamServerAdvertisement.EAnycastProxyMode.None)
			{
				serverPingLabel.Text += " - ";
				serverPingLabel.Text += localization.format("HostBan_QueryPingWarning");
				if (serverInfo.anycastProxyMode == SteamServerAdvertisement.EAnycastProxyMode.FlaggedByModerator)
				{
					serverPingLabel.TextColor = ESleekTint.BAD;
				}
				else
				{
					serverPingLabel.TextColor = ESleekTint.FONT;
				}
			}
			else
			{
				serverPingLabel.TextColor = ESleekTint.FONT;
			}

			serverBox.SizeOffset_Y = serverBoxOffset + 10;
			updateDetails();

			LevelInfo level = Level.getLevel(serverInfo.map);
			if (level != null)
			{
				Local localization2 = level.getLocalization();
				if (localization2 != null)
				{
					string desc = localization2.format("Description");
					desc = ItemTool.filterRarityRichText(desc);
					RichTextUtil.replaceNewlineMarkup(ref desc);

					mapDescriptionBox.Text = desc;
				}

				if (localization2 != null && localization2.has("Name"))
				{
					mapNameBox.Text = localization.format("Map", localization2.format("Name"));
				}
				else
				{
					mapNameBox.Text = localization.format("Map", serverInfo.map);
				}

				string previewPath = level.GetPreviewImageFilePath();
				if (!string.IsNullOrEmpty(previewPath))
				{
					mapPreviewImage.SetTextureAndShouldDestroy(ReadWrite.readTextureFromFile(previewPath), true);
				}
			}
			else
			{
				mapDescriptionBox.Text = string.Empty;
				mapNameBox.Text = serverInfo.map;
				mapPreviewImage.SetTextureAndShouldDestroy(null, true);
			}
		}

		private static void updateFavorite()
		{
			favoriteButton.IsVisible = !serverInfo.IsAddressUsingSteamFakeIP();

			if (serverFavorited)
			{
				favoriteButton.Text = localization.format("Favorite_Off_Button");
			}
			else
			{
				favoriteButton.Text = localization.format("Favorite_On_Button");
			}
		}

		private static void UpdateBookmarkButton()
		{
#if !DEDICATED_SERVER
			bookmarkButton.IsVisible = serverInfo.steamID.BPersistentGameServerAccount();

			if (bookmarkDetails != null && bookmarkDetails.isBookmarked)
			{
				bookmarkButton.text = localization.format("Bookmark_Off_Button");
				bookmarkButton.icon = MenuPlayUI.serverListUI.icons.load<Texture2D>("Bookmark_Remove");
			}
			else
			{
				bookmarkButton.text = localization.format("Bookmark_On_Button");
				bookmarkButton.icon = MenuPlayUI.serverListUI.icons.load<Texture2D>("Bookmark_Add");
			}
#endif // !DEDICATED_SERVER
		}

		private static void updatePlayers()
		{
			playersScrollBox.RemoveAllChildren();
			playersOffset = 0;
			playerCount = 0;

			playerCountBox.Text = localization.format("Players", playerCount, serverInfo.maxPlayers);
		}

		private static void onPlayersQueryRefreshed(string name, int score, float time)
		{
			System.TimeSpan timeSpan = System.TimeSpan.FromSeconds(time);
			string timeFormat = string.Empty;

			if (timeSpan.Days > 0)
			{
				timeFormat += " " + timeSpan.Days + "d";
			}

			if (timeSpan.Hours > 0)
			{
				timeFormat += " " + timeSpan.Hours + "h";
			}

			if (timeSpan.Minutes > 0)
			{
				timeFormat += " " + timeSpan.Minutes + "m";
			}

			if (timeSpan.Seconds > 0)
			{
				timeFormat += " " + timeSpan.Seconds + "s";
			}

			ISleekBox playerBox = Glazier.Get().CreateBox();
			playerBox.PositionOffset_Y = playersOffset;
			playerBox.SizeOffset_Y = 30;
			playerBox.SizeScale_X = 1;
			playersScrollBox.AddChild(playerBox);

			ISleekLabel nameLabel = Glazier.Get().CreateLabel();
			nameLabel.PositionOffset_X = 5;
			nameLabel.SizeOffset_X = -10;
			nameLabel.SizeScale_X = 1;
			nameLabel.SizeScale_Y = 1;
			nameLabel.TextAlignment = TextAnchor.MiddleLeft;
			nameLabel.Text = name;
			playerBox.AddChild(nameLabel);

			ISleekLabel timeLabel = Glazier.Get().CreateLabel();
			timeLabel.PositionOffset_X = -5;
			timeLabel.SizeOffset_X = -10;
			timeLabel.SizeScale_X = 1;
			timeLabel.SizeScale_Y = 1;
			timeLabel.TextAlignment = TextAnchor.MiddleRight;
			timeLabel.Text = timeFormat;
			playerBox.AddChild(timeLabel);

			playersOffset += 40;
			playersScrollBox.ContentSizeOffset = new Vector2(0.0f, playersOffset - 10);

			++playerCount;
			playerCountBox.Text = localization.format("Players", playerCount, serverInfo.maxPlayers);
		}

		private static void updateRules()
		{
			linksFrame.RemoveAllChildren();
			linksFrame.IsVisible = false;

			ugcTitle.IsVisible = false;
			ugcBox.RemoveAllChildren();
			ugcBox.IsVisible = false;

			configTitle.IsVisible = false;
			configBox.RemoveAllChildren();
			configBox.IsVisible = false;

			rocketTitle.IsVisible = false;
			rocketBox.RemoveAllChildren();
			rocketBox.IsVisible = false;

			updateDetails();
		}

		private static void onRulesQueryRefreshed(Dictionary<string, string> rulesMap)
		{
			if (rulesMap == null)
			{
				return;
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.AppendLine($"{rulesMap.Count} rule(s):");
			int logRuleIndex = 0;
			foreach (KeyValuePair<string, string> rule in rulesMap)
			{
				sb.AppendLine($"[{logRuleIndex}] \"{rule.Key}\": \"{rule.Value}\"");
				++logRuleIndex;
			}
			UnturnedLog.info(sb.ToString());
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD

			string browserIcon;
			if (rulesMap.TryGetValue("Browser_Icon", out browserIcon) && !string.IsNullOrEmpty(browserIcon))
			{
				titleIconImage.Refresh(browserIcon);
			}

			string browserDescHint;
			if (rulesMap.TryGetValue("Browser_Desc_Hint", out browserDescHint) && !string.IsNullOrEmpty(browserDescHint))
			{
				ProfanityFilter.ApplyFilter(OptionsSettings.filter, ref browserDescHint);

				titleDescriptionLabel.Text = browserDescHint;
			}

			if (rulesMap.TryGetValue("BookmarkHost", out string bookmarkHost))
			{
				// Only update if found, otherwise use IP address.
				serverBookmarkHost = bookmarkHost;
#if !DEDICATED_SERVER
				if (bookmarkDetails != null)
				{
					// Old host may have been valid, but update in case, e.g., the server is changing DNS entries. 
					bookmarkDetails.host = serverBookmarkHost;
					ServerBookmarksManager.MarkDirty();
				}
				UpdateBookmarkButton();
				UpdateVisibleButtons();
#endif // !DEDICATED_SERVER
			}

			string browserDescFullCount;
			if (rulesMap.TryGetValue("Browser_Desc_Full_Count", out browserDescFullCount))
			{
				int lineCount;
				if (int.TryParse(browserDescFullCount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out lineCount) && lineCount > 0)
				{
					string base64String = string.Empty;
					for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
					{
						string line;
						if (rulesMap.TryGetValue("Browser_Desc_Full_Line_" + lineIndex, out line))
						{
							base64String += line;
						}
					}

					string utf8String;
					if (ConvertEx.TryDecodeBase64AsUtf8String(base64String, out utf8String))
					{
						if (!string.IsNullOrEmpty(utf8String))
						{
							ProfanityFilter.ApplyFilter(OptionsSettings.filter, ref utf8String);
							RichTextUtil.replaceNewlineMarkup(ref utf8String);

							serverDescriptionBox.Text = utf8String;
						}
					}
					else
					{
						UnturnedLog.error($"Unable to convert server browser Base64 string: \"{base64String}\"");
					}
				}
			}

			linkUrls = new List<string>();

			string linksCountString;
			if (rulesMap.TryGetValue("Custom_Links_Count", out linksCountString))
			{
				int linksCount;
				if (int.TryParse(linksCountString, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out linksCount) && linksCount > 0)
				{
					int linkOffset = 0;

					for (int linkIndex = 0; linkIndex < linksCount; ++linkIndex)
					{
						string messageBase64;
						if (!rulesMap.TryGetValue("Custom_Link_Message_" + linkIndex, out messageBase64))
						{
							UnturnedLog.warn("Skipping link index {0} because message is missing", linkIndex);
							continue;
						}

						if (string.IsNullOrEmpty(messageBase64))
						{
							UnturnedLog.warn("Skipping link index {0} because message is empty", linkIndex);
							continue;
						}

						string urlBase64;
						if (!rulesMap.TryGetValue("Custom_Link_Url_" + linkIndex, out urlBase64))
						{
							UnturnedLog.warn("Skipping link index {0} because url is missing", linkIndex);
							continue;
						}

						if (string.IsNullOrEmpty(urlBase64))
						{
							UnturnedLog.warn("Skipping link index {0} because url is empty", linkIndex);
							continue;
						}

						string messageUtf8;
						if (!ConvertEx.TryDecodeBase64AsUtf8String(messageBase64, out messageUtf8))
						{
							UnturnedLog.warn("Skipping link index {0} because unable to decode message Base64: \"{1}\"", linkIndex, messageBase64);
							continue;
						}

						string urlUtf8;
						if (!ConvertEx.TryDecodeBase64AsUtf8String(urlBase64, out urlUtf8))
						{
							UnturnedLog.warn("Skipping link index {0} because unable to decode url Base64: \"{1}\"", linkIndex, urlBase64);
							continue;
						}

						string parsedUrl;
						if (!WebUtils.ParseThirdPartyUrl(urlUtf8, out parsedUrl))
						{
							UnturnedLog.warn("Ignoring potentially unsafe link index {0} url {1}", linkIndex, urlUtf8);
							continue;
						}

						ProfanityFilter.ApplyFilter(OptionsSettings.filter, ref messageUtf8);

						linkUrls.Add(parsedUrl);

						ISleekButton linkButton = Glazier.Get().CreateButton();
						linkButton.PositionOffset_Y += linkOffset;
						linkButton.SizeScale_X = 1.0f;
						linkButton.SizeOffset_Y = 30;
						linkButton.AllowRichText = true;
						linkButton.Text = messageUtf8;
						linkButton.TooltipText = urlUtf8; // Show original link text without Steam redirect.
						linkButton.TextColor = ESleekTint.RICH_TEXT_DEFAULT;
						linkButton.OnClicked += OnClickedLinkButton;
						linksFrame.AddChild(linkButton);
						linkOffset += 30;
					}

					if (linkOffset > 0)
					{
						linksFrame.SizeOffset_Y = linkOffset;
						linksFrame.IsVisible = true;
					}
				}
			}

			string rocketplugins;
			if (rulesMap.TryGetValue("rocketplugins", out rocketplugins) && !string.IsNullOrEmpty(rocketplugins))
			{
				string[] plugins = rocketplugins.Split(',');
				rocketBox.SizeOffset_Y = (plugins.Length * 20) + 10;
				for (int index = 0; index < plugins.Length; index++)
				{
					ISleekLabel line = Glazier.Get().CreateLabel();
					line.PositionOffset_X = 5;
					line.PositionOffset_Y = index * 20;
					line.SizeOffset_Y = 30;
					line.SizeScale_X = 1;
					line.TextAlignment = TextAnchor.MiddleLeft;
					line.Text = plugins[index];
					rocketBox.AddChild(line);
				}

				if (serverInfo.pluginFramework == SteamServerAdvertisement.EPluginFramework.Rocket)
				{
					rocketTitle.Text = localization.format("Plugins_Rocket");
				}
				else if (serverInfo.pluginFramework == SteamServerAdvertisement.EPluginFramework.OpenMod)
				{
					rocketTitle.Text = localization.format("Plugins_OpenMod");
				}
				else
				{
					rocketTitle.Text = localization.format("Plugins_Unknown");
				}

				rocketTitle.IsVisible = true;
				rocketBox.IsVisible = true;
			}

			// Default to expecting zero workshop items.
			expectedWorkshopItems = new List<PublishedFileId_t>(0);

			string browserWorkshopCount;
			if (rulesMap.TryGetValue("Mod_Count", out browserWorkshopCount))
			{
				int lineCount;
				if (int.TryParse(browserWorkshopCount, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out lineCount) && lineCount > 0)
				{
					string browserWorkshop = string.Empty;
					for (int lineIndex = 0; lineIndex < lineCount; lineIndex++)
					{
						string line;
						if (rulesMap.TryGetValue("Mod_" + lineIndex, out line))
						{
							browserWorkshop += line;
						}
					}

					string[] workshop = browserWorkshop.Split(',');
					expectedWorkshopItems = new List<PublishedFileId_t>(workshop.Length);
					for (int index = 0; index < workshop.Length; index++)
					{
						ulong fileID;
						if (ulong.TryParse(workshop[index], System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fileID))
						{
							expectedWorkshopItems.Add(new PublishedFileId_t(fileID));
						}
					}

					detailsHandle = SteamUGC.CreateQueryUGCDetailsRequest(expectedWorkshopItems.ToArray(), (uint) expectedWorkshopItems.Count);
					SteamUGC.SetAllowCachedResponse(detailsHandle, 60);
					SteamAPICall_t send = SteamUGC.SendQueryUGCRequest(detailsHandle);
					ugcQueryCompleted.Set(send);
				}
			}

			string browserConfigCountString;
			int browserConfigCount;
			if (rulesMap.TryGetValue("Cfg_Count", out browserConfigCountString) &&
				int.TryParse(browserConfigCountString, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out browserConfigCount)
				&& browserConfigCount > 0)
			{
				int offset = 0;

				for (int configIndex = 0; configIndex < browserConfigCount; ++configIndex)
				{
					string configLine;
					if (!rulesMap.TryGetValue("Cfg_" + configIndex.ToString(System.Globalization.CultureInfo.InvariantCulture), out configLine))
						continue;

					int delimiterIndex = configLine.IndexOf('.');
					int equalsIndex = configLine.IndexOf('=', delimiterIndex + 1);
					if (delimiterIndex < 0 || equalsIndex < 0)
						continue;

					string groupName = configLine.Substring(0, delimiterIndex);
					string fieldName = configLine.Substring(delimiterIndex + 1, equalsIndex - delimiterIndex - 1);
					string valueString = configLine.Substring(equalsIndex + 1);
					string valueDisplay = null;

					bool shouldWarn = false;
					FieldInfo categoryField = typeof(ModeConfigData).GetField(groupName);
					if (categoryField == null)
					{
						UnturnedLog.warn($"Unknown config category \"{groupName}\" in \"{configLine}\"");
					}
					else
					{
						FieldInfo valueField = categoryField.FieldType.GetField(fieldName);
						if (valueField == null)
						{
							UnturnedLog.warn($"Unknown config field \"{fieldName}\" in \"{configLine}\"");
						}
						else
						{
							shouldWarn = valueField.GetCustomAttribute<ConfigWarnIfTrueAttribute>() != null;
						}
					}

					if (valueString == "T")
					{
						valueDisplay = localization.format("Yes");
					}
					else if (valueString == "F")
					{
						valueDisplay = localization.format("No");
					}
					else
					{
						float valueFloat;
						if (float.TryParse(valueString, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out valueFloat))
						{
							// Convert from invariant to local.
							valueDisplay = valueFloat.ToString();
						}
						else
						{
							int valueInt;
							if (int.TryParse(valueString, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out valueInt))
							{
								// Convert from invariant to local.
								valueDisplay = valueInt.ToString();
							}
						}
					}

					if (string.IsNullOrEmpty(valueDisplay))
					{
						// Version difference?
						ISleekLabel errorLabel = Glazier.Get().CreateLabel();
						errorLabel.PositionOffset_X = 5;
						errorLabel.PositionOffset_Y = offset;
						errorLabel.SizeOffset_X = -10;
						errorLabel.SizeOffset_Y = 30;
						errorLabel.SizeScale_X = 1.0f;
						errorLabel.TextAlignment = TextAnchor.MiddleLeft;
						errorLabel.TextColor = ESleekTint.BAD;
						errorLabel.Text = configLine;
						configBox.AddChild(errorLabel);
					}
					else
					{
						ISleekLabel lineCategory = Glazier.Get().CreateLabel();
						lineCategory.PositionOffset_X = 5;
						lineCategory.PositionOffset_Y = offset;
						lineCategory.SizeOffset_X = -5;
						lineCategory.SizeOffset_Y = 30;
						lineCategory.SizeScale_X = 0.25f;
						lineCategory.TextAlignment = TextAnchor.MiddleRight;
						lineCategory.Text = MenuPlayConfigUI.sanitizeName(groupName);
						lineCategory.TextColor = new SleekColor(ESleekTint.FONT, 0.5f);
						configBox.AddChild(lineCategory);

						ISleekLabel line = Glazier.Get().CreateLabel();
						line.PositionOffset_X = 5;
						line.PositionOffset_Y = offset;
						line.PositionScale_X = 0.25f;
						line.SizeOffset_X = -5;
						line.SizeOffset_Y = 30;
						line.SizeScale_X = 0.75f;
						line.TextAlignment = TextAnchor.MiddleLeft;
						line.Text = localization.format("Rule", MenuPlayConfigUI.sanitizeName(fieldName), valueDisplay);
						if (shouldWarn)
						{
							line.TextColor = ESleekTint.BAD;
						}
						configBox.AddChild(line);
					}

					offset += 20;
				}

				configBox.SizeOffset_Y = offset + 10;
				if (offset > 0)
				{
					configTitle.IsVisible = true;
					configBox.IsVisible = true;
				}
			}

			// Nelson 2023-10-25: the newest version before I added GameVersion to the key/values map was
			// 3.21.7.0, so for a couple years after the update we defaulted to that version if GameVersion
			// was not found. Sometimes, however, an as-yet-unknown bug causes GameVersion to not be returned
			// e.g., in public issue #4167. Considering that worst case the server will kick the client with
			// a wrong version message, and GameVersion works for most players, and the server browser already
			// filters out old servers, I think it will be fine to skip this version check if missing.
			string serverVersion;
			if (rulesMap.TryGetValue("GameVersion", out serverVersion))
			{
				// Only proceed if serverVersion is parsed successfully because issue #3119 seems to indicate Linux
				// Steam client is truncating long rules data.
				uint packedServerVersion;
				if (Parser.TryGetUInt32FromIP(serverVersion, out packedServerVersion) && Provider.APP_VERSION_PACKED != packedServerVersion)
				{
					joinButton.IsVisible = false;
					joinDisabledBox.IsVisible = true;
					if (packedServerVersion > Provider.APP_VERSION_PACKED)
					{
						// Server is running a newer version of the game.
						joinDisabledBox.Text = localization.format("ServerNewerVersion_Label", serverVersion);
						joinDisabledBox.TooltipText = localization.format("ServerNewerVersion_Tooltip");
					}
					else
					{
						// Server is running an older version of the game.
						joinDisabledBox.Text = localization.format("ServerOlderVersion_Label", serverVersion);
						joinDisabledBox.TooltipText = localization.format("ServerOlderVersion_Tooltip");
					}
				}
			}

			if (rulesMap.TryGetValue("ModName", out string modName))
			{
				if (Provider._modInfo != null)
				{
					if (string.Equals(Provider._modInfo.Name, modName, System.StringComparison.Ordinal))
					{
						if (rulesMap.TryGetValue("ModVersion", out string modVersion))
						{
							if (!string.Equals(modVersion, Provider._modInfo.FormatModVersion()))
							{
								joinButton.IsVisible = false;
								joinDisabledBox.IsVisible = true;
								joinDisabledBox.Text = localization.format("ServerDifferentModVersion_Label", modVersion);
								joinDisabledBox.TooltipText = localization.format("ServerDifferentModVersion_Tooltip");
							}
						}
					}
					else
					{
						joinButton.IsVisible = false;
						joinDisabledBox.IsVisible = true;
						joinDisabledBox.Text = localization.format("SeverDifferentMod_Label", modName);
						joinDisabledBox.TooltipText = localization.format("SeverDifferentMod_Tooltip");
					}
				}
				else
				{
					joinButton.IsVisible = false;
					joinDisabledBox.IsVisible = true;
					joinDisabledBox.Text = localization.format("ServerIsModded_Label", modName);
					joinDisabledBox.TooltipText = localization.format("ServerIsModded_Tooltip");
				}
			}
			else
			{
				if (Provider._modInfo != null)
				{
					joinButton.IsVisible = false;
					joinDisabledBox.IsVisible = true;
					joinDisabledBox.Text = localization.format("ServerNotModded_Label");
					joinDisabledBox.TooltipText = localization.format("ServerNotModded_Tooltip");
				}
			}

			updateDetails();
		}

		private static void updateDetails()
		{
			float detailsOffset = 0;

			if (hostBanWarningButton.IsVisible)
			{
				hostBanWarningButton.PositionOffset_X = detailsOffset;
				detailsOffset += hostBanWarningButton.SizeOffset_Y + 10;
			}

			if (notLoggedInWarningButton.IsVisible)
			{
				notLoggedInWarningButton.PositionOffset_X = detailsOffset;
				detailsOffset += notLoggedInWarningButton.SizeOffset_Y + 10;
			}

			if (linksFrame.IsVisible)
			{
				linksFrame.PositionOffset_Y = detailsOffset;
				detailsOffset += linksFrame.SizeOffset_Y + 10;
			}

			if (serverTitle.IsVisible)
			{
				serverTitle.PositionOffset_Y = detailsOffset;
				detailsOffset += 40;
			}

			if (serverBox.IsVisible)
			{
				serverBox.PositionOffset_Y = detailsOffset;
				detailsOffset += serverBox.SizeOffset_Y + 10;
			}

			if (ugcTitle.IsVisible)
			{
				ugcTitle.PositionOffset_Y = detailsOffset;
				detailsOffset += ugcTitle.SizeOffset_Y + 10;
			}

			if (ugcBox.IsVisible)
			{
				ugcBox.PositionOffset_Y = detailsOffset;
				detailsOffset += ugcBox.SizeOffset_Y + 10;
			}

			if (configTitle.IsVisible)
			{
				configTitle.PositionOffset_Y = detailsOffset;
				detailsOffset += configTitle.SizeOffset_Y + 10;
			}

			if (configBox.IsVisible)
			{
				configBox.PositionOffset_Y = detailsOffset;
				detailsOffset += configBox.SizeOffset_Y + 10;
			}

			if (rocketTitle.IsVisible)
			{
				rocketTitle.PositionOffset_Y = detailsOffset;
				detailsOffset += rocketTitle.SizeOffset_Y + 10;
			}

			if (rocketBox.IsVisible)
			{
				rocketBox.PositionOffset_Y = detailsOffset;
				detailsOffset += rocketBox.SizeOffset_Y + 10;
			}

			detailsScrollBox.ContentSizeOffset = new Vector2(0.0f, detailsOffset - 10);
		}

		/// <summary>
		/// Adjusts width and spacing of buttons along the bottom of the screen.
		/// Favorite and bookmark buttons can be hidden depending whether the necessary server details are set.
		/// </summary>
		private static void UpdateVisibleButtons()
		{
			int visibleButtonCount = 4;

			if (favoriteButton.IsVisible)
			{
				++visibleButtonCount;
			}

			if (bookmarkButton.IsVisible)
			{
				++visibleButtonCount;
			}

			float width = 1.0f / visibleButtonCount;

			joinButton.SizeScale_X = width;
			joinDisabledBox.SizeScale_X = width;

			float position = width;

			if (favoriteButton.IsVisible)
			{
				favoriteButton.PositionScale_X = position;
				favoriteButton.SizeScale_X = width;
				position += width;
			}

			if (bookmarkButton.IsVisible)
			{
				bookmarkButton.PositionScale_X = position;
				bookmarkButton.SizeScale_X = width;
				position += width;
			}

			refreshButton.PositionScale_X = position;
			refreshButton.SizeScale_X = width;
			position += width;

			copyServerCodeButton.PositionScale_X = position;
			copyServerCodeButton.SizeScale_X = width;
			position += width;

			cancelButton.PositionScale_X = 1.0f - width;
			cancelButton.SizeScale_X = width;
		}

		private static void OnClickedLinkButton(ISleekElement button)
		{
			int index = linksFrame.FindIndexOfChild(button);
			// Link URL has already been filtered.
			WebUtils.OpenURL(linkUrls[index]);
		}

		private static void OnClickedHostBanWarning(ISleekElement button)
		{
			WebUtils.OpenURL("https://docs.smartlydressedgames.com/en/stable/servers/server-hosting-rules.html");
		}

		private static void OnClickedNotLoggedInWarning(ISleekElement button)
		{
			WebUtils.OpenURL("https://docs.smartlydressedgames.com/en/stable/servers/game-server-login-tokens.html");
		}

		public void OnDestroy()
		{
			Provider.provider.matchmakingService.onMasterServerQueryRefreshed -= onMasterServerQueryRefreshed;
			Provider.provider.matchmakingService.onPlayersQueryRefreshed -= onPlayersQueryRefreshed;
			Provider.provider.matchmakingService.onRulesQueryRefreshed -= onRulesQueryRefreshed;

			if (ugcQueryCompleted != null)
			{
				ugcQueryCompleted.Dispose();
				ugcQueryCompleted = null;
			}
		}

		public MenuPlayServerInfoUI()
		{
			localization = Localization.read("/Menu/Play/MenuPlayServerInfo.dat");

			container = new SleekFullscreenBox();
			container.PositionOffset_X = 10;
			container.PositionOffset_Y = 10;
			container.PositionScale_Y = 1;
			container.SizeOffset_X = -20;
			container.SizeOffset_Y = -20;
			container.SizeScale_X = 1;
			container.SizeScale_Y = 1;
			MenuUI.container.AddChild(container);
			active = false;

			infoContainer = Glazier.Get().CreateFrame();
			infoContainer.PositionOffset_Y = 94;
			infoContainer.SizeOffset_Y = -154;
			infoContainer.SizeScale_X = 1;
			infoContainer.SizeScale_Y = 1;
			container.AddChild(infoContainer);

			buttonsContainer = Glazier.Get().CreateFrame();
			buttonsContainer.PositionOffset_Y = -50;
			buttonsContainer.PositionScale_Y = 1;
			buttonsContainer.SizeOffset_Y = 50;
			buttonsContainer.SizeScale_X = 1;
			container.AddChild(buttonsContainer);

			playersContainer = Glazier.Get().CreateFrame();
			playersContainer.SizeOffset_X = 280;
			playersContainer.SizeScale_Y = 1;
			infoContainer.AddChild(playersContainer);

			detailsContainer = Glazier.Get().CreateFrame();
			detailsContainer.PositionOffset_X = 290;
			detailsContainer.SizeOffset_X = -detailsContainer.PositionOffset_X - 350;
			detailsContainer.SizeScale_X = 1;
			detailsContainer.SizeScale_Y = 1;
			infoContainer.AddChild(detailsContainer);

			mapContainer = Glazier.Get().CreateFrame();
			mapContainer.PositionOffset_X = -340;
			mapContainer.PositionScale_X = 1;
			mapContainer.SizeOffset_X = 340;
			mapContainer.SizeScale_Y = 1;
			infoContainer.AddChild(mapContainer);

			titleBox = Glazier.Get().CreateBox();
			titleBox.SizeOffset_Y = 84;
			titleBox.SizeScale_X = 1;
			container.AddChild(titleBox);

			titleIconImage = new SleekWebImage();
			titleIconImage.PositionOffset_X = 10;
			titleIconImage.PositionOffset_Y = 10;
			titleIconImage.SizeOffset_X = 64;
			titleIconImage.SizeOffset_Y = 64;
			titleBox.AddChild(titleIconImage);

			// Horizontal offset to keep title in line with off-center Details container.
			float titleAlignment = (playersContainer.SizeOffset_X - mapContainer.SizeOffset_X) / 2;

			titleNameLabel = Glazier.Get().CreateLabel();
			titleNameLabel.PositionOffset_X = titleAlignment;
			titleNameLabel.PositionOffset_Y = 5;
			titleNameLabel.SizeOffset_Y = 40;
			titleNameLabel.SizeScale_X = 1;
			titleNameLabel.FontSize = ESleekFontSize.Large;
			titleBox.AddChild(titleNameLabel);

			titleDescriptionLabel = Glazier.Get().CreateLabel();
			titleDescriptionLabel.PositionOffset_X = titleAlignment;
			titleDescriptionLabel.PositionOffset_Y = 35;
			titleDescriptionLabel.SizeOffset_Y = 34;
			titleDescriptionLabel.SizeScale_X = 1;
			titleDescriptionLabel.AllowRichText = true;
			titleDescriptionLabel.TextColor = ESleekTint.RICH_TEXT_DEFAULT;
			titleDescriptionLabel.TextContrastContext = ETextContrastContext.InconspicuousBackdrop;
			titleBox.AddChild(titleDescriptionLabel);

			titleCurationLabelsLabel = Glazier.Get().CreateLabel();
			titleCurationLabelsLabel.PositionOffset_X = titleAlignment;
			titleCurationLabelsLabel.PositionOffset_Y = 55;
			titleCurationLabelsLabel.SizeOffset_Y = 34;
			titleCurationLabelsLabel.SizeScale_X = 1;
			titleCurationLabelsLabel.AllowRichText = true;
			titleCurationLabelsLabel.TextColor = ESleekTint.RICH_TEXT_DEFAULT;
			titleCurationLabelsLabel.TextContrastContext = ETextContrastContext.InconspicuousBackdrop;
			titleBox.AddChild(titleCurationLabelsLabel);

			playerCountBox = Glazier.Get().CreateBox();
			playerCountBox.SizeScale_X = 1;
			playerCountBox.SizeOffset_Y = 30;
			playersContainer.AddChild(playerCountBox);

			playersScrollBox = Glazier.Get().CreateScrollView();
			playersScrollBox.PositionOffset_Y = 40;
			playersScrollBox.SizeScale_X = 1;
			playersScrollBox.SizeOffset_Y = -40;
			playersScrollBox.SizeScale_Y = 1;
			playersScrollBox.ScaleContentToWidth = true;
			playersContainer.AddChild(playersScrollBox);

			detailsBox = Glazier.Get().CreateBox();
			detailsBox.SizeScale_X = 1;
			detailsBox.SizeOffset_Y = 30;
			detailsBox.Text = localization.format("Details");
			detailsContainer.AddChild(detailsBox);

			detailsScrollBox = Glazier.Get().CreateScrollView();
			detailsScrollBox.PositionOffset_Y = 40;
			detailsScrollBox.SizeScale_X = 1;
			detailsScrollBox.SizeOffset_Y = -40;
			detailsScrollBox.SizeScale_Y = 1;
			detailsScrollBox.ScaleContentToWidth = true;
			detailsContainer.AddChild(detailsScrollBox);

			hostBanWarningButton = Glazier.Get().CreateButton();
			hostBanWarningButton.SizeOffset_Y = 60;
			hostBanWarningButton.SizeScale_X = 1;
			hostBanWarningButton.IsVisible = false;
			hostBanWarningButton.OnClicked += OnClickedHostBanWarning;
			detailsScrollBox.AddChild(hostBanWarningButton);

			notLoggedInWarningButton = Glazier.Get().CreateButton();
			notLoggedInWarningButton.SizeOffset_Y = 60;
			notLoggedInWarningButton.SizeScale_X = 1;
			notLoggedInWarningButton.IsVisible = false;
			notLoggedInWarningButton.OnClicked += OnClickedNotLoggedInWarning;
			notLoggedInWarningButton.Text += localization.format("NotLoggedInMessage");
			notLoggedInWarningButton.TextColor = ESleekTint.BAD;
			detailsScrollBox.AddChild(notLoggedInWarningButton);
			notLoggedInWarningButton.IsVisible = false;

			linksFrame = Glazier.Get().CreateFrame();
			linksFrame.PositionOffset_Y = 40;
			linksFrame.SizeScale_X = 1.0f;
			detailsScrollBox.AddChild(linksFrame);

			serverTitle = Glazier.Get().CreateBox();
			serverTitle.SizeOffset_Y = 30;
			serverTitle.SizeScale_X = 1;
			serverTitle.Text = localization.format("Server");
			detailsScrollBox.AddChild(serverTitle);

			serverBox = Glazier.Get().CreateBox();
			serverBox.PositionOffset_Y = 40;
			serverBox.SizeScale_X = 1;
			serverBox.SizeOffset_Y = 130;
			detailsScrollBox.AddChild(serverBox);

			serverWorkshopLabel = Glazier.Get().CreateLabel();
			serverWorkshopLabel.PositionOffset_X = 5;
			serverWorkshopLabel.PositionOffset_Y = 0;
			serverWorkshopLabel.SizeOffset_Y = 30;
			serverWorkshopLabel.SizeScale_X = 1;
			serverWorkshopLabel.TextAlignment = TextAnchor.MiddleLeft;
			serverBox.AddChild(serverWorkshopLabel);

			serverCombatLabel = Glazier.Get().CreateLabel();
			serverCombatLabel.PositionOffset_X = 5;
			serverCombatLabel.PositionOffset_Y = 20;
			serverCombatLabel.SizeOffset_Y = 30;
			serverCombatLabel.SizeScale_X = 1;
			serverCombatLabel.TextAlignment = TextAnchor.MiddleLeft;
			serverBox.AddChild(serverCombatLabel);

			serverPerspectiveLabel = Glazier.Get().CreateLabel();
			serverPerspectiveLabel.PositionOffset_X = 5;
			serverPerspectiveLabel.PositionOffset_Y = 40;
			serverPerspectiveLabel.SizeOffset_Y = 30;
			serverPerspectiveLabel.SizeScale_X = 1;
			serverPerspectiveLabel.TextAlignment = TextAnchor.MiddleLeft;
			serverBox.AddChild(serverPerspectiveLabel);

			serverSecurityLabel = Glazier.Get().CreateLabel();
			serverSecurityLabel.PositionOffset_X = 5;
			serverSecurityLabel.PositionOffset_Y = 60;
			serverSecurityLabel.SizeOffset_Y = 30;
			serverSecurityLabel.SizeScale_X = 1;
			serverSecurityLabel.TextAlignment = TextAnchor.MiddleLeft;
			serverBox.AddChild(serverSecurityLabel);

			serverModeLabel = Glazier.Get().CreateLabel();
			serverModeLabel.PositionOffset_X = 5;
			serverModeLabel.PositionOffset_Y = 80;
			serverModeLabel.SizeOffset_Y = 30;
			serverModeLabel.SizeScale_X = 1;
			serverModeLabel.TextAlignment = TextAnchor.MiddleLeft;
			serverBox.AddChild(serverModeLabel);

			serverCheatsLabel = Glazier.Get().CreateLabel();
			serverCheatsLabel.PositionOffset_X = 5;
			serverCheatsLabel.PositionOffset_Y = 100;
			serverCheatsLabel.SizeOffset_Y = 30;
			serverCheatsLabel.SizeScale_X = 1;
			serverCheatsLabel.TextAlignment = TextAnchor.MiddleLeft;
			serverBox.AddChild(serverCheatsLabel);

			serverMonetizationLabel = Glazier.Get().CreateLabel();
			serverMonetizationLabel.PositionOffset_X = 5;
			serverMonetizationLabel.PositionOffset_Y = 100;
			serverMonetizationLabel.SizeOffset_Y = 30;
			serverMonetizationLabel.SizeScale_X = 1;
			serverMonetizationLabel.TextAlignment = TextAnchor.MiddleLeft;
			serverBox.AddChild(serverMonetizationLabel);

			serverPingLabel = Glazier.Get().CreateLabel();
			serverPingLabel.PositionOffset_X = 5;
			serverPingLabel.PositionOffset_Y = 100;
			serverPingLabel.SizeOffset_Y = 30;
			serverPingLabel.SizeScale_X = 1;
			serverPingLabel.TextAlignment = TextAnchor.MiddleLeft;
			serverBox.AddChild(serverPingLabel);

			ugcTitle = Glazier.Get().CreateBox();
			ugcTitle.SizeOffset_Y = 30;
			ugcTitle.SizeScale_X = 1;
			ugcTitle.Text = localization.format("UGC");
			detailsScrollBox.AddChild(ugcTitle);
			ugcTitle.IsVisible = false;

			ugcBox = Glazier.Get().CreateBox();
			ugcBox.SizeScale_X = 1;
			detailsScrollBox.AddChild(ugcBox);
			ugcBox.IsVisible = false;

			configTitle = Glazier.Get().CreateBox();
			configTitle.SizeOffset_Y = 30;
			configTitle.SizeScale_X = 1;
			configTitle.Text = localization.format("Config");
			detailsScrollBox.AddChild(configTitle);
			configTitle.IsVisible = false;

			configBox = Glazier.Get().CreateBox();
			configBox.SizeScale_X = 1;
			detailsScrollBox.AddChild(configBox);
			configBox.IsVisible = false;

			rocketTitle = Glazier.Get().CreateBox();
			rocketTitle.SizeOffset_Y = 30;
			rocketTitle.SizeScale_X = 1;
			detailsScrollBox.AddChild(rocketTitle);
			rocketTitle.IsVisible = false;

			rocketBox = Glazier.Get().CreateBox();
			rocketBox.SizeScale_X = 1;
			detailsScrollBox.AddChild(rocketBox);
			rocketBox.IsVisible = false;

			mapNameBox = Glazier.Get().CreateBox();
			mapNameBox.SizeOffset_X = 340;
			mapNameBox.SizeOffset_Y = 30;
			mapContainer.AddChild(mapNameBox);

			mapPreviewBox = Glazier.Get().CreateBox();
			mapPreviewBox.PositionOffset_Y = 40;
			mapPreviewBox.SizeOffset_X = 340;
			mapPreviewBox.SizeOffset_Y = 200;
			mapContainer.AddChild(mapPreviewBox);

			// Preview.png is 320x180
			mapPreviewImage = Glazier.Get().CreateImage();
			mapPreviewImage.PositionOffset_X = 10;
			mapPreviewImage.PositionOffset_Y = 10;
			mapPreviewImage.SizeOffset_X = -20;
			mapPreviewImage.SizeOffset_Y = -20;
			mapPreviewImage.SizeScale_X = 1;
			mapPreviewImage.SizeScale_Y = 1;
			mapPreviewBox.AddChild(mapPreviewImage);

			mapDescriptionBox = Glazier.Get().CreateBox();
			mapDescriptionBox.PositionOffset_Y = 250;
			mapDescriptionBox.SizeOffset_X = 340;
			mapDescriptionBox.SizeOffset_Y = 140;
			mapDescriptionBox.TextAlignment = TextAnchor.UpperCenter;
			mapDescriptionBox.AllowRichText = true;
			mapDescriptionBox.TextColor = ESleekTint.RICH_TEXT_DEFAULT;
			mapContainer.AddChild(mapDescriptionBox);

			serverDescriptionBox = Glazier.Get().CreateBox();
			serverDescriptionBox.PositionOffset_Y = 400;
			serverDescriptionBox.SizeOffset_X = 340;
			serverDescriptionBox.SizeOffset_Y = -400;
			serverDescriptionBox.SizeScale_Y = 1;
			serverDescriptionBox.TextAlignment = TextAnchor.UpperCenter;
			serverDescriptionBox.AllowRichText = true;
			serverDescriptionBox.TextColor = ESleekTint.RICH_TEXT_DEFAULT;
			serverDescriptionBox.TextContrastContext = ETextContrastContext.InconspicuousBackdrop;
			mapContainer.AddChild(serverDescriptionBox);

			joinButton = Glazier.Get().CreateButton();
			joinButton.SizeOffset_X = -5;
			joinButton.SizeScale_X = 0.2f;
			joinButton.SizeScale_Y = 1;
			joinButton.Text = localization.format("Join_Button");
			joinButton.TooltipText = localization.format("Join_Button_Tooltip");
			joinButton.OnClicked += onClickedJoinButton;
			joinButton.FontSize = ESleekFontSize.Medium;
			buttonsContainer.AddChild(joinButton);

			joinDisabledBox = Glazier.Get().CreateBox();
			joinDisabledBox.SizeOffset_X = -5;
			joinDisabledBox.SizeScale_X = 0.2f;
			joinDisabledBox.SizeScale_Y = 1;
			joinDisabledBox.TextColor = ESleekTint.BAD;
			buttonsContainer.AddChild(joinDisabledBox);
			joinDisabledBox.IsVisible = false;

			favoriteButton = Glazier.Get().CreateButton();
			favoriteButton.PositionOffset_X = 5;
			favoriteButton.PositionScale_X = 0.2f;
			favoriteButton.SizeOffset_X = -10;
			favoriteButton.SizeScale_X = 0.2f;
			favoriteButton.SizeScale_Y = 1;
			favoriteButton.TooltipText = localization.format("Favorite_Button_Tooltip");
			favoriteButton.OnClicked += onClickedFavoriteButton;
			favoriteButton.FontSize = ESleekFontSize.Medium;
			buttonsContainer.AddChild(favoriteButton);

			bookmarkButton = new SleekButtonIcon(null, 40);
			bookmarkButton.PositionOffset_X = 5;
			bookmarkButton.PositionScale_X = 0.4f;
			bookmarkButton.SizeOffset_X = -10;
			bookmarkButton.SizeScale_X = 0.2f;
			bookmarkButton.SizeScale_Y = 1;
			bookmarkButton.tooltip = localization.format("Bookmark_Button_Tooltip");
			bookmarkButton.onClickedButton += OnClickedBookmarkButton;
			bookmarkButton.fontSize = ESleekFontSize.Medium;
			buttonsContainer.AddChild(bookmarkButton);

			refreshButton = Glazier.Get().CreateButton();
			refreshButton.PositionOffset_X = 5;
			refreshButton.PositionScale_X = 0.6f;
			refreshButton.SizeOffset_X = -10;
			refreshButton.SizeScale_X = 0.2f;
			refreshButton.SizeScale_Y = 1;
			refreshButton.Text = localization.format("Refresh_Button");
			refreshButton.TooltipText = localization.format("Refresh_Button_Tooltip");
			refreshButton.OnClicked += onClickedRefreshButton;
			refreshButton.FontSize = ESleekFontSize.Medium;
			buttonsContainer.AddChild(refreshButton);

			copyServerCodeButton = new SleekButtonIcon(MenuDashboardUI.icons.load<Texture2D>("Clipboard"), 40);
			copyServerCodeButton.PositionOffset_X = 5;
			copyServerCodeButton.SizeOffset_X = -10;
			copyServerCodeButton.SizeScale_Y = 1;
			copyServerCodeButton.text = localization.format("CopyServerCode_Label");
			copyServerCodeButton.tooltip = localization.format("CopyServerCode_Tooltip");
			copyServerCodeButton.onClickedButton += OnCopyServerCodeClicked;
			copyServerCodeButton.fontSize = ESleekFontSize.Medium;
			buttonsContainer.AddChild(copyServerCodeButton);

			cancelButton = Glazier.Get().CreateButton();
			cancelButton.PositionOffset_X = 5;
			cancelButton.PositionScale_X = 0.8f;
			cancelButton.SizeOffset_X = -5;
			cancelButton.SizeScale_X = 0.2f;
			cancelButton.SizeScale_Y = 1;
			cancelButton.Text = localization.format("Cancel_Button");
			cancelButton.TooltipText = localization.format("Cancel_Button_Tooltip");
			cancelButton.OnClicked += onClickedCancelButton;
			cancelButton.FontSize = ESleekFontSize.Medium;
			buttonsContainer.AddChild(cancelButton);

			Provider.provider.matchmakingService.onMasterServerQueryRefreshed += onMasterServerQueryRefreshed;
			Provider.provider.matchmakingService.onPlayersQueryRefreshed += onPlayersQueryRefreshed;
			Provider.provider.matchmakingService.onRulesQueryRefreshed += onRulesQueryRefreshed;

			if (ugcQueryCompleted == null)
			{
				ugcQueryCompleted = CallResult<SteamUGCQueryCompleted_t>.Create(onUGCQueryCompleted);
			}

			passwordUI = new MenuServerPasswordUI();
		}

		private static MenuServerPasswordUI passwordUI;
	}
}
