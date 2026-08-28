////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
//#define FAKELAG

using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif // UNITY_EDITOR
using UnityEngine.Networking;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Steamworks;
using System.IO;
using SDG.Provider;
using SDG.NetTransport;
using SDG.NetPak;
using global::Unturned.SystemEx;
using global::Unturned.UnityEx;
using System.Diagnostics;

namespace SDG.Unturned
{
#if FAKELAG
	struct FakeLagMessage
	{
		public ITransportConnection transportConnection;
		public byte[] data;
		public float timestamp;
	}
#endif

	public partial class Provider : MonoBehaviour
	{
		public static readonly string STEAM_IC = "Steam";
		public static readonly string STEAM_DC = "<color=#2784c6>Steam</color>";

		#region CONSTANTS

#if EXPERIMENTAL
		public static readonly AppId_t APP_ID = new AppId_t(407660);
#else
		public static readonly AppId_t APP_ID = new AppId_t(304930);
#endif
		public static readonly AppId_t PRO_ID = new AppId_t(306460);
		public static string APP_VERSION
		{
			get;
			protected set;
		}

		/// <summary>
		/// App version string packed into a 32-bit number for replication.
		/// </summary>
		public static uint APP_VERSION_PACKED
		{
			get;
			protected set;
		}
#if EXPERIMENTAL
		public static readonly string APP_NAME = "Unturned Experimental";
#else
		public static readonly string APP_NAME = "Unturned";
#endif
		public static readonly string APP_AUTHOR = "Nelson Sexton";

		public static readonly int CLIENT_TIMEOUT = 30;
		internal static readonly float PING_REQUEST_INTERVAL = 1f;

		#endregion

		#region SCREENSHOTS

		private static bool isCapturingScreenshot;

		private static StaticResourceRef<Material> screenshotBlitMaterial = new StaticResourceRef<Material>("Materials/ScreenshotBlit");

		private IEnumerator CaptureScreenshot()
		{
			bool useSupersampling = OptionsSettings.enableScreenshotSupersampling;
			// Unity has seemingly no max size with CaptureScreenshot, but downsampling relies on being
			// able to read in the screenshot as a texture which has max ~16k by 16k size.
			int maxSizeMultiplier = useSupersampling ? 4 : 16;
			int sizeMultiplier = Mathf.Clamp(OptionsSettings.screenshotSizeMultiplier, 1, maxSizeMultiplier);

			int finalWidth = Screen.width * sizeMultiplier;
			int finalHeight = Screen.height * sizeMultiplier;
			int maxSize = SystemInfo.maxTextureSize;
			if (finalWidth > maxSize || finalHeight > maxSize)
			{
				UnturnedLog.warn($"Unable to capture {finalWidth}x{finalHeight} screenshot because it exceeds max supported texture size ({maxSize})");
				isCapturingScreenshot = false;
				yield break;
			}

			if (sizeMultiplier > 1 || useSupersampling)
			{
				// If TAA is enabled during "super-sized" screenshot it will just be upscaled low-resolution screenshot.
				UnturnedPostProcess.instance.DisableAntiAliasingForScreenshot = true;
			}

			string screenshotsDirectory = PathEx.Join(UnturnedPaths.RootDirectory, "Screenshots");
			Directory.CreateDirectory(screenshotsDirectory);

			// Indicate in file name whether UI was visible during capture so that
			// loading screen can pick a nice cinematic screenshot.
			bool isHudVisible;
			if (Level.isEditor && EditorUI.window != null)
			{
				isHudVisible = EditorUI.window.isEnabled;
			}
			else if (Player.LocalPlayer != null && PlayerUI.window != null)
			{
				isHudVisible = PlayerUI.window.isEnabled;
			}
			else
			{
				isHudVisible = true;
			}

			string fileName = System.DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
			if (GraphicsSettings.WantsCinematicMode)
			{
				fileName += "_Cinematic";
			}
			if (!isHudVisible)
			{
				fileName += "_NoUI";
			}
			string filePath = Path.Combine(screenshotsDirectory, fileName + ".png");

			UnturnedLog.info($"Capturing {finalWidth}x{finalHeight} screenshot (Size Multiplier: {sizeMultiplier} Use Supersampling: {useSupersampling} HUD Visible: {isHudVisible})");

			if (useSupersampling)
			{
				// Regarding ScreenCapture Unity documentation says:
				//	"To get a reliable output from this method you must make sure it is called once the frame rendering has ended,
				//	and not during the rendering process. A simple way of ensuring this is to call it from a coroutine that yields
				//	on WaitForEndOfFrame. If you call this method during the rendering process you will get unpredictable and undefined results."
				yield return new WaitForEndOfFrame();

				const int supersampleFactor = 2;
				int superSize = sizeMultiplier * supersampleFactor;
				Texture2D supersampledTexture = ScreenCapture.CaptureScreenshotAsTexture(superSize);

				// Can re-enable regular AA now that the screenshot has been captured.
				UnturnedPostProcess.instance.DisableAntiAliasingForScreenshot = false;

				// Did not actually encounter this in testing, but just in case...
				if (supersampledTexture == null)
				{
					UnturnedLog.error("CaptureScreenshotAsTexture returned null");
					isCapturingScreenshot = false;
					yield break;
				}

				yield return null; // Time-slice work.

				supersampledTexture.filterMode = FilterMode.Bilinear; // Bilinear downsampling.

				// Blitting into a render texture will sample the entire source texture at the lower resolution.
				// 2023-01-31: for whatever reason it seems that CaptureScreenshotAsTexture has <= 1.0 alpha, whereas
				// CaptureScreenshot overrides 1.0 alpha. We now use a material during blit to override alpha. (public issue #3670)
				RenderTexture downsampleRenderTexture = RenderTexture.GetTemporary(finalWidth, finalHeight, /*depthBuffer*/ 0, supersampledTexture.graphicsFormat);
				Graphics.Blit(supersampledTexture, downsampleRenderTexture, screenshotBlitMaterial);

				yield return null; // Time-slice work.

				// Copy downsampled texture back to CPU.
				Texture2D downsampledTexture = new Texture2D(finalWidth, finalHeight, supersampledTexture.format, /*mipChain*/ false, /*linear*/ false);
				RenderTexture.active = downsampleRenderTexture;
				downsampledTexture.ReadPixels(new Rect(0, 0, finalWidth, finalHeight), 0, 0, /*recalculateMipMaps*/ false);
				RenderTexture.active = null;
				RenderTexture.ReleaseTemporary(downsampleRenderTexture);
				Destroy(supersampledTexture);

				yield return null; // Time-slice work.

				byte[] downsampledBytes = downsampledTexture.EncodeToPNG();
				Destroy(downsampledTexture);

				yield return null; // Time-slice work.

				// Re-export at downsampled resolution.
				File.WriteAllBytes(filePath, downsampledBytes);

				yield return null; // Time-slice work.
			}
			else
			{
				// Without supersampling we use Unity's capture-to-disk helper method which has async file write.
				ScreenCapture.CaptureScreenshot(filePath, sizeMultiplier);

				// Unity captures screenshot at the end of the frame, so wait for the next frame for file to exist.
				yield return null;

				// Can re-enable regular AA now that the screenshot has been captured.
				UnturnedPostProcess.instance.DisableAntiAliasingForScreenshot = false;

				float timePassed = 0.0f;
				while (true)
				{
					timePassed += Time.deltaTime;
					if (File.Exists(filePath))
					{
						break;
					}
					else
					{
						// Release builds of Unity (non-development) write the image async without a callback.
						if (timePassed < 10.0f)
						{
							yield return null; // Wait for next frame.
						}
						else
						{
							UnturnedLog.error($"Screenshot file is not available after {timePassed}s ({filePath})");
							isCapturingScreenshot = false;
							yield break;
						}
					}
				}
			}

			UnturnedLog.info("Captured screenshot: " + filePath);
			ScreenshotHandle handle = SteamScreenshots.AddScreenshotToLibrary(filePath, null, finalWidth, finalHeight);

			if (Level.info != null)
			{
				string locationName = Level.info.getLocalizedName();
				SteamScreenshots.SetLocation(handle, locationName);
				UnturnedLog.info($"Tagged location \"{locationName}\" in screenshot");
			}

			Camera mainCamera = MainCamera.instance;
			if (mainCamera != null)
			{
				Vector3 cameraPosition = mainCamera.transform.position;
				const float MAX_TAG_DISTANCE = 64.0f;
				const float SQR_MAX_TAG_DISTANCE = MAX_TAG_DISTANCE * MAX_TAG_DISTANCE;

				foreach (SteamPlayer client in clients)
				{
					if (client.player == null || client.player.channel.IsLocalPlayer)
					{
						continue;
					}

					Vector3 worldPosition = client.player.transform.position + Vector3.up;
					if ((worldPosition - cameraPosition).sqrMagnitude > SQR_MAX_TAG_DISTANCE)
					{
						// Prevent client from exploiting screenshots to find direction of other players.
						continue;
					}

					Vector3 viewportPoint = mainCamera.WorldToViewportPoint(worldPosition);
					if (viewportPoint.x < 0.0f || viewportPoint.x > 1.0f || viewportPoint.y < 0.0f || viewportPoint.y > 1.0f || viewportPoint.z < 0.0f)
					{
						// Outside view or behind camera.
						continue;
					}

					SteamScreenshots.TagUser(handle, client.playerID.steamID);
					UnturnedLog.info($"Tagged player \"{client.GetLocalDisplayName()}\" in screenshot");
				}
			}

			isCapturingScreenshot = false;
		}

		public static void RequestScreenshot()
		{
			if (isCapturingScreenshot)
				return;

			isCapturingScreenshot = true;
			steam.StartCoroutine(steam.CaptureScreenshot());
		}

#pragma warning disable
		private static Callback<ScreenshotRequested_t> screenshotRequestedCallback;
#pragma warning restore
		private static void OnSteamScreenshotRequested(ScreenshotRequested_t callback)
		{
			UnturnedLog.info("Steam overlay screenshot requested");
			RequestScreenshot();
		}

		#endregion

		#region LOCALIZATION

		private static string privateLanguage;
		public static string language
		{
			get => privateLanguage;
			private set
			{
				privateLanguage = value;
				languageIsEnglish = value == "English";
			}
		}
		internal static bool languageIsEnglish;

		// path to local folder (workshop or default local folder) IMPORTANT because there are multiple ways to load localization
		private static string _path;
		public static string path => _path;

		/// <summary>
		/// Path to directory containing "Editor", "Menu", "Player", "Curse_Words.txt", etc files.
		/// </summary>
		public static string localizationRoot
		{
			get;
			private set;
		}

		public static Local localization;

		public static List<string> streamerNames
		{
			get;
			private set;
		}

		#endregion

#if WITH_THIRDPARTYAC
		#region THIRDPARTYAC

		static partial void AddClientToThirdpartyAntiCheat(ITransportConnection clientId, SteamPlayerID playerID, SteamPlayer newClient);
		static partial void RemoveClientFromThirdpartyAntiCheat(SteamPlayer clientToRemove);
		static partial void ShutdownThirdpartyAntiCheatServer();
		static partial void ShutdownThirdpartyAntiCheatClient();
		private static partial bool InitThirdpartyAntiCheatServer();
		static partial void RunThirdpartyAntiCheatFrame();
		private static partial bool CheckThirdpartyAntiCheatWantsRestart();

		#endregion // THIRDPARTYAC
#endif

		#region RICH_PRESENCE

		/// <summary>
		/// Call whenever something impacting rich presence changes for example loading a server or changing lobbies.
		/// </summary>
		public static void updateRichPresence()
		{
			if (Dedicator.IsDedicatedServer)
			{
				return;
			}

			updateSteamRichPresence();
		}

		private static void updateSteamRichPresence()
		{
			if (OptionsSettings.ShouldHideRichPresence)
			{
				SteamFriends.ClearRichPresence();
				return;
			}

			if (Level.info != null)
			{
				if (Level.isEditor)
				{
					provider.communityService.setStatus(localization.format("Rich_Presence_Editing", Level.info.getLocalizedName()));

					SteamFriends.SetRichPresence("steam_display", "#Status_EditingLevel");
					SteamFriends.SetRichPresence("steam_player_group", string.Empty);
				}
				else
				{
					provider.communityService.setStatus(localization.format("Rich_Presence_Playing", Level.info.getLocalizedName()));

					if (isConnected && !isServer && server.m_SteamID > 0)
					{
						SteamFriends.SetRichPresence("steam_display", "#Status_PlayingMultiplayer");
						SteamFriends.SetRichPresence("steam_player_group", server.ToString());
					}
					else
					{
						SteamFriends.SetRichPresence("steam_display", "#Status_PlayingSingleplayer");
						SteamFriends.SetRichPresence("steam_player_group", string.Empty);
					}
				}

				// We format %level_name% into the editing and playing messages.
				SteamFriends.SetRichPresence("level_name", Level.info.getLocalizedName());
			}
			else
			{
				if (Lobbies.inLobby)
				{
					provider.communityService.setStatus(localization.format("Rich_Presence_Lobby"));

					SteamFriends.SetRichPresence("steam_display", "#Status_WaitingInLobby");
					SteamFriends.SetRichPresence("steam_player_group", Lobbies.currentLobby.ToString());
				}
				else
				{
					provider.communityService.setStatus(localization.format("Rich_Presence_Menu"));

					SteamFriends.SetRichPresence("steam_display", "#Status_AtMainMenu");
					SteamFriends.SetRichPresence("steam_player_group", string.Empty);
				}
			}
		}

		#endregion

		#region NETWORKING

		private static uint _bytesSent;
		public static uint bytesSent => _bytesSent;

		private static uint _bytesReceived;
		public static uint bytesReceived => _bytesReceived;

		private static uint _packetsSent;
		public static uint packetsSent => _packetsSent;

		private static uint _packetsReceived;
		public static uint packetsReceived => _packetsReceived;

		private static SteamServerAdvertisement _currentServerAdvertisement;

		/// <summary>
		/// Only used on client.
		/// Information about current game server retrieved through Steam's "A2S" query system.
		/// Available when joining using the Steam server list API (in-game server browser)
		/// or querying the Server's A2S port directly (connect by IP menu), but not when
		/// joining by Steam ID.
		/// </summary>
		public static SteamServerAdvertisement CurrentServerAdvertisement => _currentServerAdvertisement;

		private static ServerConnectParameters _currentServerConnectParameters;
		public static ServerConnectParameters CurrentServerConnectParameters => _currentServerConnectParameters;

		/// <summary>
		/// On client, is current server protected by VAC?
		/// Set after initial response is received.
		/// </summary>
		internal static bool IsVacActiveOnCurrentServer
		{
			get => isVacActive;
		}

		/// <summary>
		/// On client, is current server protected by thirdparty anti-cheat?
		/// Set after initial response is received.
		/// </summary>
		internal static bool IsThirdpartyAntiCheatActiveOnCurrentServer
		{
			get => isThirdpartyAntiCheatActive;
		}

		private static CSteamID _server;
		public static CSteamID server => _server;

		private static CSteamID _client;
		public static CSteamID client => _client;

		private static CSteamID _user;
		public static CSteamID user => _user;

		private static byte[] _clientHash;
		public static byte[] clientHash => _clientHash;

		private static string _clientName;
		public static string clientName => _clientName;

		internal static List<SteamPlayer> _clients = new List<SteamPlayer>();
		internal static Dictionary<ITransportConnection, SteamPlayer> _transportConnectionToPlayerMap = new Dictionary<ITransportConnection, SteamPlayer>();
		public static List<SteamPlayer> clients => _clients;

		/// <summary>
		/// Counts "bad" packets per-connection. Bad packets *may* be legitimate, for example a delayed burst of ping
		/// requests. Beyond a certain point, however, it's likely a cheater is trying to waste server processing time.
		/// </summary>
		private static TransportConnectionRateLimiter badMessageRateLimiter = new TransportConnectionRateLimiter();

		public static PooledTransportConnectionList GatherClientConnections()
		{
			PooledTransportConnectionList list = TransportConnectionListPool.Get();
			foreach (SteamPlayer client in _clients)
			{
				list.Add(client.transportConnection);
			}
			return list;
		}

		[System.Obsolete("Replaced by GatherClientConnections")]
		public static IEnumerable<ITransportConnection> EnumerateClients()
		{
			return GatherClientConnections();
		}

		public static PooledTransportConnectionList GatherClientConnectionsMatchingPredicate(System.Predicate<SteamPlayer> predicate)
		{
			PooledTransportConnectionList list = TransportConnectionListPool.Get();
			foreach (SteamPlayer client in _clients)
			{
				if (predicate(client))
				{
					list.Add(client.transportConnection);
				}
			}
			return list;
		}

		[System.Obsolete("Replaced by GatherClientConnectionsMatchingPredicate")]
		public static IEnumerable<ITransportConnection> EnumerateClients_Predicate(System.Predicate<SteamPlayer> predicate)
		{
			return GatherClientConnectionsMatchingPredicate(predicate);
		}

		public static PooledTransportConnectionList GatherClientConnectionsWithinSphere(Vector3 position, float radius)
		{
			PooledTransportConnectionList list = TransportConnectionListPool.Get();
			float sqrRadius = radius * radius;
			foreach (SteamPlayer client in _clients)
			{
				if (client.player != null && (client.player.transform.position - position).sqrMagnitude < sqrRadius)
				{
					list.Add(client.transportConnection);
				}
			}
			return list;
		}

		[System.Obsolete("Replaced by GatherClientConnectionsWithinSphere")]
		public static IEnumerable<ITransportConnection> EnumerateClients_WithinSphere(Vector3 position, float radius)
		{
			return GatherClientConnectionsWithinSphere(position, radius);
		}

		public static PooledTransportConnectionList GatherRemoteClientConnectionsWithinSphere(Vector3 position, float radius)
		{
			PooledTransportConnectionList list = TransportConnectionListPool.Get();
			float sqrRadius = radius * radius;
			foreach (SteamPlayer client in _clients)
			{
#if !DEDICATED_SERVER
				if (client.IsLocalServerHost)
					continue;
#endif // !DEDICATED_SERVER

				if (client.player != null && (client.player.transform.position - position).sqrMagnitude < sqrRadius)
				{
					list.Add(client.transportConnection);
				}
			}
			return list;
		}

		[System.Obsolete("Replaced by GatherRemoteClientConnectionsWithinSphere")]
		public static IEnumerable<ITransportConnection> EnumerateClients_RemoteWithinSphere(Vector3 position, float radius)
		{
			return GatherRemoteClientConnectionsWithinSphere(position, radius);
		}

		public static PooledTransportConnectionList GatherRemoteClientConnections()
		{
			PooledTransportConnectionList list = TransportConnectionListPool.Get();
			foreach (SteamPlayer client in _clients)
			{
#if !DEDICATED_SERVER
				if (client.IsLocalServerHost)
					continue;
#endif // !DEDICATED_SERVER

				list.Add(client.transportConnection);
			}
			return list;
		}

		[System.Obsolete("Replaced by GatherRemoteClientConnections")]
		public static IEnumerable<ITransportConnection> EnumerateClients_Remote()
		{
			return GatherRemoteClientConnections();
		}

		public static PooledTransportConnectionList GatherRemoteClientConnectionsMatchingPredicate(System.Predicate<SteamPlayer> predicate)
		{
			PooledTransportConnectionList list = TransportConnectionListPool.Get();
			foreach (SteamPlayer client in _clients)
			{
#if !DEDICATED_SERVER
				if (client.IsLocalServerHost)
					continue;
#endif // !DEDICATED_SERVER

				if (predicate(client))
				{
					list.Add(client.transportConnection);
				}
			}
			return list;
		}

		[System.Obsolete("Replaced by GatherRemoteClientsMatchingPredicate")]
		public static IEnumerable<ITransportConnection> EnumerateClients_RemotePredicate(System.Predicate<SteamPlayer> predicate)
		{
			return GatherRemoteClientConnectionsMatchingPredicate(predicate);
		}

		/// <summary>
		/// Exposed for Rocket transition to modules backwards compatibility.
		/// </summary>
		[System.Obsolete]
		public static List<SteamPlayer> players => clients;

		public static List<SteamPending> pending = new List<SteamPending>();
		internal static Dictionary<ITransportConnection, SteamPending> _transportConnectionToPendingPlayerMap = new Dictionary<ITransportConnection, SteamPending>();

		private static bool _isServer;
		public static bool isServer => _isServer;

		private static bool _isClient;
		public static bool isClient => _isClient;

		private static bool _isPro;
		public static bool isPro => _isPro;

		private static bool _isConnected;
		public static bool isConnected => _isConnected;

		internal static bool isWaitingForWorkshopResponse;
#if !DEDICATED_SERVER
		/// <summary>
		/// After client submits EServerMessage.Authenticate we are waiting
		/// for the EClientMessage.Accepted response.
		/// </summary>
		internal static bool isWaitingForAuthenticationResponse;

		/// <summary>
		/// Realtime that client sent EServerMessage.Authenticate request.
		/// </summary>
		internal static double sentAuthenticationRequestTime;
#endif // !DEDICATED_SERVER

		/// <summary>
		/// File IDs the client thinks the server advertised it was using, or null if UGC response was pending.
		/// Prevents the server from advertising a smaller or fake list of items.
		/// </summary>
		private static List<PublishedFileId_t> waitingForExpectedWorkshopItems;

		private static bool doServerItemsMatchAdvertisement(List<PublishedFileId_t> pendingWorkshopItems)
		{
			if (waitingForExpectedWorkshopItems == null)
			{
				// In this case the client did not finish querying the server advertisement,
				// so we have to assume the advertisement is valid.
				return true;
			}

			if (waitingForExpectedWorkshopItems.Count < pendingWorkshopItems.Count)
			{
				// Server advertised less items than it requested us to download.
				return false;
			}

			foreach (PublishedFileId_t id in pendingWorkshopItems)
			{
				bool wasAdvertised = waitingForExpectedWorkshopItems.Contains(id);
				if (wasAdvertised == false)
				{
					// Server asked us to download an item that was not advertised.
					return false;
				}
			}

			// We don't care if the server advertised more items than it was actually using.
			return true;
		}

		/// <summary>
		/// Needed before loading level.
		/// </summary>
		internal static ENPCHoliday authorityHoliday;
		private static CachedWorkshopResponse currentServerWorkshopResponse;

		internal static void receiveWorkshopResponse(CachedWorkshopResponse response)
		{
			authorityHoliday = response.holiday;
			currentServerWorkshopResponse = response;
			isWaitingForWorkshopResponse = false;

			serverName = response.serverName;
			map = response.levelName;
			isPvP = response.isPvP;
			mode = response.gameMode;
			cameraMode = response.cameraMode;
			maxPlayers = response.maxPlayers;
			isVacActive = response.isVACSecure;
#if WITH_THIRDPARTYAC
			isThirdpartyAntiCheatActive = response.isThirdpartyAntiCheatEnabled;
#endif

#if !DEDICATED_SERVER
			ServerBookmarkDetails bookmarkDetails = ServerBookmarksManager.FindBookmarkDetails(response.server);
			if (bookmarkDetails != null)
			{
				bookmarkDetails.UpdateFromWorkshopResponse(response);
				ServerBookmarksManager.MarkDirty();
			}
#endif // !DEDICATED_SERVER

			List<PublishedFileId_t> queryIDs = new List<PublishedFileId_t>(response.requiredFiles.Count);

			if (!shouldIgnoreServerWorkshopFiles)
			{

				foreach (ServerRequiredWorkshopFile file in response.requiredFiles)
				{
					if (file.fileId == 0)
					{
						// For some reason server has a null item?
						// We don't bother trying to download it.
						continue;
					}

					queryIDs.Add(new PublishedFileId_t(file.fileId));
				}
			}

			provider.workshopService.resetServerInvalidItems();

			if (CurrentServerAdvertisement != null)
			{
				if (!string.IsNullOrEmpty(CurrentServerAdvertisement.map))
				{
					if (!string.Equals(CurrentServerAdvertisement.map, response.levelName, StringComparison.OrdinalIgnoreCase))
					{
						// Server advertised map is not the same as requested map.
						_connectionFailureInfo = ESteamConnectionFailureInfo.SERVER_MAP_ADVERTISEMENT_MISMATCH;
						RequestDisconnect($"server map advertisement mismatch (Advertisement: \"{CurrentServerAdvertisement.map}\" Response: \"{response.levelName}\")");
						return;
					}
				}

				// Nelson 2023-12-23: Turning off this check for now because it seems gameserveritem_t.m_bSecure can vary
				// in some unexpected ways. For example, join and get kicked for this reason and join again and it works. Maybe
				// whether Steam returns as secure depends on some backend VAC status?
				/*
				if (CurrentServerAdvertisement.IsVACSecure != response.isVACSecure)
				{
					_connectionFailureInfo = ESteamConnectionFailureInfo.SERVER_VAC_ADVERTISEMENT_MISMATCH;
					RequestDisconnect($"server VAC advertisement mismatch (Advertisement: {CurrentServerAdvertisement.IsVACSecure} Response: {response.isVACSecure})");
					return;
				}
				*/

#if WITH_THIRDPARTYAC
				if (CurrentServerAdvertisement.IsThirdpartyAntiCheatEnabled != response.isThirdpartyAntiCheatEnabled)
				{
					_connectionFailureInfo = ESteamConnectionFailureInfo.SERVER_THIRDPARTYAC_ADVERTISEMENT_MISMATCH;
					RequestDisconnect($"server third-party anti-cheat advertisement mismatch (Advertisement: {CurrentServerAdvertisement.IsThirdpartyAntiCheatEnabled} Response: {response.isThirdpartyAntiCheatEnabled})");
					return;
				}
#endif // WITH_THIRDPARTYAC

				if (CurrentServerAdvertisement.maxPlayers != response.maxPlayers)
				{
					_connectionFailureInfo = ESteamConnectionFailureInfo.SERVER_MAXPLAYERS_ADVERTISEMENT_MISMATCH;
					RequestDisconnect($"server max players advertisement mismatch (Advertisement: {CurrentServerAdvertisement.maxPlayers} Response: {response.maxPlayers})");
					return;
				}

				if (CurrentServerAdvertisement.cameraMode != response.cameraMode)
				{
					_connectionFailureInfo = ESteamConnectionFailureInfo.SERVER_CAMERAMODE_ADVERTISEMENT_MISMATCH;
					RequestDisconnect($"server camera mode advertisement mismatch (Advertisement: {CurrentServerAdvertisement.cameraMode} Response: {response.cameraMode})");
					return;
				}

				if (CurrentServerAdvertisement.isPvP != response.isPvP)
				{
					_connectionFailureInfo = ESteamConnectionFailureInfo.SERVER_PVP_ADVERTISEMENT_MISMATCH;
					RequestDisconnect($"server PvP advertisement mismatch (Advertisement: {CurrentServerAdvertisement.isPvP} Response: {response.isPvP})");
					return;
				}
			}

			if (queryIDs.Count < 1)
			{
				UnturnedLog.info("Server specified no workshop items, launching");
				launch();
			}
			else
			{
				bool serverAdvertisedWorkshopItems = CurrentServerAdvertisement?.isWorkshop ?? true;
				if (serverAdvertisedWorkshopItems && doServerItemsMatchAdvertisement(queryIDs))
				{
					// We don't currently support interrupting the workshop files download / assets load.
					// Handling is paused until launch() is called.
					canCurrentlyHandleClientTransportFailure = false;

					UnturnedLog.info("Server specified {0} workshop item(s), querying details", queryIDs.Count);
					provider.workshopService.queryServerWorkshopItems(queryIDs, response.ip);
				}
				else
				{
					// Server advertised it was not using workshop items, but requested we download some.
					// Some hosts use plugins to hide their workshop usage.
					_connectionFailureInfo = ESteamConnectionFailureInfo.WORKSHOP_ADVERTISEMENT_MISMATCH;
					RequestDisconnect("workshop advertisement mismatch");
				}
			}
		}

		private static List<ulong> _serverWorkshopFileIDs = new List<ulong>();

		internal struct ServerRequiredWorkshopFile
		{
			public ulong fileId;
			public System.DateTime timestamp;
		}
		internal static List<ServerRequiredWorkshopFile> serverRequiredWorkshopFiles = new List<ServerRequiredWorkshopFile>();

		/// <summary>
		/// Only safe to use serverside.
		/// Get the list of workshop ids that a client needs to download when joining.
		/// </summary>
		public static List<ulong> getServerWorkshopFileIDs()
		{
			return _serverWorkshopFileIDs;
		}

		/// <summary>
		/// Only safe to use serverside.
		/// Lets clients know that this workshop id is being used on the server, and that they need to download it when joining.
		/// </summary>
		public static void registerServerUsingWorkshopFileId(ulong id)
		{
			registerServerUsingWorkshopFileId(id, 0);
		}

		internal static void registerServerUsingWorkshopFileId(ulong id, uint timestamp)
		{
			if (_serverWorkshopFileIDs.Contains(id))
				return;

			_serverWorkshopFileIDs.Add(id);
			ServerRequiredWorkshopFile requiredFile = new ServerRequiredWorkshopFile() { fileId = id, timestamp = DateTimeEx.FromUtcUnixTimeSeconds(timestamp) };
			UnturnedLog.info($"Workshop file {id} requiring timestamp {requiredFile.timestamp.ToLocalTime()}");
			serverRequiredWorkshopFiles.Add(requiredFile);
		}

		public static bool isLoadingUGC;
		public static bool isLoadingInventory; // public because provider.inv
		public static bool isLoading => isLoadingUGC;

		[Obsolete]
		public static int channels => 0;

		private static int nextPlayerChannelId = 2;
		/// <summary>
		/// Channel id was 32-bits, but now that it is in the RPC header it can be 8-bits since there never that many
		/// players online. The "manager" components are on channel 1, and each player has a channel.
		/// </summary>
		private static int allocPlayerChannelId()
		{
			const int maxID = byte.MaxValue;
			for (int attempt = 0; attempt < maxID; ++attempt)
			{
				int pendingId = nextPlayerChannelId;

				++nextPlayerChannelId;
				if (nextPlayerChannelId > maxID)
				{
					// 0 is reserved as none, 1 is used by managers, so next ID should be 2.
					nextPlayerChannelId = 2;
				}

				SteamChannel existingComponent = findChannelComponent(pendingId);
				if (existingComponent == null)
				{
					return pendingId;
				}
			}

			CommandWindow.LogErrorFormat("Fatal error! Ran out of player RPC channel IDs");
			shutdown(1, "Fatal error! Ran out of player RPC channel IDs");
			return 2;
		}

		public static ESteamConnectionFailureInfo _connectionFailureInfo;
		public static ESteamConnectionFailureInfo connectionFailureInfo
		{
			get => _connectionFailureInfo;

			set => _connectionFailureInfo = value;
		}

		internal static string _connectionFailureReason;
		public static string connectionFailureReason
		{
			get => _connectionFailureReason;
			set => _connectionFailureReason = value;
		}

		internal static uint _connectionFailureDuration;
		public static uint connectionFailureDuration => _connectionFailureDuration;

		private static List<SteamChannel> _receivers = new List<SteamChannel>();
		public static List<SteamChannel> receivers => _receivers;

		internal static byte[] buffer = new byte[Block.BUFFER_SIZE];

		internal static List<SDG.Framework.Modules.Module> critMods = new List<SDG.Framework.Modules.Module>();
		private static System.Text.StringBuilder modBuilder = new System.Text.StringBuilder();

#if WITH_THIRDPARTYAC
		private static int nextThirdpartyAntiCheatPlayerId = 1;
		private static int AllocThirdpartyAntiCheatPlayerId()
		{
			int id = nextThirdpartyAntiCheatPlayerId;
			++nextThirdpartyAntiCheatPlayerId;
			return id;
		}
#endif // WITH_THIRDPARTYAC

		public static void resetConnectionFailure()
		{
			_connectionFailureInfo = ESteamConnectionFailureInfo.NONE;
			_connectionFailureReason = "";
			_connectionFailureDuration = 0;
		}

		[System.Diagnostics.Conditional("LOG_NETCHANNEL")]
		private static void LogNetChannel(string format, params object[] args)
		{
			UnturnedLog.info(format, args);
		}

		public static void openChannel(SteamChannel receiver)
		{
			receivers.Add(receiver);
			LogNetChannel("Channel {0} opened ({1})", receiver.id, receiver.name);
		}

		public static void closeChannel(SteamChannel receiver)
		{
			receivers.RemoveFast(receiver);
			LogNetChannel("Channel {0} closed ({1})", receiver.id, receiver.name);
		}

		internal static SteamChannel findChannelComponent(int id)
		{
			for (int index = receivers.Count - 1; index >= 0; --index)
			{
				SteamChannel component = receivers[index];
				if (component == null)
				{
					// Should not happen, but an exception or plugin might break cleanup so we play it safe.
					receivers.RemoveAtFast(index);
					continue;
				}

				if (component.id == id)
				{
					return component;
				}
			}

			return null;
		}

		/// <summary>
		/// Should the network transport layer accept incoming connections?
		/// If both the queue and connected slots are full then incoming connections are ignored.
		/// </summary>
		public static bool hasRoomForNewConnection => clients.Count < maxPlayers || pending.Count < queueSize;

		/// <summary>
		/// includeQueuedPlayers ensures player won't be kicked because someone on the same IP joined after them.
		/// </summary>
		public static bool IsBlockedByMaxClientsWithSameIpAddressRule(ITransportConnection transportConnection,
			bool includeQueuedPlayers)
		{
			if (configData.Server.Use_FakeIP)
			{
				return false;
			}

			if (!transportConnection.TryGetIPv4Address(out uint address))
			{
				return false;
			}

			int max = configData.Server.Max_Clients_With_Same_IP_Address;
			int otherClientCount = 0;

			if (includeQueuedPlayers)
			{
				foreach (KeyValuePair<ITransportConnection, SteamPending> kvp in _transportConnectionToPendingPlayerMap)
				{
					ITransportConnection otherTransportConnection = kvp.Key;
					if (!transportConnection.Equals(otherTransportConnection))
					{
						if (otherTransportConnection.TryGetIPv4Address(out uint otherAddress))
						{
							if (address == otherAddress)
							{
								++otherClientCount;
								if (otherClientCount >= max)
								{
									break;
								}
							}
						}
					}
				}
			}

			if (otherClientCount < max)
			{
				foreach (KeyValuePair<ITransportConnection, SteamPlayer> kvp in _transportConnectionToPlayerMap)
				{
					ITransportConnection otherTransportConnection = kvp.Key;
					if (!transportConnection.Equals(otherTransportConnection))
					{
						if (otherTransportConnection.TryGetIPv4Address(out uint otherAddress))
						{
							if (address == otherAddress)
							{
								++otherClientCount;
								if (otherClientCount >= max)
								{
									break;
								}
							}
						}
					}
				}
			}

			if (otherClientCount + 1 > max)
			{
				// Adding this client would exceed the limit.

				if (configData.Server.Max_Clients_With_Same_IP_Address_Log_Warnings)
				{
					CommandWindow.LogWarning($"Connection {transportConnection} hit limit ({max}) for max clients with same IP address");
				}

				return true;
			}
			else
			{
				return false;
			}
		}

		/// <summary>
		/// Find player in the queue associated with a client connection.
		/// </summary>
		public static SteamPending findPendingPlayer(ITransportConnection transportConnection)
		{
			if (ReferenceEquals(transportConnection, null))
				return null;

			_transportConnectionToPendingPlayerMap.TryGetValue(transportConnection, out SteamPending pendingPlayer);
			return pendingPlayer;
		}

		internal static SteamPending findPendingPlayerBySteamId(CSteamID steamId)
		{
			foreach (SteamPending pendingPlayer in pending)
			{
				if (pendingPlayer.playerID.steamID == steamId)
				{
					return pendingPlayer;
				}
			}

			return null;
		}

		/// <summary>
		/// Find player associated with a client connection.
		/// </summary>
		public static SteamPlayer findPlayer(ITransportConnection transportConnection)
		{
			if (ReferenceEquals(transportConnection, null))
				return null;

			_transportConnectionToPlayerMap.TryGetValue(transportConnection, out SteamPlayer player);
			return player;
		}

		/// <summary>
		/// Find net transport layer connection associated with a client steam id. This could be a pending player in the
		/// queue, or a fully connected player.
		/// </summary>
		public static ITransportConnection findTransportConnection(CSteamID steamId)
		{
			foreach (SteamPlayer player in clients)
			{
				if (player.playerID.steamID == steamId)
				{
					return player.transportConnection;
				}
			}

			foreach (SteamPending pendingPlayer in pending)
			{
				if (pendingPlayer.playerID.steamID == steamId)
				{
					return pendingPlayer.transportConnection;
				}
			}

			return null;
		}

		/// <summary>
		/// Find player steam id associated with connection, otherwise nil if not found.
		/// </summary>
		public static CSteamID findTransportConnectionSteamId(ITransportConnection transportConnection)
		{
			SteamPlayer player = findPlayer(transportConnection);
			if (player != null)
			{
				return player.playerID.steamID;
			}

			SteamPending pending = findPendingPlayer(transportConnection);
			if (pending != null)
			{
				return pending.playerID.steamID;
			}

			if (transportConnection.TryGetSteamId(out ulong steamId))
			{
				return new CSteamID(steamId);
			}

			return CSteamID.Nil;
		}

		internal static NetId ClaimNetIdBlockForNewPlayer()
		{
			return NetIdRegistry.ClaimBlock(17);
		}

		internal static SteamPlayer addPlayer(ITransportConnection transportConnection, NetId netId, SteamPlayerID playerID, Vector3 point, byte angle, bool isPro, bool isAdmin, int channel, byte face, byte hair, byte beard, Color skin, Color color, Color markerColor, Color beardColor, bool hand, int shirtItem, int pantsItem, int hatItem, int backpackItem, int vestItem, int maskItem, int glassesItem, int[] skinItems, string[] skinTags, string[] skinDynamicProps, EPlayerSkillset skillset, string language, CSteamID lobbyID, EClientPlatform clientPlatform)
		{
			if (!Dedicator.IsDedicatedServer && playerID.steamID != client)
			{
				SteamFriends.SetPlayedWith(playerID.steamID);
			}

			if (playerID.steamID == client)
			{
				// Spawning player for self which has its own audio listener.
				if (Level.placeholderAudioListener != null)
				{
					Destroy(Level.placeholderAudioListener);
					Level.placeholderAudioListener = null;
				}
			}

			Transform model = null;

			try
			{
				model = gameMode.getPlayerGameObject(playerID).transform;
				model.position = point;
				model.rotation = Quaternion.Euler(0, angle * 2, 0);
			}
			catch (System.Exception exception)
			{
				UnturnedLog.error("Exception thrown when getting player from game mode:");
				UnturnedLog.exception(exception);
			}

			SteamPlayer newClient = null;

			try
			{
				newClient = new SteamPlayer(transportConnection, netId, playerID, model, isPro, isAdmin, channel, face, hair, beard, skin, color, markerColor, beardColor, hand, shirtItem, pantsItem, hatItem, backpackItem, vestItem, maskItem, glassesItem, skinItems, skinTags, skinDynamicProps, skillset, language, lobbyID, clientPlatform);
				clients.Add(newClient);
			}
			catch (System.Exception exception)
			{
				UnturnedLog.error("Exception thrown when adding player:");
				UnturnedLog.exception(exception);
			}

			if (!ReferenceEquals(transportConnection, null) && newClient != null)
			{
				_transportConnectionToPlayerMap.Add(transportConnection, newClient);
			}

			updateRichPresence();

			broadcastEnemyConnected(newClient);

			//UnturnedLog.info("Added player: {0}", playerID.steamID);
			return newClient;
		}

		internal static void RemoveClient(SteamPlayer clientToRemove)
		{
			if (!clients.Contains(clientToRemove))
			{
				UnturnedLog.warn($"RemoveClient called but not in list: {clientToRemove}");
				return;
			}

#if WITH_THIRDPARTYAC
			RemoveClientFromThirdpartyAntiCheat(clientToRemove);
#endif

			if (Dedicator.IsDedicatedServer)
			{
				clientToRemove.transportConnection.CloseConnection();
			}

			broadcastEnemyDisconnected(clientToRemove);

			try
			{
				clientToRemove.player.ReleaseNetIdBlock();
			}
			catch (System.Exception exception)
			{
				// Nelson 2024-11-14: Catching here to help mitigate public issue #4760.
				UnturnedLog.exception(exception, "Caught exception releasing player Net ID block:");
			}

			if (clientToRemove.model != null)
			{
				EffectManager.ClearAttachments(clientToRemove.model);

				clientToRemove.player.isExpectingDestroy = true;
				Destroy(clientToRemove.model.gameObject);
			}

			NetIdRegistry.Release(clientToRemove.GetNetId());

			if (!ReferenceEquals(clientToRemove.transportConnection, null))
			{
				_transportConnectionToPlayerMap.Remove(clientToRemove.transportConnection);
			}

			CSteamID steamId = clientToRemove.playerID.steamID;

			clients.Remove(clientToRemove);

			foreach (SteamPlayer otherClient in clients)
			{
				otherClient.culledPlayers.Remove(steamId);
			}

			verifyNextPlayerInQueue();

			updateRichPresence();

			//UnturnedLog.info("Removed player: {0}", player.playerID.steamID);
		}

		[System.Obsolete("Shouldn't have been used when it's internal!")]
		internal static void removePlayer(byte index)
		{
			if (index < 0 || index >= clients.Count)
			{
				UnturnedLog.error("Failed to find player: " + index);
				return;
			}

			SteamPlayer clientToRemove = clients[index];
			RemoveClient(clientToRemove);
		}

		private static void ReplicateRemoveClient(SteamPlayer clientToRemove)
		{
			NetMessages.SendMessageToClients(EClientMessage.PlayerDisconnected, ENetReliability.Reliable,
			GatherRemoteClientConnectionsMatchingPredicate((SteamPlayer potentialRecipient) =>
			{
				return potentialRecipient != clientToRemove;
			}),
			(NetPakWriter writer) =>
			{
				writer.WriteNetId(clientToRemove.GetNetId());
			});
		}

		[System.Obsolete("Shouldn't have been used in the first place because it's private!")]
		private static void replicateRemovePlayer(CSteamID skipSteamID, byte removalIndex)
		{
			SteamPlayer clientToRemove = clients[removalIndex];
			ReplicateRemoveClient(clientToRemove);
		}

		/// <summary>
		/// If there's space on the server, asks player at front of queue for their verification to begin playing.
		/// </summary>
		internal static void verifyNextPlayerInQueue()
		{
			if (pending.Count < 1)
				return; // Nobody in queue.

			if (clients.Count >= maxPlayers)
				return; // Not enough space.

			SteamPending pendingPlayer = pending[0];
			if (pendingPlayer.hasSentVerifyPacket)
				return; // Already sent, waiting on a response.

			pendingPlayer.sendVerifyPacket();
		}

		[System.Obsolete]
		private static bool isUnreliable(ESteamPacket type)
		{
			switch (type)
			{
				case ESteamPacket.UPDATE_UNRELIABLE_BUFFER:
				case ESteamPacket.UPDATE_UNRELIABLE_CHUNK_BUFFER:
				case ESteamPacket.UPDATE_VOICE:
					return true;

				default:
					return false;
			}
		}

		[System.Obsolete]
		public static bool isChunk(ESteamPacket packet)
		{
			switch (packet)
			{
				case ESteamPacket.UPDATE_UNRELIABLE_CHUNK_BUFFER:
				case ESteamPacket.UPDATE_RELIABLE_CHUNK_BUFFER:
					return true;

				default:
					return false;
			}
		}

		[System.Obsolete]
		private static bool isUpdate(ESteamPacket packet)
		{
			switch (packet)
			{
				case ESteamPacket.UPDATE_RELIABLE_BUFFER:
				case ESteamPacket.UPDATE_UNRELIABLE_BUFFER:
				case ESteamPacket.UPDATE_RELIABLE_CHUNK_BUFFER:
				case ESteamPacket.UPDATE_UNRELIABLE_CHUNK_BUFFER:
				case ESteamPacket.UPDATE_VOICE:
					return true;

				default:
					return false;
			}
		}

		internal static void resetChannels()
		{
			_bytesSent = 0;
			_bytesReceived = 0;

			_packetsSent = 0;
			_packetsReceived = 0;

			_clients.Clear();
			_transportConnectionToPlayerMap.Clear();
			pending.Clear();
			_transportConnectionToPendingPlayerMap.Clear();

			NetIdRegistry.Clear();
			NetInvocationDeferralRegistry.Clear();
			ClientAssetIntegrity.Clear();
			PhysicsMaterialNetTable.Clear();

			// Hacky for this code to be here, but we need to reset pending instantiations before connecting.
			ItemManager.ClearNetworkStuff();
			PlaceableInstantiationManager.ClearNetworkStuff();
			BarricadeManager.ClearNetworkStuff();
			StructureManager.ClearNetworkStuff();
		}


		public delegate void LoginSpawningHandler(SteamPlayerID playerID, ref Vector3 point, ref float yaw, ref EPlayerStance initialStance, ref bool needsNewSpawnpoint);

		/// <summary>
		/// Called when determining spawnpoint during player login.
		/// </summary>
		public static LoginSpawningHandler onLoginSpawning;

		private static void loadPlayerSpawn(SteamPlayerID playerID, out Vector3 point, out byte angle, out EPlayerStance initialStance)
		{
			point = Vector3.zero;
			angle = 0;
			initialStance = EPlayerStance.STAND;

			bool needsSpawn = false; // Is a new spawn point needed?
			if (PlayerSavedata.fileExists(playerID, "/Player/Player.dat") && Level.info.type == ELevelType.SURVIVAL)
			{
				Block block = PlayerSavedata.readBlock(playerID, "/Player/Player.dat", 1);

				point = block.readSingleVector3() + new Vector3(0, 0.01f, 0);
				angle = block.readByte();

				if (!point.IsFinite())
				{
					// Saved point is NaN or Infinity.
					needsSpawn = true;
					UnturnedLog.info("Reset {0} spawn position ({1}) because it was NaN or infinity", playerID, point);
				}
				else if (point.y > Level.HEIGHT)
				{
					UnturnedLog.info("Clamped {0} spawn position ({1}) because it was above the world height limit ({2})", playerID, point, Level.HEIGHT);
					point.y = Level.HEIGHT - 10.0f;
				}
				else if (!PlayerStance.getStanceForPosition(point, ref initialStance))
				{
					UnturnedLog.info("Reset {0} spawn position ({1}) because it was obstructed", playerID, point);
					needsSpawn = true;
				}
			}
			else
			{
				needsSpawn = true;
			}

			try
			{
				if (onLoginSpawning != null)
				{
					float yaw = angle * 2;
					onLoginSpawning(playerID, ref point, ref yaw, ref initialStance, ref needsSpawn);
					angle = (byte) (yaw / 2);
				}
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Plugin raised an exception from onLoginSpawning:");
				UnturnedLog.exception(e);
			}

			if (needsSpawn)
			{
				PlayerSpawnpoint spawn = LevelPlayers.getSpawn(false);

				point = spawn.point + new Vector3(0, 0.5f, 0);
				angle = (byte) (spawn.angle / 2);
			}
		}

		internal static bool hasSentReadyToLoginNotification;
		/// <summary>
		/// Is client waiting for response to ESteamPacket.CONNECT request?
		/// </summary>
		internal static bool isWaitingForConnectResponse;
		/// <summary>
		/// Realtime that client sent ESteamPacket.CONNECT request.
		/// </summary>
		private static float sentConnectRequestTime;

		/* Nelson 2024-07-30: The disconnect timer allows us to kick ourself from multiplayer after a random amount of
		 * time to mess with cheaters who aren't using hacked clients. (Hacked client could easily disable this.) 
		 * This is not an xmldoc comment. My intention is to exclude it from the public docs. */
		internal static float catPouncingMechanism = -33.0f;

		/// <summary>
		/// Nelson 2023-08-09: adding because in some cases, namely workshop download and level loading,
		/// we can't properly handle client transport failures because these loading systems don't
		/// currently support cancelling partway through. (public issue #4036)
		/// </summary>
		private static bool canCurrentlyHandleClientTransportFailure;
		private static bool hasPendingClientTransportFailure;
		private static string pendingClientTransportFailureMessage;

		private static void ResetClientTransportFailure()
		{
			canCurrentlyHandleClientTransportFailure = true;
			hasPendingClientTransportFailure = false;
			pendingClientTransportFailureMessage = null;
		}

		private static void TriggerDisconnectFromClientTransportFailure()
		{
			hasPendingClientTransportFailure = false;
			_connectionFailureInfo = ESteamConnectionFailureInfo.CUSTOM;
			_connectionFailureReason = pendingClientTransportFailureMessage;
			RequestDisconnect($"Client transport failure: \"{pendingClientTransportFailureMessage}\"");
		}

		private static void onLevelLoaded(int level)
		{
			if (level == 2)
			{
				isLoadingUGC = false;

				//lastPacketTick = Time.realtimeSinceStartup;

				if (isConnected)
				{
					if (isServer)
					{
						if (isClient)
						{
							SteamPlayerID playerID = new SteamPlayerID(client, Characters.selected, clientName, Characters.active.name, Characters.active.nick, Characters.active.group);

							Vector3 point;
							byte angle;
							EPlayerStance initialStance;
							loadPlayerSpawn(playerID, out point, out angle, out initialStance);

							int shirtItem = Provider.provider.economyService.getInventoryItem(Characters.active.packageShirt);
							int pantsItem = Provider.provider.economyService.getInventoryItem(Characters.active.packagePants);
							int hatItem = Provider.provider.economyService.getInventoryItem(Characters.active.packageHat);
							int backpackItem = Provider.provider.economyService.getInventoryItem(Characters.active.packageBackpack);
							int vestItem = Provider.provider.economyService.getInventoryItem(Characters.active.packageVest);
							int maskItem = Provider.provider.economyService.getInventoryItem(Characters.active.packageMask);
							int glassesItem = Provider.provider.economyService.getInventoryItem(Characters.active.packageGlasses);

							int[] skinItems = new int[Characters.packageSkins.Count];
							for (int index = 0; index < skinItems.Length; index++)
							{
								skinItems[index] = Provider.provider.economyService.getInventoryItem(Characters.packageSkins[index]);
							}

							string[] skinTags = new string[Characters.packageSkins.Count];
							for (int index = 0; index < skinTags.Length; index++)
							{
								skinTags[index] = Provider.provider.economyService.getInventoryTags(Characters.packageSkins[index]);
							}

							string[] skinDynamicProps = new string[Characters.packageSkins.Count];
							for (int index = 0; index < skinDynamicProps.Length; index++)
							{
								skinDynamicProps[index] = Provider.provider.economyService.getInventoryDynamicProps(Characters.packageSkins[index]);
							}

#if DEDICATED_SERVER
							ITransportConnection dummyConnection = null;
#else
							SDG.NetTransport.Loopback.TransportConnection_Loopback dummyConnection = SDG.NetTransport.Loopback.TransportConnection_Loopback.Create();
#endif // DEDICATED_SERVER
							NetId netId = ClaimNetIdBlockForNewPlayer();
							SteamPlayer newClient = addPlayer(dummyConnection, netId, playerID, point, angle, isPro, true, allocPlayerChannelId(), Characters.active.face, Characters.active.hair, Characters.active.beard, Characters.active.skin, Characters.active.color, Characters.active.markerColor, Characters.active.BeardColor, Characters.active.hand, shirtItem, pantsItem, hatItem, backpackItem, vestItem, maskItem, glassesItem, skinItems, skinTags, skinDynamicProps, Characters.active.skillset, language, Lobbies.currentLobby, default);
							newClient.player.stance.initialStance = initialStance;
							newClient.player.InitializePlayer();
							newClient.player.SendInitialPlayerState(newClient);

							Lobbies.leaveLobby();

							updateRichPresence();

							try
							{
								onServerConnected?.Invoke(playerID.steamID);
							}
							catch (Exception e)
							{
								UnturnedLog.warn("Plugin raised an exception from onServerConnected:");
								UnturnedLog.exception(e);
							}
						}
						else if (Dedicator.IsDedicatedServer)
						{
							CommandWindow.Log("//////////////////////////////////////////////////////");
							CommandWindow.Log(localization.format("ServerCode", SteamGameServer.GetSteamID()));
							CommandWindow.Log(localization.format("ServerCodeDetails"));
							CommandWindow.Log(localization.format("ServerCodeCopy", "CopyServerCode"));
							CommandWindow.Log("//////////////////////////////////////////////////////");
						}
					}
					else
					{
						if (hasPendingClientTransportFailure)
						{
							UnturnedLog.info("Now able to handle client transport failure that occurred during level load");
							TriggerDisconnectFromClientTransportFailure();
							return;
						}
						// Now that workshop and level loading are finished we can handle failures again.
						canCurrentlyHandleClientTransportFailure = true;

						EClientPlatform clientPlatform;
#if UNITY_EDITOR || UNITY_STANDALONE_WIN
						clientPlatform = EClientPlatform.Windows;
#elif UNITY_STANDALONE_OSX
						clientPlatform = EClientPlatform.Mac;
#elif UNITY_STANDALONE_LINUX
						clientPlatform = EClientPlatform.Linux;
#endif

						critMods.Clear();
						modBuilder.Length = 0;
						SDG.Framework.Modules.ModuleHook.getRequiredModules(critMods);
						for (int i = 0; i < critMods.Count; i++)
						{
							modBuilder.Append(critMods[i].config.Name);
							modBuilder.Append(",");
							modBuilder.Append(critMods[i].config.Version_Internal);
							if (i < critMods.Count - 1)
							{
								modBuilder.Append(";");
							}
						}

						UnturnedLog.info("Ready to connect");

						hasSentReadyToLoginNotification = true;
						isWaitingForConnectResponse = true;
						sentConnectRequestTime = Time.realtimeSinceStartup;

						NetMessages.SendMessageToServer(EServerMessage.ReadyToConnect, ENetReliability.Reliable, (NetPakWriter writer) =>
						{
							writer.WriteUInt8(Characters.selected);
							writer.WriteString(clientName);
							writer.WriteString(Characters.active.name);
							writer.WriteBytes(_serverPasswordHash, 20);
							writer.WriteBytes(Level.hash, 20);
							writer.WriteBytes(ReadWrite.readData(), 20);
#if !DEDICATED_SERVER
							writer.WriteBytes(ResourceHash.localHash, 20);
#endif // !DEDICATED_SERVER
							writer.WriteEnum(clientPlatform);
							writer.WriteUInt32(APP_VERSION_PACKED);
							if (_modInfo != null)
							{
								writer.WriteString(_modInfo.Name, 8);
								writer.WriteUInt32(_modInfo.GetPackedVersion());
							}
							else
							{
								writer.WriteString(null, 8);
								writer.WriteUInt32(uint.MaxValue);
							}
							writer.WriteBit(isPro);
							writer.WriteUInt16(MathfEx.ClampToUShort(CurrentServerAdvertisement?.PingMs ?? 1));
							writer.WriteString(Characters.active.nick);
							writer.WriteSteamID(Characters.active.group);
							writer.WriteUInt8(Characters.active.face);
							writer.WriteUInt8(Characters.active.hair);
							writer.WriteUInt8(Characters.active.beard);
							writer.WriteColor32RGB(Characters.active.skin);
							writer.WriteColor32RGB(Characters.active.color);
							writer.WriteColor32RGB(Characters.active.markerColor);
							writer.WriteColor32RGB(Characters.active.BeardColor);
							writer.WriteBit(Characters.active.hand);
							writer.WriteUInt64(Characters.active.packageShirt);
							writer.WriteUInt64(Characters.active.packagePants);
							writer.WriteUInt64(Characters.active.packageHat);
							writer.WriteUInt64(Characters.active.packageBackpack);
							writer.WriteUInt64(Characters.active.packageVest);
							writer.WriteUInt64(Characters.active.packageMask);
							writer.WriteUInt64(Characters.active.packageGlasses);
							writer.WriteList(Characters.packageSkins, writer.WriteUInt64, MAX_SKINS_LENGTH);
							writer.WriteEnum(Characters.active.skillset);
							writer.WriteString(modBuilder.ToString());
							writer.WriteString(language);
							writer.WriteSteamID(Lobbies.currentLobby);
							writer.WriteUInt32(Level.packedVersion);

							byte[][] hwids = LocalHwid.GetHwids();
							writer.WriteUInt8((byte) hwids.Length);
							foreach (byte[] hwid in hwids)
							{
								writer.WriteBytes(hwid, 20);
							}

							writer.WriteBytes(TempSteamworksEconomy.econInfoHash, 20);
							writer.WriteSteamID(user);
						});
					}
				}
			}
		}

		internal static readonly NetLength MAX_SKINS_LENGTH = new NetLength(127);

		/// <summary>
		/// Manages client to server communication.
		/// </summary>
		internal static IClientTransport clientTransport;

		/// <summary>
		/// Manages server to client communication.
		/// </summary>
		private static IServerTransport serverTransport;

		/// <summary>
		/// Connect to server entry point on client.
		/// Requests workshop details for download prior to loading level.
		/// Once workshop is ready launch() is called.
		/// </summary>
		public static void connect(ServerConnectParameters parameters, SteamServerAdvertisement advertisement, List<PublishedFileId_t> expectedWorkshopItems)
		{
			if (isConnected)
			{
				return;
			}

			_currentServerConnectParameters = parameters;
			_currentServerAdvertisement = advertisement;
			isWhitelisted = false;
			isVacActive = false; // Unknown until initial response is received.
			isThirdpartyAntiCheatActive = false; // Unknown until initial response is received.
			_isConnected = true;
			_queuePosition = 0;

			resetChannels();

			if (_currentServerAdvertisement != null)
			{
				Lobbies.LinkLobby(_currentServerAdvertisement.ip, _currentServerAdvertisement.queryPort);
				_server = _currentServerAdvertisement.steamID;
			}
			else
			{
				Lobbies.LinkLobby(parameters.address.value, parameters.queryPort);
				_server = parameters.steamId;
			}

			_serverPassword = parameters.password;
			_serverPasswordHash = Hash.SHA1(parameters.password);
			_isClient = true;

			//lastPacketTick = Time.realtimeSinceStartup;

			timeLastPacketWasReceivedFromServer = Time.realtimeSinceStartup;
			pings = new float[4];

			lag(_currentServerAdvertisement != null ? _currentServerAdvertisement.PingMs / 1000f : 0);

			isLoadingUGC = true;
			LoadingUI.updateScene();

			hasSentReadyToLoginNotification = false;
			isWaitingForConnectResponse = false; // True once ESteamPacket.CONNECT is sent (not yet).
			isWaitingForWorkshopResponse = true;
			waitingForExpectedWorkshopItems = expectedWorkshopItems;
#if !DEDICATED_SERVER
			isWaitingForAuthenticationResponse = false;
#endif // !DEDICATED_SERVER

			catPouncingMechanism = -22.0f;

			List<SteamItemInstanceID_t> wear = new List<SteamItemInstanceID_t>();

			if (Characters.active.packageShirt != 0)
			{
				wear.Add((SteamItemInstanceID_t) Characters.active.packageShirt);
			}

			if (Characters.active.packagePants != 0)
			{
				wear.Add((SteamItemInstanceID_t) Characters.active.packagePants);
			}

			if (Characters.active.packageHat != 0)
			{
				wear.Add((SteamItemInstanceID_t) Characters.active.packageHat);
			}

			if (Characters.active.packageBackpack != 0)
			{
				wear.Add((SteamItemInstanceID_t) Characters.active.packageBackpack);
			}

			if (Characters.active.packageVest != 0)
			{
				wear.Add((SteamItemInstanceID_t) Characters.active.packageVest);
			}

			if (Characters.active.packageMask != 0)
			{
				wear.Add((SteamItemInstanceID_t) Characters.active.packageMask);
			}

			if (Characters.active.packageGlasses != 0)
			{
				wear.Add((SteamItemInstanceID_t) Characters.active.packageGlasses);
			}

			for (int index = 0; index < Characters.packageSkins.Count; index++)
			{
				ulong package = Characters.packageSkins[index];

				if (package != 0)
				{
					wear.Add((SteamItemInstanceID_t) package);
				}
			}

			if (wear.Count > 0)
			{
				SteamInventory.GetItemsByID(out provider.economyService.wearingResult, wear.ToArray(), (uint) wear.Count);
			}

			Level.loading();

			ResetClientTransportFailure();

			clientTransport = NetTransportFactory.CreateClientTransport(_currentServerAdvertisement?.networkTransport);
			UnturnedLog.info("Initializing {0}", clientTransport.GetType().Name);
			clientTransport.Initialize(onClientTransportReady, onClientTransportFailure);
		}

		/// <summary>
		/// Callback once client transport is ready to send messages.
		/// </summary>
		private static void onClientTransportReady()
		{
			CachedWorkshopResponse response = null;
			foreach (CachedWorkshopResponse cache in cachedWorkshopResponses)
			{
				if (cache.server == server && Time.realtimeSinceStartup - cache.realTime < 60.0f) // Cache for 60 seconds, server is rate-limited to 30 seconds.
				{
					response = cache;
					break;
				}
			}

			if (response != null) // Has recently cached response.
			{
				receiveWorkshopResponse(response);
			}
			else
			{
				NetMessages.SendMessageToServer(EServerMessage.GetWorkshopFiles, ENetReliability.Reliable, (NetPakWriter writer) =>
				{
					writer.AlignToByte();
					// 32 bits (4 bytes) * 240 = 960
					for (int i = 0; i < 240; ++i)
					{
						writer.WriteBits(0, 32);
					}
					writer.WriteString("Hello!");
				});
			}
		}

		/// <summary>
		/// Callback when something goes wrong and client must disconnect.
		/// </summary>
		private static void onClientTransportFailure(string message)
		{
			hasPendingClientTransportFailure = true;
			pendingClientTransportFailureMessage = message;

			if (canCurrentlyHandleClientTransportFailure)
			{
				TriggerDisconnectFromClientTransportFailure();
			}
			else
			{
				UnturnedLog.info("Deferring client transport failure because we can't currently handle it");
			}
		}

		private static bool CompareClientAndServerWorkshopFileTimestamps()
		{
			if (provider.workshopService.serverPendingIDs == null)
			{
				return true;
			}

			foreach (PublishedFileId_t fileId in provider.workshopService.serverPendingIDs)
			{
				ServerRequiredWorkshopFile serverDetails;
				if (!currentServerWorkshopResponse.FindRequiredFile(fileId.m_PublishedFileId, out serverDetails))
				{
					UnturnedLog.error($"Server workshop files response missing details for file: {fileId}");
					continue;
				}

				if (serverDetails.timestamp.Year < 2000)
				{
					// Probably Unix epoch / invalid timestamp.
					UnturnedLog.info($"Skipping timestamp comparison for server workshop file {fileId} because timestamp is invalid ({serverDetails.timestamp.ToLocalTime()})");
					continue;
				}

				string filePath;
				ulong fileSize;
				uint localTimestampUnix;
				if (!SteamUGC.GetItemInstallInfo(fileId, out fileSize, out filePath, 1024, out localTimestampUnix))
				{
					UnturnedLog.info($"Skipping timestamp comparison for server workshop file {fileId} because item install info is missing");
					continue;
				}

				System.DateTime localTimestamp = DateTimeEx.FromUtcUnixTimeSeconds(localTimestampUnix);
				if (localTimestamp == serverDetails.timestamp)
				{
					// Both have the same timestamp loaded, great!
					UnturnedLog.info($"Workshop file {fileId} timestamp matches between client and server ({localTimestamp})");
					continue;
				}

				string title;
				CachedUGCDetails localDetails;
				bool hasLocalDetails = SDG.Provider.TempSteamworksWorkshop.getCachedDetails(fileId, out localDetails);
				if (hasLocalDetails)
				{
					title = localDetails.GetTitle();
				}
				else
				{
					title = $"Unknown File ID {fileId}";
				}

				string message;
				bool shouldVerifyGameFiles;
				if (serverDetails.timestamp > localTimestamp)
				{
					message = $"Server is running a newer version of the \"{title}\" workshop file.";
				}
				else
				{
					message = $"Server is running an older version of the \"{title}\" workshop file.";
				}
				if (hasLocalDetails)
				{
					System.DateTime steamTimestamp = DateTimeEx.FromUtcUnixTimeSeconds(localDetails.updateTimestamp);
					if (localTimestamp == steamTimestamp)
					{
						message += "\nYour installed copy of the file matches the most recent version on Steam.";
						message += $"\nLocal and Steam timestamp: {localTimestamp.ToLocalTime()} Server timestamp: {serverDetails.timestamp.ToLocalTime()}";
						shouldVerifyGameFiles = false; // Client is already up-to-date.
					}
					else if (serverDetails.timestamp == steamTimestamp)
					{
						message += "\nThe server's installed copy of the file matches the most recent version on Steam.";
						message += $"\nLocal timestamp: {localTimestamp.ToLocalTime()} Server and Steam timestamp: {serverDetails.timestamp.ToLocalTime()}";
						shouldVerifyGameFiles = true; // Client needs to update.
					}
					else
					{
						message += $"\nLocal timestamp: {localTimestamp.ToLocalTime()} Server timestamp: {serverDetails.timestamp.ToLocalTime()} Steam timestamp: {steamTimestamp}";
						shouldVerifyGameFiles = true; // Nobody is up-to-date.
					}
				}
				else
				{
					message += $"\nLocal timestamp: {localTimestamp.ToLocalTime()} Server timestamp: {serverDetails.timestamp.ToLocalTime()}";
					shouldVerifyGameFiles = serverDetails.timestamp > localTimestamp; // Verify if server is more up-to-date.
				}
				_connectionFailureReason = message;
				_connectionFailureInfo = shouldVerifyGameFiles ?
					ESteamConnectionFailureInfo.CUSTOM_SHOULD_VERIFY_GAME_FILES : ESteamConnectionFailureInfo.CUSTOM;

				RequestDisconnect($"Loaded workshop file timestamp mismatch (File ID: {fileId} Local timestamp: {localTimestamp.ToLocalTime()} Server timestamp: {serverDetails.timestamp.ToLocalTime()})");
				return false;
			}

			return true;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		private static CommandLineFlag shouldIgnoreServerWorkshopFiles = new CommandLineFlag(false, "-IgnoreServerWorkshopFiles");
#else
		private const bool shouldIgnoreServerWorkshopFiles = false;
#endif

		/// <summary>
		/// Multiplayer load level entry point on client.
		/// Called once workshop downloads are finished, or we know the server is not using workshop.
		/// Once level is loaded the connect packet is sent to the server.
		/// </summary>
		public static void launch()
		{
			LevelInfo pendingLevel = Level.getLevel(map);
			if (pendingLevel == null)
			{
				_connectionFailureInfo = ESteamConnectionFailureInfo.MAP;

				RequestDisconnect($"could not find level \"{map}\"");

				return;
			}

			if (!shouldIgnoreServerWorkshopFiles)
			{
				if (!CompareClientAndServerWorkshopFileTimestamps())
				{
					// Method will already have logged why and requested disconnect.
					return;
				}
			}

			if (hasPendingClientTransportFailure)
			{
				UnturnedLog.info("Now able to handle client transport failure that occurred during workshop file download/install/load");
				TriggerDisconnectFromClientTransportFailure();
				return;
			}

			if (!shouldIgnoreServerWorkshopFiles)
			{
				Assets.ApplyServerAssetMapping(pendingLevel, provider.workshopService.serverPendingIDs);
			}

			// We don't currently support interrupting the map loading.
			// Handling is paused until loading finishes.
			canCurrentlyHandleClientTransportFailure = false;

			UnturnedLog.info("Loading server level ({0})", map);
			Level.load(pendingLevel, false);
			loadGameMode();
		}

		private static void loadGameMode()
		{
			LevelAsset levelAsset = Level.getAsset();
			if (levelAsset == null)
			{
				gameMode = new SurvivalGameMode();
				return;
			}

			Type gameModeType = levelAsset.defaultGameMode.type;
			if (gameModeType == null)
			{
				gameMode = new SurvivalGameMode();
				return;
			}

			gameMode = Activator.CreateInstance(gameModeType) as GameMode;
			if (gameMode == null)
			{
				gameMode = new SurvivalGameMode();
			}
		}

		private static void unloadGameMode()
		{
			gameMode = null;
		}

		public static void singleplayer(EGameMode singleplayerMode, bool singleplayerCheats)
		{
			_isConnected = true; // Whether the application is currently involved in networking.

			resetChannels();

			Dedicator.serverVisibility = ESteamServerVisibility.LAN;
			Dedicator.serverID = "Singleplayer_" + Characters.selected;

			Commander.init();

			maxPlayers = 1;
			queueSize = 8;
			serverName = "Singleplayer #" + (Characters.selected + 1);
			serverPassword = "Singleplayer";

			// VAC and thirdparty anti-cheat are not used in singleplayer.
			isVacActive = false;
			isThirdpartyAntiCheatActive = false;

			ip = 0;
			port = 25000;

			timeLastPacketWasReceivedFromServer = Time.realtimeSinceStartup;
			pings = new float[4];

			isPvP = true;
			isWhitelisted = false;
			hideAdmins = false;
			hasCheats = singleplayerCheats;
			filterName = false;
			mode = singleplayerMode;
			isGold = false;
			gameMode = null;
			cameraMode = ECameraMode.BOTH;

			if (singleplayerMode != EGameMode.TUTORIAL)
			{
				PlayerInventory.skillsets = PlayerInventory.SKILLSETS_CLIENT;
			}

			lag(0);

			SteamWhitelist.load();
			SteamBlacklist.load();
			SteamAdminlist.load();

			_currentServerAdvertisement = null;

			_configData = ConfigData.CreateDefault(true);
			_modeConfigDataOverrides.Clear();
			if (singleplayerMode != EGameMode.TUTORIAL)
			{
				LoadGameplayConfig(true);
			}
			_modeConfigData = _configData.getModeConfig(mode);
			if (_modeConfigData == null)
			{
				_modeConfigData = new ModeConfigData(mode);
				_modeConfigData.InitSingleplayerDefaults();
			}
			authorityHoliday = _modeConfigData.Gameplay.Allow_Holidays ? HolidayUtil.GetScheduledHoliday() : ENPCHoliday.NONE;

			_isServer = true;
			_isClient = true;

			PhysicsMaterialNetTable.ServerPopulateTable();

			time = SteamUtils.GetServerRealTime();
			Level.load(Level.getLevel(map), true);
			loadGameMode();
			applyLevelModeConfigOverrides();

			_server = user;
			_client = _server;
			_clientHash = Hash.SHA1(client);

			timeLastPacketWasReceivedFromServer = Time.realtimeSinceStartup;

			broadcastServerHosted();
		}

		private static CommandLineString clGameplayConfigFileOverride = new CommandLineString("-GameplayConfigFile");
		/// <summary>
		/// Anticipating some hosts will prefer the old format.
		/// </summary>
		private static CommandLineFlag clUseLegacyJsonConfig = new CommandLineFlag(false, "-UseLegacyJsonGameplayConfig");
		private static CommandLineFlag clLogGameplayConfig = new CommandLineFlag(false, "-LogGameplayConfig");
		/// <summary>
		/// Remove empty strings, dictionaries, and lists.
		/// </summary>
		private static CommandLineFlag clGameplayConfigNoEmptyValues = new CommandLineFlag(false, "-GameplayConfigNoEmptyValues");
		/// <summary>
		/// Remove generated comments.
		/// </summary>
		private static CommandLineFlag clGameplayConfigNoGeneratedComments = new CommandLineFlag(false, "-GameplayConfigNoGeneratedComments");
		private static void LoadGameplayConfig(bool singleplayer)
		{
			Stopwatch watch = Stopwatch.StartNew();

			bool enableV2Config = !clUseLegacyJsonConfig;
			string v2FilePath = null;
			if (enableV2Config)
			{
				if (clGameplayConfigFileOverride.hasValue)
				{
					if (Path.IsPathFullyQualified(clGameplayConfigFileOverride.value))
					{
						if (File.Exists(clGameplayConfigFileOverride.value))
						{
							v2FilePath = clGameplayConfigFileOverride.value;
						}
						else
						{
							CommandWindow.LogWarning($"-GameplayConfigFile appears to be an absolute path but does not exist: \"{clGameplayConfigFileOverride.value}\"");
						}
					}
					else
					{
						string transformedPath = ReadWrite.PATH + ServerSavedata.transformPath('/' + clGameplayConfigFileOverride.value);
						if (File.Exists(transformedPath))
						{
							v2FilePath = transformedPath;
						}
						else
						{
							CommandWindow.LogWarning($"-GameplayConfigFile appears to be a relative path but does not exist at: \"{transformedPath}\"");
						}
					}
				}

				if (string.IsNullOrEmpty(v2FilePath))
				{
					if (singleplayer)
					{
						v2FilePath = PlayConfigUtils.GetSingleplayerConfigPathV2(Characters.selected, mode);
					}
					else
					{
						// Default to Config.txt
						v2FilePath = PlayConfigUtils.GetServerConfigPathV2(serverID);
						if (!File.Exists(v2FilePath))
						{
							// If it doesn't exist yet (maybe entirely new server) check for legacy
							// Config.json in which case we use per-difficulty config file.
							if (ServerSavedata.fileExists("/Config.json"))
							{
								v2FilePath = PlayConfigUtils.GetServerConfigPathV2(serverID, mode);
							}
						}
					}
				}
			}

			IEditableDatDictionary dictionaryToWrite = null;
			if (enableV2Config && File.Exists(v2FilePath))
			{
				UnityEngine.Profiling.Profiler.BeginSample("LoadGameplayConfig.ReadFileV2");
				// Either specified on command-line or a default v2 config file.
				IDatDictionary parsedRootDictionary = null;
				try
				{
					using (FileStream fileStream = new FileStream(v2FilePath, FileMode.Open, FileAccess.Read, FileShare.Read))
					using (StreamReader streamReader = new StreamReader(fileStream))
					{
						DatParser parser = new DatParser();
						parser.EnableMetadata = true;
						parsedRootDictionary = parser.Parse(streamReader);

						if (parser.HasError)
						{
							CommandWindow.LogWarning("Error(s) parsing gameplay config:");
							foreach (string message in parser.ErrorMessages)
							{
								CommandWindow.LogWarning(message);
							}
						}
					}
				}
				catch (System.Exception e)
				{
					UnturnedLog.exception(e, $"Caught exception parsing v2 gameplay config from \"{v2FilePath}\":");
				}
				UnityEngine.Profiling.Profiler.EndSample(); // LoadGameplayConfig.ReadFileV2

				if (parsedRootDictionary == null)
				{
					return;
				}

				if (!singleplayer)
				{
					UnityEngine.Profiling.Profiler.BeginSample("LoadGameplayConfig.ParseServerConfig");
					try
					{
						PlayConfigUtils.ParseServerConfig(parsedRootDictionary, _configData);
					}
					catch (System.Exception e)
					{
						// Likely NotImplementedException if we messed handling a particular type
						UnturnedLog.exception(e, $"Caught exception parsing server config from \"{v2FilePath}\":");
					}
					UnityEngine.Profiling.Profiler.EndSample(); // LoadGameplayConfig.ParseServerConfig
				}

				UnityEngine.Profiling.Profiler.BeginSample("LoadGameplayConfig.ParseModeConfig");
				try
				{
					PlayConfigUtils.ParseModeConfig(parsedRootDictionary, _configData.getModeConfig(mode), _modeConfigDataOverrides);
				}
				catch (System.Exception e)
				{
					// Likely NotImplementedException if we messed handling a particular type
					UnturnedLog.exception(e, $"Caught exception parsing mode config from \"{v2FilePath}\":");
				}
				UnityEngine.Profiling.Profiler.EndSample(); // LoadGameplayConfig.ParseModeConfig

				if (!singleplayer)
				{
					dictionaryToWrite = parsedRootDictionary.Edit();
				}
			}
			else
			{
				if (enableV2Config)
				{
					dictionaryToWrite = MetadataPreservingDatWriter.CreateRoot();
				}

				ConfigData legacyConfig = ConfigData.CreateDefault(singleplayer);
				if (ServerSavedata.fileExists("/Config.json"))
				{
					if (enableV2Config)
					{
						CommandWindow.Log($"Converting older Config.json file {mode} mode section into newer txt file");
					}
					else
					{
						CommandWindow.Log("Not converting legacy Config.json file, using as-is");
					}

					try
					{
						ServerSavedata.populateJSON("/Config.json", legacyConfig);
					}
					catch (Exception e)
					{
						UnturnedLog.exception(e, "Caught exception while parsing json gameplay config:");
					}

					if (!singleplayer && enableV2Config)
					{
						Dictionary<FieldInfo, object> serverConfigOverrides = new Dictionary<FieldInfo, object>();

						try
						{
							PlayConfigUtils.GatherServerModifiedFields(configData, legacyConfig, serverConfigOverrides);
						}
						catch (System.Exception e)
						{
							// Likely NotImplementedException if we messed handling a particular type
							UnturnedLog.exception(e, $"Caught exception gathering server modified fields for json config conversion:");
						}

						foreach (KeyValuePair<FieldInfo, object> pair in serverConfigOverrides)
						{
							CommandWindow.Log($"Converted {PlayConfigUtils.GetFieldPath(pair.Key)} = \"{pair.Value}\"");
						}

						try
						{
							PlayConfigUtils.ApplyServerConfigOverrides(dictionaryToWrite, serverConfigOverrides);
						}
						catch (System.Exception e)
						{
							// Likely NotImplementedException if we messed handling a particular type
							UnturnedLog.exception(e, $"Caught exception applying server modified fields for json config conversion:");
						}
					}

					try
					{
						PlayConfigUtils.GatherModifiedFields(configData.getModeConfig(mode), legacyConfig.getModeConfig(mode), _modeConfigDataOverrides);
					}
					catch (System.Exception e)
					{
						// Likely NotImplementedException if we messed handling a particular type
						UnturnedLog.exception(e, $"Caught exception gathering mode modified fields for json config:");
					}

					if (enableV2Config)
					{
						foreach (KeyValuePair<FieldInfo, object> pair in _modeConfigDataOverrides)
						{
							CommandWindow.Log($"Converted {PlayConfigUtils.GetFieldPath(pair.Key)} = \"{pair.Value}\"");
						}

						try
						{
							PlayConfigUtils.ApplyModeConfigOverrides(dictionaryToWrite, _modeConfigDataOverrides);
						}
						catch (System.Exception e)
						{
							// Likely NotImplementedException if we messed handling a particular type
							UnturnedLog.exception(e, $"Caught exception applying mode modified fields for json config conversion:");
						}
					}

					_configData = legacyConfig;
				}

				if (clUseLegacyJsonConfig)
				{
					try
					{
						ServerSavedata.serializeJSON("/Config.json", legacyConfig);
					}
					catch (Exception e)
					{
						UnturnedLog.exception(e, "Caught exception while serializing json gameplay config:");
					}
				}
			}

			if (clLogGameplayConfig)
			{
				CommandWindow.Log("Server gameplay config overrides:");
				foreach (KeyValuePair<FieldInfo, object> pair in _modeConfigDataOverrides)
				{
					CommandWindow.Log($"{PlayConfigUtils.GetFieldPath(pair.Key)} = \"{pair.Value}\"");
				}
			}

			watch.Stop();
			UnturnedLog.info($"Load gameplay config: {watch.ElapsedMilliseconds} ms");

			if (dictionaryToWrite != null)
			{
				watch.Restart();
				WriteGameplayConfigThreadState threadState = new WriteGameplayConfigThreadState();
				threadState.withComments = !singleplayer;
				threadState.noEmptyValues = !singleplayer && clGameplayConfigNoEmptyValues;
				threadState.noGeneratedComments = !singleplayer && clGameplayConfigNoGeneratedComments;
				threadState.filePath = v2FilePath;
				threadState.rootDictionary = dictionaryToWrite;
				threadState.watch = watch;
				System.Threading.ThreadPool.QueueUserWorkItem(WriteGameplayConfigOnWorkerThread, threadState);
			}
		}

		private class WriteGameplayConfigThreadState
		{
			public bool withComments;
			public bool noEmptyValues;
			public bool noGeneratedComments;
			public string filePath;
			public List<KeyValuePair<Exception, string>> errors = new List<KeyValuePair<Exception, string>>();
			public IEditableDatDictionary rootDictionary;
			public Stopwatch watch;

			public void AddError(Exception e, string message)
			{
				errors.Add(new KeyValuePair<Exception, string>(e, message));
			}
		}

		private static void WriteGameplayConfigOnWorkerThread(object voidState)
		{
			WriteGameplayConfigThreadState state = (WriteGameplayConfigThreadState) voidState;

			if (state.withComments)
			{
				IEditableDatValue formatVersion = state.rootDictionary.GetOrAddValue("Version");
				formatVersion.SetInt32(1);
				formatVersion.PreferredLineNumber = 1;
				formatVersion.SortingPreference = IEditableDatNode.ESortingPreference.TowardFront;
				formatVersion.MergeGeneratedCommentAlloc(PlayConfigUtils.COMMENT_PREFIX, new string[] {
					"Unturned Server Configuration File",
					"",
					"Lines beginning with // are comments.",
					$"Comments beginning with {PlayConfigUtils.COMMENT_PREFIX.Trim()} are auto-generated.",
					$"Any comments you write (without {PlayConfigUtils.COMMENT_PREFIX.Trim()}) will be preserved.",
					"",
					"Settings without a value use the default for the mode (easy/normal/hard).",
					"For example, this setting would use the default:",
					"",
					"Setting",
					"",
					"Whereas this setting is overridden with value four:",
					"",
					"Setting 4",
					"",
				});

				try
				{
					PlayConfigUtils.PopulateConfigFilePropertiesAndComments(state.rootDictionary);
				}
				catch (System.Exception e)
				{
					// Likely NotImplementedException if we messed handling a particular type
					state.AddError(e, $"Caught exception updating config file \"{state.filePath}\":");
				}
			}

			// Would be more optimal to integrate into PopulateConfigFilePropertiesAndComments, but
			// this is a niche option on a background thread.
			if (state.noEmptyValues)
			{
				try
				{
					PlayConfigUtils.RemoveEmptyValues(state.rootDictionary);
				}
				catch (System.Exception e)
				{
					state.AddError(e, $"Caught exception removing empty values from config file \"{state.filePath}\":");
				}
			}

			// Would be more optimal to integrate into PopulateConfigFilePropertiesAndComments, but
			// this is a niche option on a background thread.
			if (state.noGeneratedComments)
			{
				try
				{
					PlayConfigUtils.RemoveGeneratedComments(state.rootDictionary);
				}
				catch (System.Exception e)
				{
					state.AddError(e, $"Caught exception removing empty values from config file \"{state.filePath}\":");
				}
			}

			ReadWrite.DeleteIfExistsAbsolute(ServerSavedata.GetBackupFilePathV1(state.filePath)); // Cleanup
			ReadWrite.MoveIfExistsAbsolute(state.filePath, ServerSavedata.GetBackupFilePath(state.filePath));

			try
			{
				string dirname = Path.GetDirectoryName(state.filePath);
				if (!Directory.Exists(dirname))
				{
					Directory.CreateDirectory(dirname);
				}

				const bool append = false;
				using (StreamWriter fileStream = new StreamWriter(state.filePath, append, System.Text.Encoding.UTF8))
				{
					DatWriter datWriter = new DatWriter(fileStream);
					MetadataPreservingDatWriter metadataPreservingDatWriter = new MetadataPreservingDatWriter();
					metadataPreservingDatWriter.WriteRootDictionary(state.rootDictionary, datWriter);
				}
			}
			catch (System.Exception e)
			{
				state.AddError(e, $"Caught exception writing updated config file to: \"{state.filePath}\"");
			}

			GameThreadQueueUtil.QueueGameThreadWorkItem(OnWriteGameplayConfigFinished, state);
		}

		private static void OnWriteGameplayConfigFinished(object voidState)
		{
			WriteGameplayConfigThreadState state = (WriteGameplayConfigThreadState) voidState;
			state.watch.Stop();
			UnturnedLog.info($"Rewriting gameplay config (on worker thread): {state.watch.ElapsedMilliseconds} ms");
			foreach (KeyValuePair<Exception, string> error in state.errors)
			{
				UnturnedLog.exception(error.Key, error.Value);
			}
		}

		public static void host()
		{
			_isConnected = true; // Whether the application is currently involved in networking.

			resetChannels();

			openGameServer();

			_isServer = true;

			broadcastServerHosted();
		}

		public delegate void CommenceShutdownHandler();
		/// <summary>
		/// Event for plugins prior to kicking players during shutdown.
		/// </summary>
		public static event CommenceShutdownHandler onCommenceShutdown;

		private static void broadcastCommenceShutdown()
		{
			try
			{
				onCommenceShutdown?.Invoke();
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Plugin raised an exception from onCommenceShutdown:");
				UnturnedLog.exception(e);
			}
		}

		private static int countShutdownTimer = -1;
		private static string shutdownMessage = string.Empty;
		private static float lastTimerMessage;
		internal static bool didServerShutdownTimerReachZero;

		public static void shutdown()
		{
			shutdown(0);
		}

		public static void shutdown(int timer)
		{
			shutdown(timer, string.Empty);
		}

		public static void shutdown(int timer, string explanation)
		{
			countShutdownTimer = timer;
			lastTimerMessage = Time.realtimeSinceStartup;
			shutdownMessage = explanation;
			UnturnedLog.info($"Set server shutdown timer to {timer}s (client message: {explanation})");
		}

		/// <summary>
		/// Set on the server when initializing Steam API.
		/// Used to notify pending clients whether VAC is active.
		/// Set on clients after initial response is received.
		/// </summary>
		internal static bool isVacActive;

		/// <summary>
		/// Set on the server when initializing thirdparty anti-cheat API.
		/// Used to notify pending clients whether thirdparty anti-cheat is active.
		/// Set on clients after initial response is received.
		/// </summary>
		internal static bool isThirdpartyAntiCheatActive;
		private static bool hasSetIsThirdpartyAntiCheatActive;

		public static void RequestDisconnect(string reason)
		{
			UnturnedLog.info("Disconnecting: " + reason);
			disconnect();
		}

		/// <summary>
		/// Client should call RequestDisconnect instead to ensure all disconnects have a logged reason.
		/// </summary>
		public static void disconnect()
		{
#if !DEDICATED_SERVER
			// Double-check we submit any kill counter progress.
			if (!Dedicator.IsDedicatedServer && Player.LocalPlayer != null && Player.LocalPlayer.channel != null && Player.LocalPlayer.channel.owner != null)
			{
				Player.LocalPlayer.channel.owner.commitModifiedDynamicProps();
			}
#endif // !DEDICATED_SERVER

			if (isServer)
			{
#if WITH_THIRDPARTYAC
				if (isThirdpartyAntiCheatActive)
				{
					ShutdownThirdpartyAntiCheatServer();
				}
#endif

				if (serverTransport != null) // Can be null in singleplayer.
				{
					// Do not set serverTransport to null yet because disconnect can be called during the listen loop.
					serverTransport.TearDown();
				}

				if (Dedicator.IsDedicatedServer)
				{
					closeGameServer(); // do this in P2P too
				}
				else// if(!Dedicator.IsDedicatedServer)
				{
					broadcastServerShutdown();

					//closeGameServer(); // do this in P2P
				}

				if (isClient)
				{
					_client = user;
					_clientHash = Hash.SHA1(client);
				}

				_isServer = false;
				_isClient = false;
			}
			else if (isClient)
			{
#if WITH_THIRDPARTYAC
				ShutdownThirdpartyAntiCheatClient();
#endif

				// Tell server we are disconnecting which is useful to distinguish from error/timeout.
				NetMessages.SendMessageToServer(EServerMessage.GracefullyDisconnect, ENetReliability.Reliable, (NetPakWriter writer) => { });

				// Do not set clientTransport to null yet because disconnect can be called during the listen loop.
				clientTransport.TearDown();

				SteamFriends.SetRichPresence("connect", "");

				Lobbies.leaveLobby();

				CancelAllSteamAuthTickets();

				SteamUser.AdvertiseGame(CSteamID.Nil, 0, 0);

				_server = new CSteamID();
				_isServer = false;
				_isClient = false;
			}

			onClientDisconnected?.Invoke();

			if (!isApplicationQuitting) // Do not waste time returning to main menu.
			{
				// Reset holiday for main menu. (server may have used an override)
				authorityHoliday = HolidayUtil.GetScheduledHoliday();

				Level.exit();
			}

			Assets.ClearServerAssetMapping();

			unloadGameMode();

			_isConnected = false;
			hasSentReadyToLoginNotification = false;
			isWaitingForConnectResponse = false;
			isWaitingForWorkshopResponse = false;
			isLoadingUGC = false;
#if !DEDICATED_SERVER
			isWaitingForAuthenticationResponse = false;
#endif // !DEDICATED_SERVER

			isLoadingInventory = true;

			UnturnedLog.info("Disconnected");
		}

		[System.Obsolete]
		public static void sendGUIDTable(SteamPending player)
		{
			accept(player);
		}

		private static bool isServerConnectedToSteam;
		/// <summary>
		/// Internet server callback when backend is ready.
		/// </summary>
		private static void handleServerReady()
		{
			if (isServerConnectedToSteam)
			{
				return;
			}
			isServerConnectedToSteam = true; // Maybe a better way is to to unsubscribe from the event? Have to check because Steam's connected callback can fire multiple times.

			CommandWindow.Log("Steam servers ready!");
			initializeDedicatedUGC();
		}

		private static void initializeDedicatedUGC()
		{
			WorkshopDownloadConfig downloadConfig = WorkshopDownloadConfig.getOrLoad();
			DedicatedUGC.initialize();

			if (Assets.shouldLoadAnyAssets)
			{
				foreach (ulong workshopDownloadID in downloadConfig.File_IDs)
				{
					DedicatedUGC.registerItemInstallation(workshopDownloadID);
				}
			}

			DedicatedUGC.installed += onDedicatedUGCInstalled;

			bool onlyFromCache = Dedicator.offlineOnly;
			DedicatedUGC.beginInstallingItems(onlyFromCache);
		}

		public static string getModeTagAbbreviation(EGameMode gm)
		{
			switch (gm)
			{
				case EGameMode.EASY:
					return "EZY";

				case EGameMode.HARD:
					return "HRD";

				case EGameMode.NORMAL:
					return "NRM";

				default:
					return null;
			}
		}

		public static string getCameraModeTagAbbreviation(ECameraMode cm)
		{
			switch (cm)
			{
				case ECameraMode.FIRST:
					return "1Pp";

				case ECameraMode.BOTH:
					return "2Pp";

				case ECameraMode.THIRD:
					return "3Pp";

				case ECameraMode.VEHICLE:
					return "4Pp";

				default:
					return null;
			}
		}

		public static string GetMonetizationTagAbbreviation(EServerMonetizationTag monetization)
		{
			switch (monetization)
			{
				default:
					return null;

				case EServerMonetizationTag.None:
					return "MTXn";
				case EServerMonetizationTag.NonGameplay:
					return "MTXy";
				case EServerMonetizationTag.Monetized:
					return "MTXg";
			}
		}

		/// <summary>
		/// If missing map is a curated map then log information about how to install it.
		/// </summary>
		private static void maybeLogCuratedMapFallback(string attemptedMap)
		{
			if (statusData == null || statusData.Maps == null || statusData.Maps.Curated_Map_Links == null)
				return;

			foreach (CuratedMapLink link in statusData.Maps.Curated_Map_Links)
			{
				if (link.Name.Equals(attemptedMap, StringComparison.InvariantCultureIgnoreCase))
				{
					CommandWindow.LogWarningFormat("Attempting to load curated map '{0}'? Include its workshop file ID ({1}) in the WorkshopDownloadConfig.json File_IDs array.", link.Name, link.Workshop_File_Id);
					return;
				}
			}
		}

		internal static BuiltinAutoShutdown autoShutdownManager = null;
		private static IDedicatedWorkshopUpdateMonitor dswUpdateMonitor = null;
		private static bool isDedicatedUGCInstalled;

		/// <summary>
		/// Was not able to find documentation for this unfortunately,
		/// but it seems the max length is 127 characters as of 2022-09-12.
		/// </summary>
		private const int STEAM_KEYVALUE_MAX_VALUE_LENGTH = 127;

		private static void onDedicatedUGCInstalled()
		{
			if (isDedicatedUGCInstalled)
			{
				return;
			}
			isDedicatedUGCInstalled = true;

			apiWarningMessageHook = new Steamworks.SteamAPIWarningMessageHook_t(onAPIWarningMessage);
			SteamGameServerUtils.SetWarningMessageHook(apiWarningMessageHook);

			//SteamGameServerNetworking.AllowP2PPacketRelay(true);

			time = SteamGameServerUtils.GetServerRealTime();

			LevelInfo pendingLevel = Level.getLevel(map);
			if (pendingLevel == null)
			{
				string attemptedMap = map;
				maybeLogCuratedMapFallback(attemptedMap);
				map = "PEI";
				CommandWindow.LogError(localization.format("Map_Missing", attemptedMap, map));

				pendingLevel = Level.getLevel(map);
				if (pendingLevel == null)
				{
					CommandWindow.LogError("Fatal error: unable to load fallback map");
				}
			}

			if (pendingLevel != null)
			{
				// Fix capitalization of map name for server list. (public issue #3990)
				map = pendingLevel.name;
			}

			List<PublishedFileId_t> fileIdsForAssetMapping = null;
			if (_serverWorkshopFileIDs != null)
			{
				fileIdsForAssetMapping = new List<PublishedFileId_t>(_serverWorkshopFileIDs.Count);
				foreach (ulong fileId in _serverWorkshopFileIDs)
				{
					fileIdsForAssetMapping.Add(new PublishedFileId_t(fileId));
				}
			}
			Assets.ApplyServerAssetMapping(pendingLevel, fileIdsForAssetMapping);

			PhysicsMaterialNetTable.ServerPopulateTable();

			Level.load(pendingLevel, true);
			loadGameMode();
			applyLevelModeConfigOverrides();

			SteamGameServer.SetMaxPlayerCount(maxPlayers);
			SteamGameServer.SetServerName(serverName);
			SteamGameServer.SetPasswordProtected(serverPassword != "");
			SteamGameServer.SetMapName(map);

			if (Dedicator.IsDedicatedServer)
			{
				if (!ReadWrite.folderExists("/Bundles/Workshop/Content", true))
				{
					ReadWrite.createFolder("/Bundles/Workshop/Content", true);
				}

				string globalDir = "/Bundles/Workshop/Content";
				string[] globalFolders = ReadWrite.getFolders(globalDir);
				for (int index = 0; index < globalFolders.Length; index++)
				{
					string folder = ReadWrite.folderName(globalFolders[index]);
					ulong fileID;

					if (ulong.TryParse(folder, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fileID))
					{
						registerServerUsingWorkshopFileId(fileID);
						CommandWindow.Log("Recommended to add workshop item " + fileID + " to WorkshopDownloadConfig.json and remove it from " + globalDir);
					}
					else
					{
						CommandWindow.LogWarning("Invalid workshop item '" + folder + "' in " + globalDir);
					}
				}

				string serverLocalDir = ServerSavedata.directory + "/" + Provider.serverID + "/Workshop/Content";
				if (!ReadWrite.folderExists(serverLocalDir, true))
				{
					ReadWrite.createFolder(serverLocalDir, true);
				}

				string[] localFolders = ReadWrite.getFolders(serverLocalDir);
				for (int index = 0; index < localFolders.Length; index++)
				{
					string folder = ReadWrite.folderName(localFolders[index]);
					ulong fileID;

					if (ulong.TryParse(folder, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out fileID))
					{
						registerServerUsingWorkshopFileId(fileID);
						CommandWindow.Log("Recommended to add workshop item " + fileID + " to WorkshopDownloadConfig.json and remove it from " + serverLocalDir);
					}
					else
					{
						CommandWindow.LogWarning("Invalid workshop item '" + folder + "' in " + serverLocalDir);
					}
				}

				string level = new DirectoryInfo(Level.info.path).Parent.Name;
				ulong levelID;

				if (ulong.TryParse(level, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out levelID))
				{
					registerServerUsingWorkshopFileId(levelID);
				}

				// GameData
				{
					string gameData = serverPassword != "" ? "PASS" : "SSAP";
					gameData += ",";
					gameData += configData.Server.VAC_Secure ? "VAC_ON" : "VAC_OFF";
					gameData += ",GAME_VERSION_";
					gameData += VersionUtils.binaryToHexadecimal(APP_VERSION_PACKED);
					gameData += ",MAP_VERSION_";
					gameData += VersionUtils.binaryToHexadecimal(Level.packedVersion);

					if (_modInfo != null)
					{
						gameData += ",MOD_NAME_";
						gameData += _modInfo.FormatServerListName();
						gameData += ",MOD_VERSION_";
						gameData += VersionUtils.binaryToHexadecimal(_modInfo.GetPackedVersion());
					}
					else
					{
						gameData += ",MOD_NAME_NA,MOD_VERSION_NA";
					}

					SteamGameServer.SetGameData(gameData);
				}

				// Version numbers in "gamedata" are not available in query result, so include here as well.
				SteamGameServer.SetKeyValue("GameVersion", APP_VERSION);

				if (_modInfo != null)
				{
					SteamGameServer.SetKeyValue("ModName", _modInfo.Name);
					SteamGameServer.SetKeyValue("ModVersion", _modInfo.FormatModVersion());
				}

				// Tags were shortened to 2-3 letters to save space for thumb and desc
				int maxTagsLength = 128;
				string tags = (isPvP ? "PVP" : "PVE") + "," + (hasCheats ? "CHy" : "CHn") + ',' + getModeTagAbbreviation(mode) + "," + getCameraModeTagAbbreviation(cameraMode) + "," + (getServerWorkshopFileIDs().Count > 0 ? "WSy" : "WSn") + "," + (isGold ? "GLD" : "F2P");
				if (configData.Browser.Is_Using_Anycast_Proxy)
				{
					tags += ",ACP";
				}
#if WITH_THIRDPARTYAC
				tags += "," + (isThirdpartyAntiCheatActive ? "BEy" : "BEn");
				if (!hasSetIsThirdpartyAntiCheatActive)
				{
					CommandWindow.LogError("Order of things is messed up! isThirdpartyAntiCheatActive should have been set before advertising game server.");
				}
#endif
				string mtxTag = GetMonetizationTagAbbreviation(configData.Browser.Monetization);
				if (!string.IsNullOrEmpty(mtxTag))
				{
					tags += "," + mtxTag;
				}
				if (!string.IsNullOrEmpty(configData.Browser.Thumbnail))
				{
					tags += ",<tn>" + configData.Browser.Thumbnail + "</tn>";
				}
				tags += string.Format(",<net>{0}</net>", NetTransportFactory.GetTag(serverTransport));

				string pluginFramework = SteamPluginAdvertising.Get().PluginFrameworkTag;
				if (!string.IsNullOrEmpty(pluginFramework))
				{
					tags += string.Format(",<pf>{0}</pf>", pluginFramework);
				}

				//tags += ",<dc>" + configData.Browser.Desc_Server_List + "</dc>"; // Fallback incase GS desc is deprecated
				if (tags.Length > maxTagsLength)
				{
					CommandWindow.LogWarning("Server browser thumbnail URL is " + (tags.Length - maxTagsLength) + " characters over budget!");
					CommandWindow.LogWarning("Server will not list properly until this URL is adjusted!");
				}
				//else
				//{
				//	CommandWindow.Log("Server browser tags (thumbnail URL) have " + (maxTagsLength - tags.Length) + " spare characters left");
				//}
				SteamGameServer.SetGameTags(tags);

				int maxDescLength = 64;
				if (configData.Browser.Desc_Server_List.Length > maxDescLength)
				{
					CommandWindow.LogWarning("Server browser description is " + (configData.Browser.Desc_Server_List.Length - maxDescLength) + " characters over budget!");
				}
				SteamGameServer.SetGameDescription(configData.Browser.Desc_Server_List);

				SteamGameServer.SetKeyValue("Browser_Icon", configData.Browser.Icon);
				SteamGameServer.SetKeyValue("Browser_Desc_Hint", configData.Browser.Desc_Hint);
				SteamGameServer.SetKeyValue("BookmarkHost", configData.Browser.BookmarkHost);

				AdvertiseFullDescription(configData.Browser.Desc_Full);

				if (getServerWorkshopFileIDs().Count > 0)
				{
					string workshop = string.Empty;

					for (int index = 0; index < getServerWorkshopFileIDs().Count; index++)
					{
						if (workshop.Length > 0)
						{
							workshop += ',';
						}

						workshop += getServerWorkshopFileIDs()[index];
					}

					int workshopCount = ((workshop.Length - 1) / STEAM_KEYVALUE_MAX_VALUE_LENGTH) + 1;
					int workshopIndex = 0;
					SteamGameServer.SetKeyValue("Mod_Count", workshopCount.ToString());
					for (int step = 0; step < workshop.Length; step += STEAM_KEYVALUE_MAX_VALUE_LENGTH)
					{
						int length = STEAM_KEYVALUE_MAX_VALUE_LENGTH;
						if (step + length > workshop.Length)
						{
							length = workshop.Length - step;
						}

						string line = workshop.Substring(step, length);
						SteamGameServer.SetKeyValue("Mod_" + workshopIndex, line);
						workshopIndex++;
					}
				}

				if (configData.Browser.Links != null && configData.Browser.Links.Length > 0)
				{
					SteamGameServer.SetKeyValue("Custom_Links_Count", configData.Browser.Links.Length.ToString());
					for (int index = 0; index < configData.Browser.Links.Length; ++index)
					{
						BrowserConfigData.Link link = configData.Browser.Links[index];

						string messageBase64;
						if (!ConvertEx.TryEncodeUtf8StringAsBase64(link.Message, out messageBase64))
						{
							UnturnedLog.error($"Unable to encode lobby link message as Base64: \"{link.Message}\"");
							continue;
						}

						string urlBase64;
						if (!ConvertEx.TryEncodeUtf8StringAsBase64(link.Url, out urlBase64))
						{
							UnturnedLog.error($"Unable to encode lobby link url as Base64: \"{link.Url}\"");
							continue;
						}

						SteamGameServer.SetKeyValue("Custom_Link_Message_" + index, messageBase64);
						SteamGameServer.SetKeyValue("Custom_Link_Url_" + index, urlBase64);
					}
				}

				AdvertiseConfig();

				SteamPluginAdvertising.Get().NotifyGameServerReady();
			}

			dswUpdateMonitor = DedicatedWorkshopUpdateMonitorFactory.createForLevel(pendingLevel);

			_server = SteamGameServer.GetSteamID();
			_client = _server;
			_clientHash = Hash.SHA1(client);

			if (Dedicator.IsDedicatedServer)
			{
				_clientName = localization.format("Console");

				autoShutdownManager = steam.gameObject.AddComponent<BuiltinAutoShutdown>();

				{
					// I put this code directly in here to make it harder to patch out.

					// Needs to be updated for IPv6. Originally Steam only supported IPv4, but now supports IPv6.
					SteamIPAddress_t steamAddress = SteamGameServer.GetPublicIP();
					uint publicIp;
					steamAddress.TryGetIPv4Address(out publicIp);

					HostBans.EHostBanFlags flags = HostBans.HostBansManager.Get().MatchBasicDetails(new IPv4Address(publicIp), port, serverName, _server.m_SteamID);
					flags |= HostBans.HostBansManager.Get().MatchExtendedDetails(configData.Browser.Desc_Server_List, configData.Browser.Thumbnail);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
					UnturnedLog.info($"Checking host bans with these details: IP: {Parser.getIPFromUInt32(publicIp)} Port: {port} Name: \"{serverName}\" SteamID: {_server} Description: \"{configData.Browser.Desc_Server_List}\" Thumbnail: \"{configData.Browser.Thumbnail}\"");
					UnturnedLog.info($"Host ban flags: {flags}");
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD

					if ((flags & HostBans.EHostBanFlags.RecommendHostCheckWarningsList) != HostBans.EHostBanFlags.None)
					{
						CommandWindow.LogWarning("It appears this server has received a warning.");
						CommandWindow.LogWarning("Checking the Unturned Server Standing page is recommended:");
						CommandWindow.LogWarning("https://smartlydressedgames.com/UnturnedHostBans/index.html");
					}

					if (flags.HasFlag(HostBans.EHostBanFlags.Blocked))
					{
						shutdown();
					}
				}
			}

			timeLastPacketWasReceivedFromServer = Time.realtimeSinceStartup;
		}

		/// <summary>
		/// Set key/value tags on Steam server advertisement so that client can display text in browser.
		/// </summary>
		private static void AdvertiseFullDescription(string message)
		{
			if (string.IsNullOrEmpty(message))
				return;

			string base64String;
			if (!ConvertEx.TryEncodeUtf8StringAsBase64(message, out base64String))
			{
				UnturnedLog.error("Unable to encode server browser description to Base64");
				return;
			}

			if (string.IsNullOrEmpty(base64String))
			{
				UnturnedLog.error("Base64 server browser description was empty");
				return;
			}

			int descCount = ((base64String.Length - 1) / STEAM_KEYVALUE_MAX_VALUE_LENGTH) + 1;
			int descIndex = 0;
			SteamGameServer.SetKeyValue("Browser_Desc_Full_Count", descCount.ToString());
			for (int step = 0; step < base64String.Length; step += STEAM_KEYVALUE_MAX_VALUE_LENGTH)
			{
				int length = STEAM_KEYVALUE_MAX_VALUE_LENGTH;
				if (step + length > base64String.Length)
				{
					length = base64String.Length - step;
				}

				string line = base64String.Substring(step, length);
				SteamGameServer.SetKeyValue("Browser_Desc_Full_Line_" + descIndex, line);
				descIndex++;
			}
		}

		/// <summary>
		/// Set key/value tags on Steam server advertisement so that client can display server config in browser.
		/// </summary>
		private static void AdvertiseConfig()
		{
			int configCount = 0;

			Type configType = modeConfigData.GetType();
			FieldInfo[] categoryFields = configType.GetFields();
			foreach (FieldInfo categoryField in categoryFields)
			{
				Type categoryType = categoryField.FieldType;
				FieldInfo[] configFields = categoryType.GetFields();
				foreach (FieldInfo configField in configFields)
				{
					bool hasOverride = _modeConfigDataOverrides.TryGetValue(configField, out object overrideValue);
					if (!hasOverride || overrideValue == null)
					{
						continue;
					}

					string advertiseValue = null;
					Type fieldType = configField.FieldType;
					if (fieldType == typeof(bool))
					{
						bool currentBool = (bool) overrideValue;
						advertiseValue = categoryField.Name + "." + configField.Name + "=" + (currentBool ? "T" : "F");
					}
					else if (fieldType == typeof(float))
					{
						float currentFloat = (float) overrideValue;
						advertiseValue = categoryField.Name + "." + configField.Name + "=" + currentFloat.ToString(System.Globalization.CultureInfo.InvariantCulture);
					}
					else if (fieldType == typeof(uint))
					{
						uint currentInt = (uint) overrideValue;
						advertiseValue = categoryField.Name + "." + configField.Name + "=" + currentInt.ToString(System.Globalization.CultureInfo.InvariantCulture);
					}
					else
					{
						CommandWindow.LogErrorFormat("Unable to advertise config type: {0}", fieldType);
					}

					if (!string.IsNullOrEmpty(advertiseValue))
					{
						string advertiseKey = "Cfg_" + configCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
						++configCount;
						SteamGameServer.SetKeyValue(advertiseKey, advertiseValue);
					}
				}
			}

			SteamGameServer.SetKeyValue("Cfg_Count", configCount.ToString(System.Globalization.CultureInfo.InvariantCulture));
		}

		[System.Obsolete]
		public delegate void ServerWritingPacketHandler(CSteamID remoteSteamId, ESteamPacket type, byte[] payload, int size, int channel);
		[System.Obsolete]
		public static ServerWritingPacketHandler onServerWritingPacket;

		/// <summary>
		/// Primarily kept for backwards compatibility with plugins. Some RPCs that reply to sender also use this but
		/// should be tidied up.
		/// </summary>
		[System.Obsolete]
		public static void send(CSteamID steamID, ESteamPacket type, byte[] packet, int size, int channel)
		{
			ITransportConnection transportConnection = findTransportConnection(steamID);
			if (transportConnection != null)
			{
				sendToClient(transportConnection, type, packet, size);
			}
		}

		/// <summary>
		/// Hack to deal with the oversight of reordering the ESteamPacket enum during net messaging rewrite causing
		/// older plugins to send wrong packet type.
		/// </summary>
		[System.Obsolete]
		private static bool remapSteamPacketType(ref ESteamPacket type)
		{
			switch (type)
			{
				case ESteamPacket.KICKED: // 15
					type = ESteamPacket.UPDATE_RELIABLE_BUFFER;
					return true;

				case ESteamPacket.CONNECTED: // 16
					type = ESteamPacket.UPDATE_UNRELIABLE_BUFFER;
					return true;

				default:
					return false;
			}
		}

		/// <summary>
		/// Send to a connected client.
		/// </summary>
		[System.Obsolete]
		public static void sendToClient(ITransportConnection transportConnection, ESteamPacket type, byte[] packet, int size)
		{
			if (size < 1)
				throw new System.ArgumentOutOfRangeException("size");

			if (transportConnection == null)
				throw new ArgumentNullException("transportConnection");

#if WITH_GAME_THREAD_ASSERTIONS
			ThreadUtil.assertIsGameThread();
#endif

			if (!isConnected)
			{
				return;
			}

			if (!isServer)
			{
				throw new NotSupportedException("Sending to client while not running as server");
			}

			if (remapSteamPacketType(ref type))
			{
				packet[0] = (byte) type;
			}

			_bytesSent += (uint) size;
			_packetsSent++;

			ENetReliability sendType;

			if (isUnreliable(type))
			{
				sendType = ENetReliability.Unreliable;
			}
			else
			{
				sendType = ENetReliability.Reliable;
			}

			transportConnection.Send(packet, size, sendType);
		}

		/// <summary>
		/// The server ignores workshop info requests if it's been less than 30 seconds,
		/// so we cache that info for 1 minute in-case we try to connect again right away.
		/// </summary>
		internal class CachedWorkshopResponse
		{
			/// <summary>
			/// This information is needed before the level is loaded.
			/// </summary>
			public ENPCHoliday holiday;

			/// <summary>
			/// Advertised server name. e.g., "Nelson's Unturned Server"
			/// </summary>
			public string serverName;

			/// <summary>
			/// Name of map to load.
			/// </summary>
			public string levelName;

			public bool isPvP;
			public bool allowAdminCheatCodes;
			public bool isVACSecure;
#if WITH_THIRDPARTYAC
			public bool isThirdpartyAntiCheatEnabled;
#endif
			public bool isGold;

			/// <summary>
			/// Legacy difficulty mode that should be removed eventually.
			/// </summary>
			public EGameMode gameMode;

			/// <summary>
			/// Perspective settings.
			/// </summary>
			public ECameraMode cameraMode;

			public byte maxPlayers;

			public string bookmarkHost;
			public string thumbnailUrl;
			public string serverDescription;

			public CSteamID server;

			/// <summary>
			/// Server's IP from when we originally received response.
			/// Used to test download restrictions.
			/// </summary>
			public uint ip;

			public List<ServerRequiredWorkshopFile> requiredFiles = new List<ServerRequiredWorkshopFile>();

			/// <summary>
			/// Last realtime this cache was updated.
			/// </summary>
			public float realTime;

			internal bool FindRequiredFile(ulong fileId, out ServerRequiredWorkshopFile details)
			{
				foreach (ServerRequiredWorkshopFile requiredFile in requiredFiles)
				{
					if (requiredFile.fileId == fileId)
					{
						details = requiredFile;
						return true;
					}
				}

				details = default;
				return false;
			}
		}

		internal static List<CachedWorkshopResponse> cachedWorkshopResponses = new List<CachedWorkshopResponse>();

		private static HashSet<CSteamID> netIgnoredSteamIDs = new HashSet<CSteamID>();

		/// <summary>
		/// Hacked-together initial implementation to refuse network messages from specific players.
		/// On PC some cheats send garbage packets in which case those clients should be blocked.
		/// </summary>
		public static bool shouldNetIgnoreSteamId(CSteamID id)
		{
			return netIgnoredSteamIDs.Contains(id);
		}

		/// <summary>
		/// Close connection, and refuse all future connection attempts from a remote player.
		/// Used when garbage messages are received from hacked clients to avoid wasting time on them.
		/// </summary>
		public static void refuseGarbageConnection(CSteamID remoteId, string reason)
		{
			UnturnedLog.info("Refusing connections from " + remoteId + " (" + reason + ")");
			netIgnoredSteamIDs.Add(remoteId);
		}

		public static void refuseGarbageConnection(ITransportConnection transportConnection, string reason)
		{
			if (ReferenceEquals(transportConnection, null))
				throw new ArgumentNullException("transportConnection");

			transportConnection.CloseConnection();

			CSteamID steamId = findTransportConnectionSteamId(transportConnection);
			if (steamId != CSteamID.Nil)
			{
				refuseGarbageConnection(steamId, reason);
			}
		}

		/// <summary>
		/// Record that a bad packet was received from connection and maybe kick them if rate limit is exceeded.
		/// </summary>
		public static void IncrementBadPacketsFromConnection(ITransportConnection transportConnection)
		{
			if (ReferenceEquals(transportConnection, null))
				throw new ArgumentNullException("transportConnection");

			bool shouldKick = badMessageRateLimiter.IsBlocked(transportConnection);
			if (shouldKick)
			{
				UnturnedLog.info($"Connection {transportConnection} hit bad packet limit (window: {badMessageRateLimiter.window} s, threshold: {badMessageRateLimiter.threshold})");

				SteamPlayer player = findPlayer(transportConnection);
				if (player != null)
				{
					kick(player.playerID.steamID, "hit bad packet rate limit");
				}
				else
				{
					reject(transportConnection, ESteamRejection.BAD_PACKET_RATE_LIMITING);
				}
			}
		}

		/// <summary>
		/// Private to prevent plugins from changing the value.
		/// </summary>
		private static CommandLineFlag _constNetEvents = new CommandLineFlag(false, "-ConstNetEvents");

		/// <summary>
		/// Should buffers used by plugin network events be read-only copies?
		/// </summary>
		public static bool useConstNetEvents => _constNetEvents;

		public static bool hasNetBufferChanged(byte[] original, byte[] copy, int offset, int size)
		{
			for (int index = offset + size - 1; index >= offset; --index)
			{
				if (copy[index] != original[index])
				{
					return true;
				}
			}

			return false;
		}

		public delegate void ServerReadingPacketHandler(CSteamID remoteSteamId, byte[] payload, int offset, int size, int channel);
		[System.Obsolete]
		public static ServerReadingPacketHandler onServerReadingPacket;

		/// <summary>
		/// First four bytes of RPC messages are the channel id.
		/// </summary>
		internal static bool getChannelHeader(byte[] packet, int size, int offset, out int channel)
		{
			int channelOffset = offset + SteamChannel.RPC_HEADER_SIZE;
			if (channelOffset + SteamChannel.CHANNEL_ID_HEADER_SIZE > size)
			{
				channel = -1;
				return false;
			}

			channel = packet[channelOffset];
			return true;
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD
		/// <summary>
		/// Should players be allowed to join this server regardless of whether their version number matches ours?
		/// Useful to allow players to join debug mode servers.
		/// </summary>
		private static CommandLineFlag bypassVersion = new CommandLineFlag(false, "-BypassVersion");
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD

		/// <summary>
		/// Is version number supplied by client compatible with us?
		/// </summary>
		internal static bool canClientVersionJoinServer(uint version)
		{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
			if (bypassVersion)
			{
				return true;
			}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
			return version == APP_VERSION_PACKED;
		}

		internal static void legacyReceiveClient(byte[] packet, int offset, int size)
		{
			CSteamID steamID = server;

			_bytesReceived += (uint) size;
			_packetsReceived++;

			int channel;
			if (!getChannelHeader(packet, size, offset, out channel))
			{
				return;
			}

			SteamChannel component = findChannelComponent(channel);
			if (component != null)
			{
#pragma warning disable
				component.receive(steamID, packet, offset, size);
#pragma warning restore
			}
		}

#if TICKDEBUG
		private static float lastTick;
#endif

#if FAKELAG
		private static Queue<FakeLagMessage> fakeLag = new Queue<FakeLagMessage>();
#endif

		private static void listenServer()
		{
			long size;
			ITransportConnection clientId;
			while (serverTransport.Receive(buffer, out size, out clientId))
			{
#if FAKELAG
				FakeLagMessage message = new FakeLagMessage();
				message.transportConnection = clientId;
				message.data = new byte[size];
				Array.Copy(buffer, message.data, message.data.Length);
				message.timestamp = Time.realtimeSinceStartup;
				fakeLag.Enqueue(message);
#else // !FAKELAG
				NetMessages.ReceiveMessageFromClient(clientId, buffer, 0, (int) size);
#endif // !FAKELAG
			}

#if FAKELAG
			while(fakeLag.Count > 0)
			{
				FakeLagMessage message = fakeLag.Peek();

				if(Time.realtimeSinceStartup - message.timestamp < 0.5f)
				{
					break;
				}

				fakeLag.Dequeue();
				NetMessages.ReceiveMessageFromClient(message.transportConnection, message.data, 0, message.data.Length);
			}
#endif // !FAKELAG
		}

		private static void listenClient()
		{
			long size;
			while (clientTransport.Receive(buffer, out size))
			{
#if FAKELAG
				FakeLagMessage message = new FakeLagMessage();
				message.data = new byte[size];
				Array.Copy(buffer, message.data, message.data.Length);
				message.timestamp = Time.realtimeSinceStartup;
				fakeLag.Enqueue(message);
#else // !FAKELAG
				NetMessages.ReceiveMessageFromServer(buffer, 0, (int) size);
#endif // !FAKELAG
			}

#if FAKELAG
			while (fakeLag.Count > 0)
			{
				FakeLagMessage message = fakeLag.Peek();

				if (Time.realtimeSinceStartup - message.timestamp < 0.5f)
				{
					break;
				}

				fakeLag.Dequeue();
				NetMessages.ReceiveMessageFromServer(message.data, 0, message.data.Length);
			}
#endif // !FAKELAG
		}

		private void SendPingRequestToAllClients()
		{
			float realtime = Time.realtimeSinceStartup;
			foreach (SteamPlayer client in clients)
			{
				if (realtime - client.timeLastPingRequestWasSentToClient > 1 || client.timeLastPingRequestWasSentToClient < 0)
				{
					client.timeLastPingRequestWasSentToClient = realtime;
					NetMessages.SendMessageToClient(EClientMessage.PingRequest, ENetReliability.Unreliable, client.transportConnection, (NetPakWriter writer) => { });
				}
			}
		}

		/// <summary>
		/// Notify players waiting to join server if their position in the queue has changed.
		/// </summary>
		private void NotifyClientsInQueueOfPosition()
		{
			for (int queuePosition = 0; queuePosition < pending.Count; ++queuePosition)
			{
				if (pending[queuePosition].lastNotifiedQueuePosition != queuePosition)
				{
					pending[queuePosition].lastNotifiedQueuePosition = queuePosition;
					NetMessages.SendMessageToClient(EClientMessage.QueuePositionChanged, ENetReliability.Reliable, pending[queuePosition].transportConnection, (NetPakWriter writer) =>
					{
						writer.WriteUInt8(MathfEx.ClampToByte(queuePosition));
					});
				}
			}
		}

		private List<SteamPlayer> clientsWithBadConnecion = new List<SteamPlayer>();
		private void KickClientsWithBadConnection()
		{
			clientsWithBadConnecion.Clear();

			float realtime = Time.realtimeSinceStartup;
			float sumTimeSinceLastPacketWasReceivedFromClient = 0.0f;
			int numberOfClientsKickedForTimeout = 0;
			int sumPing = 0;
			int numberOfClientsKickedForHighPing = 0;
			foreach (SteamPlayer client in clients)
			{
				float timeSinceLastPacketWasReceivedFromClient = realtime - client.timeLastPacketWasReceivedFromClient;
				if (timeSinceLastPacketWasReceivedFromClient > configData.Server.Timeout_Game_Seconds)
				{
					if (CommandWindow.shouldLogJoinLeave)
					{
						SteamPlayerID playerID = client.playerID;
						CommandWindow.Log(localization.format("Dismiss_Timeout", playerID.steamID, playerID.playerName, playerID.characterName));
					}
					UnturnedLog.info($"Kicking {client.transportConnection} after {timeSinceLastPacketWasReceivedFromClient} s without message");
					clientsWithBadConnecion.Add(client);
					sumTimeSinceLastPacketWasReceivedFromClient += timeSinceLastPacketWasReceivedFromClient;
					++numberOfClientsKickedForTimeout;
					continue;
				}

				if (realtime - client.joined > configData.Server.Timeout_Game_Seconds)
				{
					int pingMs = Mathf.FloorToInt(client.ping * 1000.0f);
					if (pingMs > configData.Server.Max_Ping_Milliseconds)
					{
						if (CommandWindow.shouldLogJoinLeave)
						{
							SteamPlayerID playerID = client.playerID;
							CommandWindow.Log(localization.format("Dismiss_Ping", pingMs, configData.Server.Max_Ping_Milliseconds, playerID.steamID, playerID.playerName, playerID.characterName));
						}
						UnturnedLog.info($"Kicking {client.transportConnection} because their ping ({pingMs} ms) exceeds limit ({configData.Server.Max_Ping_Milliseconds} ms)");
						clientsWithBadConnecion.Add(client);
						sumPing += pingMs;
						numberOfClientsKickedForHighPing++;
					}
				}
			}

			if (clientsWithBadConnecion.Count > 1)
			{
				UnturnedLog.info($"Kicking {clientsWithBadConnecion.Count} clients with bad connection this frame. Maybe something blocked the main thread on the server? ({clientsWithBadConnecion.Count} clients kicked of {clients.Count} total clients)");

				float averageTimeSinceLastPacketWasReceivedFromKickedClient = numberOfClientsKickedForTimeout > 0 ?
						sumTimeSinceLastPacketWasReceivedFromClient / clientsWithBadConnecion.Count : 0.0f;

				UnturnedLog.info($"Kicking {numberOfClientsKickedForTimeout} for exceeding timeout limit ({configData.Server.Timeout_Game_Seconds} s) with average of {averageTimeSinceLastPacketWasReceivedFromKickedClient} s without message");

				int averagePingOfKickedClient = numberOfClientsKickedForHighPing > 0 ?
						sumPing / numberOfClientsKickedForHighPing : 0;

				UnturnedLog.info($"Kicking {numberOfClientsKickedForHighPing} for exceeding ping limit ({configData.Server.Max_Ping_Milliseconds} ms) with average of {averagePingOfKickedClient} ms ping");
			}

			foreach (SteamPlayer client in clientsWithBadConnecion)
			{
				try
				{
					dismiss(client.playerID.steamID);
				}
				catch (System.Exception exception)
				{
					UnturnedLog.exception(exception, "Caught exception while kicking client for bad connection:");
				}
			}
		}

		/// <summary>
		/// Prevent any particular client from delaying the server connection queue process.
		/// </summary>
		private void KickClientsBlockingUpQueue()
		{
			if (pending.Count < 1)
			{
				// Nobody in the queue.
				return;
			}

			float timeoutThreshold = configData.Server.GetClampedTimeoutQueueSeconds();

			SteamPending frontOfQueueClient = pending[0];
			if (frontOfQueueClient.hasSentVerifyPacket && frontOfQueueClient.realtimeSinceSentVerifyPacket > timeoutThreshold)
			{
				// Kicking front of queue players is necessary to prevent cheaters from blocking it up, but we
				// log the reasons to see what back end system might be failing.
				UnturnedLog.info("Front of queue player timed out: {0} ({1})",
					frontOfQueueClient.playerID.steamID,
					frontOfQueueClient.GetQueueStateDebugString());

				ESteamRejection rejection;
				if (!frontOfQueueClient.hasAuthentication && frontOfQueueClient.hasProof && frontOfQueueClient.hasGroup)
				{
					rejection = ESteamRejection.LATE_PENDING_STEAM_AUTH;
					UnturnedLog.info($"Server was only waiting for Steam authentication response for front of queue player, but {frontOfQueueClient.realtimeSinceSentVerifyPacket}s passed so we will give the next player a chance instead.");
				}
				else if (frontOfQueueClient.hasAuthentication && !frontOfQueueClient.hasProof && frontOfQueueClient.hasGroup)
				{
					rejection = ESteamRejection.LATE_PENDING_STEAM_ECON;
					UnturnedLog.info($"Server was only waiting for Steam economy/inventory details response for front of queue player, but {frontOfQueueClient.realtimeSinceSentVerifyPacket}s passed so we will give the next player a chance instead.");
				}
				else if (frontOfQueueClient.hasAuthentication && frontOfQueueClient.hasProof && !frontOfQueueClient.hasGroup)
				{
					rejection = ESteamRejection.LATE_PENDING_STEAM_GROUPS;
					UnturnedLog.info($"Server was only waiting for Steam group/clan details response for front of queue player, but {frontOfQueueClient.realtimeSinceSentVerifyPacket}s passed so we will give the next player a chance instead.");
				}
				else
				{
					rejection = ESteamRejection.LATE_PENDING;
					UnturnedLog.info($"Server was waiting for multiple responses about front of queue player, but {frontOfQueueClient.realtimeSinceSentVerifyPacket}s passed so we will give the next player a chance instead.");
				}

				reject(frontOfQueueClient.playerID.steamID, rejection);
				return;
			}

			// Front of queue player was not kicked, so check whether other players in the queue might need to be kicked.
			if (pending.Count > 1)
			{
				float realtime = Time.realtimeSinceStartup;
				for (int index = pending.Count - 1; index > 0; --index)
				{
					float timeSinceLastMessage = realtime - pending[index].lastReceivedPingRequestRealtime;
					if (timeSinceLastMessage > configData.Server.Timeout_Queue_Seconds)
					{
						SteamPending kickedPlayer = pending[index];
						UnturnedLog.info($"Kicking queued player {kickedPlayer.transportConnection} after {timeSinceLastMessage}s without message");
						reject(kickedPlayer.playerID.steamID, ESteamRejection.LATE_PENDING);
						break;
					}
				}
			}
		}

		private static void listen()
		{
			if (!isConnected)
			{
				return;
			}

#if TICKDEBUG
			if(Time.realtimeSinceStartup - lastTick < 0.25)
			{
				return;
			}
			lastTick = Time.realtimeSinceStartup;
#endif

			UnityEngine.Profiling.Profiler.BeginSample("Listen");
			if (isServer)
			{
				if (!Dedicator.IsDedicatedServer)
				{
					return; // added as a test, remove this stuff to start working on P2P
				}

				if (!Level.isLoaded)
				{
					return; // delay packets that might accidentally come in while loading level
				}

				TransportConnectionListPool.ReleaseAll();

				listenServer();

				if (Dedicator.IsDedicatedServer)
				{
					if (Time.realtimeSinceStartup - lastPingRequestTime > PING_REQUEST_INTERVAL)
					{
						lastPingRequestTime = Time.realtimeSinceStartup;
						steam.SendPingRequestToAllClients();
					}

					if (Time.realtimeSinceStartup - lastQueueNotificationTime > 6.0f)
					{
						lastQueueNotificationTime = Time.realtimeSinceStartup;
						steam.NotifyClientsInQueueOfPosition();
					}

					steam.KickClientsWithBadConnection();
					steam.KickClientsBlockingUpQueue();

					// Marker to make bulk disconnects easier to find in huge log files.
					if (steam.clientsKickedForTransportConnectionFailureCount > 1)
					{
						UnturnedLog.info($"Removed {steam.clientsKickedForTransportConnectionFailureCount} clients due to transport failure this frame");
					}
					steam.clientsKickedForTransportConnectionFailureCount = 0;
				}

				if (dswUpdateMonitor != null)
				{
					dswUpdateMonitor.tick(Time.deltaTime);
				}
			}
			else
			{
				listenClient();

				// May have disconnected during listenClient.
				if (!isConnected)
					return;

				if (isLoadingUGC)
				{
					if (isWaitingForWorkshopResponse)
					{
						float timeSinceWorkshopRequest = Time.realtimeSinceStartup - timeLastPacketWasReceivedFromServer;
						if (timeSinceWorkshopRequest > CLIENT_TIMEOUT)
						{
							_connectionFailureInfo = ESteamConnectionFailureInfo.TIMED_OUT;

							RequestDisconnect($"Server did not reply to workshop details request ({timeSinceWorkshopRequest}s elapsed)");
						}
						return;
					}

					timeLastPacketWasReceivedFromServer = Time.realtimeSinceStartup;
					return;
				}

				// Originally, we sent ping requests after Level.isLoading is finished. However, at some point Level.isLoading
				// started checking whether initial global state was received, which meant pings wouldn't be sent until client
				// finished logging in. This caused players to disconnect while in the server queue. (Public issue #5393.)
				if (hasSentReadyToLoginNotification && Time.realtimeSinceStartup - lastPingRequestTime > PING_REQUEST_INTERVAL)
				{
					if (Time.realtimeSinceStartup - timeLastPingRequestWasSentToServer > 1 || timeLastPingRequestWasSentToServer < 0)
					{
						lastPingRequestTime = Time.realtimeSinceStartup;
						timeLastPingRequestWasSentToServer = Time.realtimeSinceStartup;

						NetMessages.SendMessageToServer(EServerMessage.PingRequest, ENetReliability.Unreliable, (NetPakWriter writer) => { });
					}
				}

				if (Level.isLoading)
				{
					// Normal client timeout is ignored while loading the level because there are a lot of potentially
					// long blocking operations. Once the file IO portion of level loading is complete and connect request
					// is sent the client is just waiting, so we have a short timeout, otherwise the client may wait
					// indefinitely in the case that the server ignores the connection request.
					float timeSinceConnectRequest = Time.realtimeSinceStartup - timeLastPacketWasReceivedFromServer;
					if (isWaitingForConnectResponse && timeSinceConnectRequest > 10.0f)
					{
						_connectionFailureInfo = ESteamConnectionFailureInfo.TIMED_OUT;
						RequestDisconnect($"Server did not reply to connection request ({timeSinceConnectRequest}s elapsed)");
						return;
					}

#if !DEDICATED_SERVER
					if (isWaitingForAuthenticationResponse)
					{
						// Once authentication is sent client is just waiting. Server might take a bit of time for
						// backend requests, so we wait longer than for connect request. Timeout eventually otherwise
						// client may wait indefinitely in the case that the server ignores the connection request.
						//
						// Server has an equivalent timeout in case it sent EClientMessage.Verify and client did not reply.
						double timeSinceRequest = Time.realtimeSinceStartupAsDouble - sentAuthenticationRequestTime;
						if (timeSinceRequest > ServerConfigData.CLIENT_TIMEOUT_QUEUE_SECONDS)
						{
							_connectionFailureInfo = ESteamConnectionFailureInfo.TIMED_OUT_LOGIN;
							RequestDisconnect($"Server did not reply to authentication request ({timeSinceRequest}s elapsed)");
							return;
						}
					}
#endif // !DEDICATED_SERVER

					timeLastPacketWasReceivedFromServer = Time.realtimeSinceStartup;
					return;
				}

				float timeSinceLastMessageFromServer = Time.realtimeSinceStartup - timeLastPacketWasReceivedFromServer;
				if (timeSinceLastMessageFromServer > CLIENT_TIMEOUT)
				{
					_connectionFailureInfo = ESteamConnectionFailureInfo.TIMED_OUT;

					RequestDisconnect($"it has been {timeSinceLastMessageFromServer}s without a message from the server");
					return;
				}

#if WITH_THIRDPARTYAC
				if (CheckThirdpartyAntiCheatWantsRestart())
				{
					// method calls RequestDisconnect
					return;
				}
#endif

				if (catPouncingMechanism > -0.5f)
				{
					catPouncingMechanism -= Time.deltaTime;
					if (catPouncingMechanism < 0.01f)
					{
						catPouncingMechanism = -66.0f;
						_connectionFailureInfo = ESteamConnectionFailureInfo.HWID_MODIFIED;
						RequestDisconnect("HWID Modified");
						return;
					}
				}

				ClientAssetIntegrity.SendRequests();
			}
			UnityEngine.Profiling.Profiler.EndSample();
		}

#endregion

		#region GAMESERVER

		public delegate void ServerConnected(CSteamID steamID);
		public delegate void ServerDisconnected(CSteamID steamID);
		public delegate void ServerHosted();
		public delegate void ServerShutdown();

		public static ServerConnected onServerConnected;
		public static ServerDisconnected onServerDisconnected;
		public static ServerHosted onServerHosted;
		public static ServerShutdown onServerShutdown;

		private static void broadcastServerDisconnected(CSteamID steamID)
		{
			try
			{
				onServerDisconnected?.Invoke(steamID);
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Plugin raised an exception from onServerDisconnected:");
				UnturnedLog.exception(e);
			}
		}

		private static void broadcastServerHosted()
		{
			try
			{
				onServerHosted?.Invoke();
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Plugin raised an exception from onServerHosted:");
				UnturnedLog.exception(e);
			}
		}

		private static void broadcastServerShutdown()
		{
			try
			{
				onServerShutdown?.Invoke();
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Plugin raised an exception from onServerShutdown:");
				UnturnedLog.exception(e);
			}
		}

		// GSPolicyResponse is probably used to deactivate VAC checks if the Steam servers say m_bSecure == 0, but Unturned never did that properly (oops) and it works fine so I'm disabling it 
		//#pragma warning disable
		//		private static Callback<GSPolicyResponse_t> gsPolicyResponse;
		//#pragma warning restore
		//		private static void onGSPolicyResponse(GSPolicyResponse_t callback)
		//		{
		//			Profiler.BeginSample("onGSPolicyResponse");
		//			if(callback.m_bSecure != 0)
		//			{
		//				Dedicator.security = ESteamSecurity.SECURE;
		//			}
		//			else if(Dedicator.security == ESteamSecurity.SECURE)
		//			{
		//				Dedicator.security = ESteamSecurity.INSECURE;
		//			}
		//			Profiler.EndSample();
		//		}

#pragma warning disable
		private static Callback<P2PSessionConnectFail_t> p2pSessionConnectFail;
#pragma warning restore
		private static void onP2PSessionConnectFail(P2PSessionConnectFail_t callback)
		{
			UnityEngine.Profiling.Profiler.BeginSample("onP2PSessionConnectFail");
			// 2022-10-17 This should probably not be happening? Maybe only servers using old SteamNetworking?
			UnturnedLog.info($"Removing player {callback.m_steamIDRemote} due to P2P connect failure (Error: {callback.m_eP2PSessionError})");
			dismiss(callback.m_steamIDRemote);
			UnityEngine.Profiling.Profiler.EndSample();
		}

		[System.Obsolete]
		public delegate void CheckValid(ValidateAuthTicketResponse_t callback, ref bool isValid);
		[System.Obsolete("onCheckValidWithExplanation takes priority if bound")]
		public static CheckValid onCheckValid;
		public delegate void CheckValidWithExplanation(ValidateAuthTicketResponse_t callback, ref bool isValid, ref string explanation);
		public static CheckValidWithExplanation onCheckValidWithExplanation;

		public delegate void CheckBanStatusHandler(CSteamID steamID, uint remoteIP, ref bool isBanned, ref string banReason, ref uint banRemainingDuration);

		[System.Obsolete]
		public static CheckBanStatusHandler onCheckBanStatus;

		public delegate void CheckBanStatusWithHWIDHandler(SteamPlayerID playerID, uint remoteIP, ref bool isBanned, ref string banReason, ref uint banRemainingDuration);
		public static CheckBanStatusWithHWIDHandler onCheckBanStatusWithHWID;

		internal static void checkBanStatus(SteamPlayerID playerID, uint remoteIP, out bool isBanned, out string banReason, out uint banRemainingDuration)
		{
			isBanned = false;
			banReason = string.Empty;
			banRemainingDuration = 0;

			SteamBlacklistID blacklistID;
			if (SteamBlacklist.checkBanned(playerID.steamID, remoteIP, playerID.GetHwids(), out blacklistID))
			{
				isBanned = true;
				banReason = blacklistID.reason;
				banRemainingDuration = blacklistID.getTime();
			}

			try
			{
				if (onCheckBanStatusWithHWID != null)
				{
					onCheckBanStatusWithHWID.Invoke(playerID, remoteIP, ref isBanned, ref banReason, ref banRemainingDuration);
				}
#pragma warning disable
				else if (onCheckBanStatus != null)
				{
					onCheckBanStatus.Invoke(playerID.steamID, remoteIP, ref isBanned, ref banReason, ref banRemainingDuration);
				}
#pragma warning restore
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Plugin raised an exception from onCheckBanStatus:");
				UnturnedLog.exception(e);
			}
		}

		public delegate void RequestBanPlayerHandler(CSteamID instigator, CSteamID playerToBan, uint ipToBan, ref string reason, ref uint duration, ref bool shouldVanillaBan);
		[System.Obsolete("V2 provides list of HWIDs to ban")]
		public static RequestBanPlayerHandler onBanPlayerRequested;
		public delegate void RequestBanPlayerHandlerV2(CSteamID instigator, CSteamID playerToBan, uint ipToBan, IEnumerable<byte[]> hwidsToBan, ref string reason, ref uint duration, ref bool shouldVanillaBan);
		public static RequestBanPlayerHandlerV2 onBanPlayerRequestedV2;

		[System.Obsolete("Now accepts list of HWIDs to ban")]
		public static bool requestBanPlayer(CSteamID instigator, CSteamID playerToBan, uint ipToBan, string reason, uint duration)
		{
			return requestBanPlayer(instigator, playerToBan, ipToBan, null, reason, duration);
		}

		public static bool requestBanPlayer(CSteamID instigator, CSteamID playerToBan, uint ipToBan, IEnumerable<byte[]> hwidsToBan, string reason, uint duration)
		{
			bool shouldVanillaBan = true;

			try
			{
#pragma warning disable
				onBanPlayerRequested?.Invoke(instigator, playerToBan, ipToBan, ref reason, ref duration, ref shouldVanillaBan);
#pragma warning restore
			}
			catch (Exception e)
			{
				UnturnedLog.exception(e, "Plugin raised an exception from onBanPlayerRequested:");
			}

			try
			{
				onBanPlayerRequestedV2?.Invoke(instigator, playerToBan, ipToBan, hwidsToBan, ref reason, ref duration, ref shouldVanillaBan);
			}
			catch (Exception e)
			{
				UnturnedLog.exception(e, "Plugin raised an exception from onBanPlayerRequestedV2:");
			}

			if (shouldVanillaBan)
			{
				SteamBlacklist.ban(playerToBan, ipToBan, hwidsToBan, instigator, reason, duration);
			}
			return true;
		}

		public delegate void RequestUnbanPlayerHandler(CSteamID instigator, CSteamID playerToUnban, ref bool shouldVanillaUnban);
		public static RequestUnbanPlayerHandler onUnbanPlayerRequested;

		public static bool requestUnbanPlayer(CSteamID instigator, CSteamID playerToUnban)
		{
			bool shouldVanillaUnban = true;

			try
			{
				onUnbanPlayerRequested?.Invoke(instigator, playerToUnban, ref shouldVanillaUnban);
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Plugin raised an exception from onUnbanPlayerRequested:");
				UnturnedLog.exception(e);
			}

			if (shouldVanillaUnban)
			{
				return SteamBlacklist.unban(playerToUnban);
			}
			else
			{
				return true;
			}
		}

		private static void handleValidateAuthTicketResponse(ValidateAuthTicketResponse_t callback)
		{
			if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseOK)
			{
				SteamPending player = null;
				for (int index = 0; index < pending.Count; index++)
				{
					if (pending[index].playerID.steamID == callback.m_SteamID)
					{
						player = pending[index];
						break;
					}
				}

				if (player == null)
				{
					for (int index = 0; index < clients.Count; index++)
					{
						if (clients[index].playerID.steamID == callback.m_SteamID)
						{
							return; // Ignore this response if they're already playing on the server
						}
					}

					reject(callback.m_SteamID, ESteamRejection.NOT_PENDING);
					return;
				}

				bool isValid = true;
				string explanation = string.Empty;

				try
				{
					if (onCheckValidWithExplanation != null)
					{
						onCheckValidWithExplanation(callback, ref isValid, ref explanation);
					}
#pragma warning disable
					else if (onCheckValid != null)
					{
						onCheckValid(callback, ref isValid);
					}
#pragma warning restore
				}
				catch (Exception e)
				{
					UnturnedLog.warn("Plugin raised an exception from onCheckValidWithExplanation or onCheckValid:");
					UnturnedLog.exception(e);
				}

				if (!isValid)
				{
					reject(callback.m_SteamID, ESteamRejection.PLUGIN, explanation: explanation);
					return;
				}

#if EXPERIMENTAL
				bool isPro = true;
#else
				bool isPro = Steamworks.SteamGameServer.UserHasLicenseForApp(player.playerID.steamID, PRO_ID) != EUserHasLicenseForAppResult.k_EUserHasLicenseResultDoesNotHaveLicense;
#endif

				if (isGold && !isPro)
				{
					reject(player.playerID.steamID, ESteamRejection.PRO_SERVER);
					return;
				}

				if ((player.playerID.characterID >= Customization.FREE_CHARACTERS && !isPro) || player.playerID.characterID >= Customization.FREE_CHARACTERS + Customization.PRO_CHARACTERS)
				{
					reject(player.playerID.steamID, ESteamRejection.PRO_CHARACTER);
					return;
				}

				if (!isPro && player.isPro)
				{
					reject(player.playerID.steamID, ESteamRejection.PRO_DESYNC);
					return;
				}

				if (player.face >= Customization.FACES_FREE + Customization.FACES_PRO || (!isPro && player.face >= Customization.FACES_FREE))
				{
					reject(player.playerID.steamID, ESteamRejection.PRO_APPEARANCE);
					return;
				}

				if (player.hair >= Customization.HAIRS_FREE + Customization.HAIRS_PRO || (!isPro && player.hair >= Customization.HAIRS_FREE))
				{
					reject(player.playerID.steamID, ESteamRejection.PRO_APPEARANCE);
					return;
				}

				if (player.beard >= Customization.BEARDS_FREE + Customization.BEARDS_PRO || (!isPro && player.beard >= Customization.BEARDS_FREE))
				{
					reject(player.playerID.steamID, ESteamRejection.PRO_APPEARANCE);
					return;
				}

				if (!isPro)
				{
					if (!Customization.checkSkin(player.skin))
					{
						reject(player.playerID.steamID, ESteamRejection.PRO_APPEARANCE);
						return;
					}

					if (!Customization.checkColor(player.color))
					{
						reject(player.playerID.steamID, ESteamRejection.PRO_APPEARANCE);
						return;
					}
				}

				// If changing behaviour here remember to update ServerMessageHandler_Authenticate offline-only as well.

				player.assignedPro = isPro;
				player.assignedAdmin = SteamAdminlist.checkAdmin(player.playerID.steamID);

				player.hasAuthentication = true;

				if (player.canAcceptYet)
				{
					accept(player);
				}
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseUserNotConnectedToSteam)
			{
				reject(callback.m_SteamID, ESteamRejection.AUTH_NO_STEAM);
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseNoLicenseOrExpired)
			{
				reject(callback.m_SteamID, ESteamRejection.AUTH_LICENSE_EXPIRED);
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseVACBanned)
			{
				reject(callback.m_SteamID, ESteamRejection.AUTH_VAC_BAN);
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseLoggedInElseWhere)
			{
				reject(callback.m_SteamID, ESteamRejection.AUTH_ELSEWHERE);
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseVACCheckTimedOut)
			{
				reject(callback.m_SteamID, ESteamRejection.AUTH_TIMED_OUT);
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseAuthTicketCanceled)
			{
				if (CommandWindow.shouldLogJoinLeave)
				{
					CommandWindow.Log("Player finished session: " + callback.m_SteamID);
				}
				else
				{
					UnturnedLog.info("Player finished session: " + callback.m_SteamID);
				}
				dismiss(callback.m_SteamID); // player left server
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseAuthTicketInvalidAlreadyUsed)
			{
				reject(callback.m_SteamID, ESteamRejection.AUTH_USED);
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseAuthTicketInvalid)
			{
				reject(callback.m_SteamID, ESteamRejection.AUTH_NO_USER);
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponsePublisherIssuedBan)
			{
				reject(callback.m_SteamID, ESteamRejection.AUTH_PUB_BAN);
			}
			else if (callback.m_eAuthSessionResponse == EAuthSessionResponse.k_EAuthSessionResponseAuthTicketNetworkIdentityFailure)
			{
				reject(callback.m_SteamID, ESteamRejection.AUTH_NETWORK_IDENTITY_FAILURE);
			}
			else
			{
				if (CommandWindow.shouldLogJoinLeave)
				{
					CommandWindow.Log("Kicking player " + callback.m_SteamID + " for unknown session response " + callback.m_eAuthSessionResponse);
				}
				else
				{
					UnturnedLog.info("Kicking player " + callback.m_SteamID + " for unknown session response " + callback.m_eAuthSessionResponse);
				}
				dismiss(callback.m_SteamID); // unknown response
			}
		}

#pragma warning disable
		private static Callback<ValidateAuthTicketResponse_t> validateAuthTicketResponse;
#pragma warning restore
		private static void onValidateAuthTicketResponse(ValidateAuthTicketResponse_t callback)
		{
			UnityEngine.Profiling.Profiler.BeginSample("onValidateAuthTicketResponse");
			handleValidateAuthTicketResponse(callback);
			UnityEngine.Profiling.Profiler.EndSample();
		}

		private static void handleClientGroupStatus(GSClientGroupStatus_t callback)
		{
			SteamPending player = null;

			for (int index = 0; index < pending.Count; index++)
			{
				if (pending[index].playerID.steamID == callback.m_SteamIDUser)
				{
					player = pending[index];
					break;
				}
			}

			if (player == null)
			{
				reject(callback.m_SteamIDUser, ESteamRejection.NOT_PENDING);
				return;
			}

			if (!callback.m_bMember && !callback.m_bOfficer)
			{
				player.playerID.group = CSteamID.Nil;
			}

			player.hasGroup = true;

			if (player.canAcceptYet)
			{
				accept(player);
			}
		}

#pragma warning disable
		private static Callback<GSClientGroupStatus_t> clientGroupStatus;
#pragma warning restore
		private static void onClientGroupStatus(GSClientGroupStatus_t callback)
		{
			UnityEngine.Profiling.Profiler.BeginSample("onClientGroupStatus");
			handleClientGroupStatus(callback);
			UnityEngine.Profiling.Profiler.EndSample();
		}

		/// <summary>
		/// Allows hosting providers to limit the configurable max players value from the command-line.
		/// </summary>
		private static CommandLineInt clMaxPlayersLimit = new CommandLineInt("-MaxPlayersLimit");

		private static byte _maxPlayers;
		public static byte maxPlayers
		{
			get => _maxPlayers;

			set
			{
				_maxPlayers = value;
				if (clMaxPlayersLimit.hasValue && maxPlayers > clMaxPlayersLimit.value)
				{
					_maxPlayers = (byte) clMaxPlayersLimit.value;
					UnturnedLog.info("Clamped max players down from {0} to {1}", value, clMaxPlayersLimit.value);
				}

				if (isServer)
				{
					SteamGameServer.SetMaxPlayerCount(maxPlayers);
				}
			}
		}

		public static byte queueSize;

		internal static byte _queuePosition;
		public static byte queuePosition => _queuePosition;

		public delegate void QueuePositionUpdated();
		public static QueuePositionUpdated onQueuePositionUpdated;

		private static string _serverName;
		public static string serverName
		{
			get => _serverName;

			set
			{
				_serverName = value;

				if (Dedicator.commandWindow != null)
				{
					Dedicator.commandWindow.title = serverName;
				}

				if (isServer)
				{
					SteamGameServer.SetServerName(serverName);
				}
			}
		}

		public static string serverID
		{
			get => Dedicator.serverID;

			set => Dedicator.serverID = value;
		}

		/// <summary>
		/// Deprecated-ish IPv4 to bind listen socket to. Set by bind command.
		/// </summary>
		public static uint ip;

		/// <summary>
		/// Local address to bind listen socket to. Set by bind command.
		/// </summary>
		public static string bindAddress;

		/// <summary>
		/// Steam query port.
		/// </summary>
		public static ushort port;

		/// <summary>
		/// If hosting a server, get the game traffic port.
		/// </summary>
		public static ushort GetServerConnectionPort()
		{
			return (ushort) (port + 1);
		}

		internal static byte[] _serverPasswordHash;
		public static byte[] serverPasswordHash => _serverPasswordHash;

		private static string _serverPassword;
		public static string serverPassword
		{
			get => _serverPassword;

			set
			{
				_serverPassword = value;
				_serverPasswordHash = Hash.SHA1(serverPassword);

				if (isServer)
				{
					SteamGameServer.SetPasswordProtected(serverPassword != "");
				}
			}
		}

		public static string map;
		public static bool isPvP;
		public static bool isWhitelisted;
		public static bool hideAdmins;
		public static bool hasCheats;
		public static bool filterName;
		public static EGameMode mode;
		public static bool isGold;
		public static GameMode gameMode;

		public static ECameraMode cameraMode;

		private static StatusData _statusData;
		public static StatusData statusData => _statusData;

		internal static ModInfo _modInfo;
		/// <summary>
		/// Non-null if this is a custom build of the game.
		/// </summary>
		public static ModInfo GetModInfo() => _modInfo;

		private static PreferenceData _preferenceData;
		public static PreferenceData preferenceData => _preferenceData;

		private static ConfigData _configData;
		public static ConfigData configData => _configData;

		internal static ModeConfigData _modeConfigData;
		public static ModeConfigData modeConfigData => _modeConfigData;
		/// <summary>
		/// Populated when parsing modeConfigData. Level overrides check whether a property is overridden here before applying.
		/// </summary>
		private static Dictionary<FieldInfo, object> _modeConfigDataOverrides = new Dictionary<FieldInfo, object>();

		private static void applyLevelConfigOverride(FieldInfo field, object targetObject, KeyValuePair<string, object> levelOverride)
		{
			Type fieldType = field.FieldType;
			if (fieldType == typeof(bool))
			{
				field.SetValue(targetObject, Convert.ToBoolean(levelOverride.Value));
			}
			else if (fieldType == typeof(float))
			{
				field.SetValue(targetObject, Convert.ToSingle(levelOverride.Value));
			}
			else if (fieldType == typeof(uint))
			{
				field.SetValue(targetObject, Convert.ToUInt32(levelOverride.Value));
			}
			else
			{
				CommandWindow.LogErrorFormat("Unable to handle level mode config override type: {0} ({1})", fieldType, levelOverride.Key);
				return;
			}

			CommandWindow.LogFormat("Level overrides config {0}: {1}", levelOverride.Key, levelOverride.Value);
		}

		private static CommandLineFlag clNoLevelConfigOverrides = new CommandLineFlag(false, "-NoLevelConfigOverrides");
		public static void applyLevelModeConfigOverrides()
		{
			if (Level.info == null || Level.info.configData == null)
				return;

			if (clNoLevelConfigOverrides)
			{
				CommandWindow.Log($"Skipping all level config overrides because {clNoLevelConfigOverrides.flag} is enabled");
				return;
			}

			if (Level.info.configData.Mode_Config_Overrides != null && Level.info.configData.Mode_Config_Overrides.Count > 0)
			{
				ApplyLevelModeConfigOverrides(Level.info.configData.Mode_Config_Overrides);
			}

			Dictionary<string, object> perDifficulty = Level.info.configData.GetPerDifficultyConfigOverrides(mode);
			if (perDifficulty != null && perDifficulty.Count > 0)
			{
				UnturnedLog.info($"Level has {perDifficulty.Count} override(s) specific to {mode}:");
				ApplyLevelModeConfigOverrides(perDifficulty);
			}
		}

		private static void ApplyLevelModeConfigOverrides(Dictionary<string, object> levelOverrides)
		{
			foreach (KeyValuePair<string, object> modeConfigOverride in levelOverrides)
			{
				if (string.IsNullOrEmpty(modeConfigOverride.Key))
				{
					CommandWindow.LogError("Level mode config overrides contains an empty key");
					return;
				}

				if (modeConfigOverride.Value == null)
				{
					CommandWindow.LogError("Level mode config overrides contains a null value");
					return;
				}

				// If implementing other restrictions we should make this an attribute.
				if (modeConfigOverride.Key == "Gameplay.Disable_Motion_Sickness_Options")
				{
					CommandWindow.LogWarning($"Level cannot override {modeConfigOverride.Key}");
					continue;
				}

				Type targetType = typeof(ModeConfigData);
				object targetObject = modeConfigData;
				string[] fieldNames = modeConfigOverride.Key.Split('.');
				for (int fieldNameIndex = 0; fieldNameIndex < fieldNames.Length; fieldNameIndex++)
				{
					string fieldName = fieldNames[fieldNameIndex];
					FieldInfo field = targetType.GetField(fieldName);
					if (field == null)
					{
						CommandWindow.LogError("Failed to find mode config for level override: " + fieldName);
						break;
					}

					if (fieldNameIndex == fieldNames.Length - 1)
					{
						if (_modeConfigDataOverrides.ContainsKey(field))
						{
							CommandWindow.Log($"Skipping level config override {modeConfigOverride.Key} because it's overridden in server config");
							break;
						}

						try
						{
							applyLevelConfigOverride(field, targetObject, modeConfigOverride);
						}
						catch (Exception e) // Probably a casting exception. Ideally we make this more robust.
						{
							CommandWindow.LogError("Exception when applying level config override: " + modeConfigOverride.Key);
							UnturnedLog.exception(e);
							break;
						}
					}
					else
					{
						targetType = field.FieldType;
						targetObject = field.GetValue(targetObject);
					}
				}
			}
		}

		public static void accept(SteamPending player)
		{
			accept(player.playerID, player.assignedPro, player.assignedAdmin, player.face, player.hair, player.beard, player.skin, player.color, player.markerColor, player.BeardColor, player.hand, player.shirtItem, player.pantsItem, player.hatItem, player.backpackItem, player.vestItem, player.maskItem, player.glassesItem, player.skinItems, player.skinTags, player.skinDynamicProps, player.skillset, player.language, player.lobbyID, player.clientPlatform);
		}

		/// <summary>
		/// Used to build packet about each existing player for new player, and then once to build a packet
		/// for existing players about the new player. Note that in this second case forPlayer is null
		/// because the packet is re-used.
		/// </summary>
		private static void WriteConnectedMessage(NetPakWriter writer, SteamPlayer aboutPlayer, SteamPlayer forPlayer)
		{
			bool isAboutSelf = aboutPlayer == forPlayer;

			writer.WriteNetId(aboutPlayer.GetNetId());
			writer.WriteSteamID(aboutPlayer.playerID.steamID);
			writer.WriteUInt8(aboutPlayer.playerID.characterID);
			writer.WriteString(aboutPlayer.playerID.playerName);
			writer.WriteString(aboutPlayer.playerID.characterName);

			Vector3 sendPosition;
			byte sendCompressedYaw;
			if (!aboutPlayer.player.movement.hasMostRecentlyAddedUpdate)
			{
				sendPosition = aboutPlayer.model.transform.position;
				sendCompressedYaw = MeasurementTool.angleToByte(aboutPlayer.model.transform.rotation.eulerAngles.y);
			}
			else
			{
				sendPosition = aboutPlayer.player.movement.mostRecentlyAddedUpdate.pos;
				sendCompressedYaw = aboutPlayer.player.movement.mostRecentlyAddedUpdate.rot;
			}

			if (!isAboutSelf)
			{
				Vector3 recipientPosition = forPlayer.model.transform.position;
				if (PlayerManager.IsPlayerCulledAtPosition(aboutPlayer, sendPosition, forPlayer, recipientPosition))
				{
					sendPosition = PlayerManager.CulledPosition;
					sendCompressedYaw = 0;
					forPlayer.culledPlayers.Add(aboutPlayer.playerID.steamID);
				}
			}

			writer.WriteClampedVector3(sendPosition);
			writer.WriteUInt8(sendCompressedYaw);

			writer.WriteBit(aboutPlayer.isPro);

			bool isAdmin = aboutPlayer.isAdmin;
			if (!isAboutSelf && hideAdmins)
			{
				isAdmin = false;
			}
			writer.WriteBit(isAdmin);

			writer.WriteUInt8((byte) aboutPlayer.channel);
			writer.WriteSteamID(aboutPlayer.playerID.group);
			writer.WriteString(aboutPlayer.playerID.nickName);
			writer.WriteUInt8(aboutPlayer.face);
			writer.WriteUInt8(aboutPlayer.hair);
			writer.WriteUInt8(aboutPlayer.beard);
			writer.WriteColor32RGB(aboutPlayer.skin);
			writer.WriteColor32RGB(aboutPlayer.color);
			writer.WriteColor32RGB(aboutPlayer.markerColor);
			writer.WriteColor32RGB(aboutPlayer.BeardColor);
			writer.WriteBit(aboutPlayer.IsLeftHanded);
			writer.WriteInt32(aboutPlayer.shirtItem);
			writer.WriteInt32(aboutPlayer.pantsItem);
			writer.WriteInt32(aboutPlayer.hatItem);
			writer.WriteInt32(aboutPlayer.backpackItem);
			writer.WriteInt32(aboutPlayer.vestItem);
			writer.WriteInt32(aboutPlayer.maskItem);
			writer.WriteInt32(aboutPlayer.glassesItem);

			int[] skinItems = aboutPlayer.skinItems;
			writer.WriteUInt8((byte) skinItems.Length);
			foreach (int skin in skinItems)
			{
				writer.WriteInt32(skin);
			}

			string[] skinTags = aboutPlayer.skinTags;
			writer.WriteUInt8((byte) skinTags.Length);
			foreach (string tag in skinTags)
			{
				writer.WriteString(tag);
			}

			string[] skinDynamicProps = aboutPlayer.skinDynamicProps;
			writer.WriteUInt8((byte) skinDynamicProps.Length);
			foreach (string dynProps in skinDynamicProps)
			{
				writer.WriteString(dynProps);
			}

			writer.WriteEnum(aboutPlayer.skillset);
			writer.WriteString(aboutPlayer.language);
		}

		/// <summary>
		/// Not exactly ideal, but this a few old "once per player" client->server RPCs.
		/// </summary>
		private static void SendInitialGlobalState(SteamPlayer client)
		{
			PhysicsMaterialNetTable.Send(client.transportConnection);
			LightingManager.SendInitialGlobalState(client);
			// Vehicle is sent after players because it will parent them into seats.
			VehicleManager.SendInitialGlobalState(client);
			AnimalManager.SendInitialGlobalState(client.transportConnection);
			LevelManager.SendInitialGlobalState(client);
			ZombieManager.SendInitialGlobalState(client);
		}

		[System.Obsolete("This should not have been public in the first place")]
		public static void accept(SteamPlayerID playerID, bool isPro, bool isAdmin, byte face, byte hair, byte beard, Color skin, Color color, Color markerColor, bool hand, int shirtItem, int pantsItem, int hatItem, int backpackItem, int vestItem, int maskItem, int glassesItem, int[] skinItems, string[] skinTags, string[] skinDynamicProps, EPlayerSkillset skillset, string language, CSteamID lobbyID)
		{
			accept(playerID, isPro, isAdmin, face, hair, beard, skin, color, markerColor, color, hand, shirtItem, pantsItem, hatItem, backpackItem, vestItem, maskItem, glassesItem, skinItems, skinTags, skinDynamicProps, skillset, language, lobbyID, default);
		}

		internal static void accept(SteamPlayerID playerID, bool isPro, bool isAdmin, byte face, byte hair, byte beard, Color skin, Color color, Color markerColor, Color beardColor, bool hand, int shirtItem, int pantsItem, int hatItem, int backpackItem, int vestItem, int maskItem, int glassesItem, int[] skinItems, string[] skinTags, string[] skinDynamicProps, EPlayerSkillset skillset, string language, CSteamID lobbyID, EClientPlatform clientPlatform)
		{
			ITransportConnection clientId = null;
			bool isPending = false;
			int pendingIndex = 0;

			for (int index = 0; index < pending.Count; index++)
			{
				if (pending[index].playerID == playerID)
				{
					if (pending[index].inventoryResult != SteamInventoryResult_t.Invalid)
					{
						SteamGameServerInventory.DestroyResult(pending[index].inventoryResult);
						pending[index].inventoryResult = SteamInventoryResult_t.Invalid;
					}

					clientId = pending[index].transportConnection;
					isPending = true;
					pendingIndex = index;
					pending.RemoveAt(index);
					if (!ReferenceEquals(clientId, null))
					{
						_transportConnectionToPendingPlayerMap.Remove(clientId);
					}
					break;
				}
			}

			if (!isPending)
			{
				UnturnedLog.info($"Ignoring call to accept {playerID} because they are not in the queue");
				return;
			}

			UnturnedLog.info($"Accepting queued player {playerID}");

			NetMessages.SendMessageToClient(EClientMessage.ReplicateConfig, ENetReliability.Reliable, clientId, (NetPakWriter writer) =>
			{
				writer.WriteUInt8((byte) modeConfigData.Gameplay.Repair_Level_Max);
				writer.WriteFloat(modeConfigData.Players.Skill_Cost_Multiplier);
				writer.WriteBit(modeConfigData.Players.Skillset_Reduces_Skill_Cost);
				writer.WriteBit(modeConfigData.Players.Prevent_Level_Skill_Overrides);
				writer.WriteBit(modeConfigData.Gameplay.Hitmarkers);
				writer.WriteBit(modeConfigData.Gameplay.Crosshair);
				writer.WriteBit(modeConfigData.Gameplay.Ballistics);
				writer.WriteBit(modeConfigData.Gameplay.Chart);
				writer.WriteBit(modeConfigData.Gameplay.Satellite);
				writer.WriteBit(modeConfigData.Gameplay.Compass);
				writer.WriteBit(modeConfigData.Gameplay.Group_Map);
				writer.WriteBit(modeConfigData.Gameplay.Group_HUD);
				writer.WriteBit(modeConfigData.Gameplay.Group_Player_List);
				writer.WriteBit(modeConfigData.Gameplay.Allow_Static_Groups);
				writer.WriteBit(modeConfigData.Gameplay.Allow_Dynamic_Groups);
				writer.WriteBit(modeConfigData.Gameplay.Allow_Shoulder_Camera);
				writer.WriteBit(modeConfigData.Gameplay.Can_Suicide);
				writer.WriteBit(modeConfigData.Gameplay.Friendly_Fire);
				writer.WriteBit(modeConfigData.Gameplay.Bypass_Buildable_Mobility);
				writer.WriteBit(modeConfigData.Gameplay.Bypass_Building_In_Safezones);
				writer.WriteBit(modeConfigData.Gameplay.Bypass_No_Building_Zones);
				writer.WriteBit(modeConfigData.Gameplay.Allow_Freeform_Buildables);
				writer.WriteBit(modeConfigData.Gameplay.Allow_Freeform_Buildables_On_Vehicles);
				writer.WriteBit(modeConfigData.Gameplay.Enable_Damage_Flinch);
				writer.WriteBit(modeConfigData.Gameplay.Enable_Explosion_Camera_Shake);
				writer.WriteBit(modeConfigData.Gameplay.Enable_Workstation_Requirements);
				writer.WriteBit(modeConfigData.Gameplay.Disable_Motion_Sickness_Options);
				writer.WriteBit(modeConfigData.Gameplay.Disable_Foliage_Off);
				writer.WriteBit(modeConfigData.Gameplay.Use_2D_Scope_Overlay);
				writer.WriteBit(modeConfigData.Gameplay.Enable_Fishing_Catch_Challenge);
				writer.WriteUInt16((ushort) modeConfigData.Gameplay.Timer_Exit);
				writer.WriteUInt16((ushort) modeConfigData.Gameplay.Timer_Respawn);
				writer.WriteUInt16((ushort) modeConfigData.Gameplay.Timer_Home);
				writer.WriteUInt16((ushort) modeConfigData.Gameplay.Max_Group_Members);
				writer.WriteBit(modeConfigData.Barricades.Allow_Item_Placement_On_Vehicle);
				writer.WriteBit(modeConfigData.Barricades.Allow_Trap_Placement_On_Vehicle);
				writer.WriteFloat(modeConfigData.Barricades.Max_Item_Distance_From_Hull);
				writer.WriteFloat(modeConfigData.Barricades.Max_Trap_Distance_From_Hull);
				writer.WriteFloat(modeConfigData.Gameplay.AirStrafing_Acceleration_Multiplier);
				writer.WriteFloat(modeConfigData.Gameplay.AirStrafing_Deceleration_Multiplier);
				writer.WriteFloat(modeConfigData.Gameplay.FirstPerson_RecoilMultiplier);
				writer.WriteFloat(modeConfigData.Gameplay.FirstPerson_AimingRecoilMultiplier);
				writer.WriteFloat(modeConfigData.Gameplay.FirstPerson_AimingZoomRecoilReduction);
				writer.WriteFloat(modeConfigData.Gameplay.ThirdPerson_RecoilMultiplier);
				writer.WriteFloat(modeConfigData.Gameplay.ThirdPerson_SpreadMultiplier);
				writer.WriteFloat(modeConfigData.Gameplay.Viewmodel_AimingJumpLandMultiplier);
				writer.WriteFloat(modeConfigData.Gameplay.Viewmodel_AimingMisalignmentMultiplier);
				writer.WriteFloat(modeConfigData.Gameplay.Min_Fishing_Bite_Interval);
				writer.WriteFloat(modeConfigData.Gameplay.Max_Fishing_Bite_Interval);
				writer.WriteFloat(modeConfigData.Gameplay.Fishing_MaxStrength_Bite_Interval_Multiplier);
			});

			// Per-player info on the server list's details window.
			{
				// Previously this listed the playerName, but cheaters were using that to search the server list
				// for specific streamers / popular community members. Character name allows some more anonymity.
				string displayName = playerID.characterName;

				// Steam shows "score" on the server list, but we have no in-game score.
				uint score = isPro ? 1 : (uint) 0;

				SteamGameServer.BUpdateUserData(playerID.steamID, displayName, score);
			}

			Vector3 point;
			byte angle;
			EPlayerStance initialStance;
			loadPlayerSpawn(playerID, out point, out angle, out initialStance);

			int channel = allocPlayerChannelId();
			NetId netId = ClaimNetIdBlockForNewPlayer();

			SteamPlayer newClient = addPlayer(clientId, netId, playerID, point, angle, isPro, isAdmin, channel, face, hair, beard, skin, color, markerColor, beardColor, hand, shirtItem, pantsItem, hatItem, backpackItem, vestItem, maskItem, glassesItem, skinItems, skinTags, skinDynamicProps, skillset, language, lobbyID, clientPlatform);
#if WITH_THIRDPARTYAC
			newClient.thirdpartyAntiCheatId = AllocThirdpartyAntiCheatPlayerId();
#endif

			PlayerStance stanceComponent = newClient.player.GetComponent<PlayerStance>();
			if (stanceComponent != null)
			{
				stanceComponent.initialStance = initialStance;
			}
			else
			{
				UnturnedLog.warn("Was unable to get PlayerStance for new connection!");
			}

			// Tell newly connected player about all existing players and themself
			foreach (SteamPlayer aboutClient in _clients)
			{
				try
				{
					NetMessages.SendMessageToClient(EClientMessage.PlayerConnected, ENetReliability.Reliable, newClient.transportConnection, (NetPakWriter writer) =>
					{
						WriteConnectedMessage(writer, aboutClient, newClient);
					});
				}
				catch (System.Exception exception)
				{
					// Nelson 2024-11-14: Catching here to help mitigate public issue #4760.
					UnturnedLog.exception(exception, $"Caught exception sending PlayerConnected message about {aboutClient} to new client {newClient}:");
					UnturnedLog.error("This is likely a fatal error!");
				}
			}

			uint ipForClient;
			ushort queryPortForClient;
			GetAddressAndPortForClientAdvertisement(out ipForClient, out queryPortForClient);

			NetMessages.SendMessageToClient(EClientMessage.Accepted, ENetReliability.Reliable, clientId, (NetPakWriter writer) =>
			{
				writer.WriteUInt32(ipForClient);
				writer.WriteUInt16(queryPortForClient);
			});

#if WITH_THIRDPARTYAC
			AddClientToThirdpartyAntiCheat(clientId, playerID, newClient);
#endif

			// Tell existing players about the newly connected player
			foreach (SteamPlayer forClient in clients)
			{
				if (forClient == newClient)
				{
					// Already told about themselves earlier.
					continue;
				}

				try
				{
					NetMessages.SendMessageToClient(EClientMessage.PlayerConnected, ENetReliability.Reliable, forClient.transportConnection, (NetPakWriter writer) =>
					{
						WriteConnectedMessage(writer, newClient, forClient);
					});
				}
				catch (System.Exception exception)
				{
					// Nelson 2024-11-14: Catching here to help mitigate public issue #4760.
					UnturnedLog.exception(exception, $"Caught exception sending PlayerConnected message for new client {newClient} to existing clients:");
					UnturnedLog.error("This is likely a fatal error!");
				}
			}

			SendInitialGlobalState(newClient);

			newClient.player.InitializePlayer();

			// Tell newly connected player about all existing player states and their own state.
			foreach (SteamPlayer client in _clients)
			{
				client.player.SendInitialPlayerState(newClient);
			}
			// Tell existing players about the newly connected player state.
			newClient.player.SendInitialPlayerState(GatherRemoteClientConnectionsMatchingPredicate(
			(SteamPlayer potentialRecipient) =>
			{
				return potentialRecipient != newClient;
			}
			));

			try
			{
				onServerConnected?.Invoke(playerID.steamID);
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Plugin raised an exception from onServerConnected:");
				UnturnedLog.exception(e);
			}

			if (CommandWindow.shouldLogJoinLeave)
			{
				CommandWindow.Log(localization.format("PlayerConnectedText", playerID.steamID, playerID.playerName, playerID.characterName));
			}
			else
			{
				UnturnedLog.info(localization.format("PlayerConnectedText", playerID.steamID, playerID.playerName, playerID.characterName));
			}

			bool wasAtFrontOfQueue = pendingIndex == 0;
			if (wasAtFrontOfQueue)
			{
				verifyNextPlayerInQueue();
			}
		}

		private static void GetAddressAndPortForClientAdvertisement(out uint ip, out ushort queryPort)
		{
			if (configData.Server.Use_FakeIP)
			{
				SteamGameServerNetworkingSockets.GetFakeIP(0, out SteamNetworkingFakeIPResult_t fakeIpInfo);
				if (fakeIpInfo.m_eResult == EResult.k_EResultOK && fakeIpInfo.m_unPorts != null && fakeIpInfo.m_unPorts.Length > 0)
				{
					ip = fakeIpInfo.m_unIP;
					queryPort = fakeIpInfo.m_unPorts[0];
				}
				else
				{
					ip = 0;
					queryPort = 0;
				}
			}
			else
			{
				SteamGameServer.GetPublicIP().TryGetIPv4Address(out ip);
				queryPort = port;
			}
		}

		public delegate void RejectingPlayerCallback(CSteamID steamID, ESteamRejection rejection, string explanation);

		/// <summary>
		/// Event for plugins when rejecting a player.
		/// </summary>
		public static event RejectingPlayerCallback onRejectingPlayer;

		private static void broadcastRejectingPlayer(CSteamID steamID, ESteamRejection rejection, string explanation)
		{
			try
			{
				onRejectingPlayer?.Invoke(steamID, rejection, explanation);
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Plugin raised an exception from onRejectingPlayer:");
				UnturnedLog.exception(e);
			}
		}

		public static void reject(CSteamID steamID, ESteamRejection rejection)
		{
			reject(steamID, rejection, string.Empty);
		}

		// exposed for econ (messy!)
		public static void reject(CSteamID steamID, ESteamRejection rejection, string explanation)
		{
			ITransportConnection connection = findTransportConnection(steamID);
			if (connection != null)
			{
				reject(connection, rejection, explanation);
			}
		}

		public static void reject(ITransportConnection transportConnection, ESteamRejection rejection)
		{
			reject(transportConnection, rejection, string.Empty);
		}

		public static void reject(ITransportConnection transportConnection, ESteamRejection rejection, string explanation)
		{
			if (ReferenceEquals(transportConnection, null))
				throw new ArgumentNullException("transportConnection");

#if WITH_GAME_THREAD_ASSERTIONS
			ThreadUtil.assertIsGameThread();
#endif
			CSteamID steamId = findTransportConnectionSteamId(transportConnection);

			if (steamId != CSteamID.Nil)
			{
				broadcastRejectingPlayer(steamId, rejection, explanation);
			}

			for (int index = 0; index < pending.Count; index++)
			{
				if (transportConnection.Equals(pending[index].transportConnection))
				{
					if (rejection == ESteamRejection.AUTH_VAC_BAN)
					{
						ChatManager.say(pending[index].playerID.playerName + " was banned by VAC", Color.yellow);
					}
					else if (rejection == ESteamRejection.AUTH_PUB_BAN)
					{
						string suffix =
#if WITH_THIRDPARTYAC
						ThirdpartyAntiCheat.GamebanSuffix;
#else
						" was banned by anti-cheat";
#endif
						ChatManager.say(pending[index].playerID.playerName + suffix, Color.yellow);
					}

					if (pending[index].inventoryResult != SteamInventoryResult_t.Invalid)
					{
						SteamGameServerInventory.DestroyResult(pending[index].inventoryResult);
						pending[index].inventoryResult = SteamInventoryResult_t.Invalid;
					}

					pending.RemoveAt(index);
					_transportConnectionToPendingPlayerMap.Remove(transportConnection);

					bool wasAtFrontOfQueue = index == 0;
					if (wasAtFrontOfQueue)
					{
						verifyNextPlayerInQueue();
					}

					// Don't return! See below.
					break;
				}
			}

			// Server has to cancel ticket and send rejection method even if not pending,
			// because reject can be called prior to the player being accepted as pending.

			SteamGameServer.EndAuthSession(steamId);

			NetMessages.SendMessageToClient(EClientMessage.Rejected, ENetReliability.Reliable, transportConnection, (NetPakWriter writer) =>
			{
				writer.WriteEnum(rejection);
				writer.WriteString(explanation);
			});

			transportConnection.CloseConnection();
		}

		[System.Obsolete]
		internal static void notifyClientPending(ITransportConnection transportConnection)
		{ }

		private static bool findClientForKickBanDismiss(CSteamID steamID, out SteamPlayer foundClient, out byte foundIndex)
		{
			for (byte index = 0; index < clients.Count; ++index)
			{
				SteamPlayer potentialClient = clients[index];
				if (potentialClient.playerID.steamID == steamID)
				{
					foundClient = potentialClient;
					foundIndex = index;
					return true;
				}
			}

			foundClient = null;
			foundIndex = 0;
			return false;
		}

		private static void validateDisconnectedMaintainedIndex(CSteamID steamID, byte index)
		{
			if (index >= clients.Count || clients[index].playerID.steamID != steamID)
			{
				UnturnedLog.error("Clients array was modified during onServerDisconnected!");
			}
		}

		/// <summary>
		/// Notify client that they were kicked.
		/// </summary>
		private static void notifyKickedInternal(ITransportConnection transportConnection, string reason)
		{
			NetMessages.SendMessageToClient(EClientMessage.Kicked, ENetReliability.Reliable, transportConnection, (NetPakWriter writer) =>
			{
				writer.WriteString(reason);
			});
		}

		public static void kick(CSteamID steamID, string reason)
		{
#if WITH_GAME_THREAD_ASSERTIONS
			ThreadUtil.assertIsGameThread();
#endif

			SteamPlayer removeClient;
			byte removalIndex;
			if (findClientForKickBanDismiss(steamID, out removeClient, out removalIndex) == false)
				return;

			UnturnedLog.info($"Kicking player {steamID} because \"{reason}\"");
			notifyKickedInternal(removeClient.transportConnection, reason);

			broadcastServerDisconnected(steamID);
			validateDisconnectedMaintainedIndex(steamID, removalIndex);

			SteamGameServer.EndAuthSession(steamID);

			RemoveClient(removeClient);
			ReplicateRemoveClient(removeClient);
		}

		/// <summary>
		/// Notify client that they were banned.
		/// </summary>
		internal static void notifyBannedInternal(ITransportConnection transportConnection, string reason, uint duration)
		{
			NetMessages.SendMessageToClient(EClientMessage.Banned, ENetReliability.Reliable, transportConnection, (NetPakWriter writer) =>
			{
				writer.WriteString(reason);
				writer.WriteUInt32(duration);
			});
		}

		public static void ban(CSteamID steamID, string reason, uint duration)
		{
#if WITH_GAME_THREAD_ASSERTIONS
			ThreadUtil.assertIsGameThread();
#endif

			SteamPlayer removeClient;
			byte removalIndex;
			if (findClientForKickBanDismiss(steamID, out removeClient, out removalIndex) == false)
				return;

			UnturnedLog.info($"Banning player {steamID} for {TimeSpan.FromSeconds(duration)} because \"{reason}\"");
			notifyBannedInternal(removeClient.transportConnection, reason, duration);

			broadcastServerDisconnected(steamID);
			validateDisconnectedMaintainedIndex(steamID, removalIndex);

			SteamGameServer.EndAuthSession(steamID);

			RemoveClient(removeClient);
			ReplicateRemoveClient(removeClient);
		}

		/// <summary>
		/// Player left server by canceling their ticket, or we are disconnecting them without telling them.
		/// Does not send any packets to the disconnecting player.
		/// </summary>
		public static void dismiss(CSteamID steamID)
		{
#if WITH_GAME_THREAD_ASSERTIONS
			ThreadUtil.assertIsGameThread();
#endif

			SteamPlayer removeClient;
			byte removalIndex;
			if (findClientForKickBanDismiss(steamID, out removeClient, out removalIndex) == false)
			{
				// This can happen if OnServerTransportConnectionFailure already disconnected them.
				return;
			}

			broadcastServerDisconnected(steamID);
			validateDisconnectedMaintainedIndex(steamID, removalIndex);

			SteamGameServer.EndAuthSession(steamID);

			if (CommandWindow.shouldLogJoinLeave)
			{
				CommandWindow.Log(localization.format("PlayerDisconnectedText", steamID, removeClient.playerID.playerName, removeClient.playerID.characterName));
			}
			else
			{
				UnturnedLog.info(localization.format("PlayerDisconnectedText", steamID, removeClient.playerID.playerName, removeClient.playerID.characterName));
			}

			RemoveClient(removeClient);
			ReplicateRemoveClient(removeClient);
		}

		/// <summary>
		/// Number of transport connection failures on this frame.
		/// </summary>
		private int clientsKickedForTransportConnectionFailureCount;

		/// <summary>
		/// Callback when a pending player or existing player unexpectedly loses connection at the transport level.
		/// </summary>
		private static void OnServerTransportConnectionFailure(ITransportConnection transportConnection, string debugString, bool isError)
		{
			SteamPending pendingPlayer = findPendingPlayer(transportConnection);
			if (pendingPlayer != null)
			{
				if (isError)
				{
					++steam.clientsKickedForTransportConnectionFailureCount;
					UnturnedLog.info($"Removing player in queue {transportConnection} due to transport failure ({debugString}) queue state: \"{pendingPlayer.GetQueueStateDebugString()}\"");
				}
				else
				{
					UnturnedLog.info($"Removing player in queue {transportConnection} because they disconnected ({debugString}) queue state: \"{pendingPlayer.GetQueueStateDebugString()}\"");
				}
				reject(transportConnection, ESteamRejection.LATE_PENDING);
				return;
			}

			SteamPlayer client = findPlayer(transportConnection);
			if (client != null)
			{
				if (isError)
				{
					++steam.clientsKickedForTransportConnectionFailureCount;
					UnturnedLog.info($"Removing player {transportConnection} due to transport failure ({debugString})");
				}
				else
				{
					UnturnedLog.info($"Removing player {transportConnection} because they disconnected ({debugString})");
				}
				dismiss(client.playerID.steamID);
			}
		}

		internal static bool verifyTicket(CSteamID steamID, byte[] ticket)
		{
			EBeginAuthSessionResult result = SteamGameServer.BeginAuthSession(ticket, ticket.Length, steamID);
			return result == EBeginAuthSessionResult.k_EBeginAuthSessionResultOK;
		}

		private static void openGameServer()
		{
			if (isServer || isClient)
			{
				UnturnedLog.error("Failed to open game server: session already in progress.");

				return;
			}

			SDG.Provider.Services.Multiplayer.ESecurityMode authenticationMode = SDG.Provider.Services.Multiplayer.ESecurityMode.LAN;

			switch (Dedicator.serverVisibility)
			{
				case ESteamServerVisibility.Internet:
					if (configData.Server.VAC_Secure)
					{
						authenticationMode = SDG.Provider.Services.Multiplayer.ESecurityMode.SECURE;
					}
					else
					{
						authenticationMode = SDG.Provider.Services.Multiplayer.ESecurityMode.INSECURE;
					}
					break;
				case ESteamServerVisibility.LAN:
					authenticationMode = SDG.Provider.Services.Multiplayer.ESecurityMode.LAN;
					break;
			}

			if (authenticationMode == SDG.Provider.Services.Multiplayer.ESecurityMode.INSECURE)
			{
				CommandWindow.LogWarning(localization.format("InsecureWarningText"));
			}

			isVacActive = authenticationMode == SDG.Provider.Services.Multiplayer.ESecurityMode.SECURE;

#if UNITY_STANDALONE_LINUX
			try
			{
				string executablePath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
				if(executablePath.EndsWith(".x86"))
				{
					CommandWindow.LogWarning("Consider switching to the 64-bit Linux build: https://github.com/SmartlyDressedGames/Unturned-3.x-Community/issues/697");
				}
			}
			catch
			{ }
#endif // UNITY_STANDALONE_LINUX

#if WITH_THIRDPARTYAC
			if (IsServerThirdpartyAntiCheatEnabled && authenticationMode == SDG.Provider.Services.Multiplayer.ESecurityMode.SECURE)
			{
				if (!InitThirdpartyAntiCheatServer())
				{
					// Explanation is logged by initialize method.
					QuitGame("anti-cheat server init failed");
					return;
				}

				isThirdpartyAntiCheatActive = true;
			}
			else
			{
				isThirdpartyAntiCheatActive = false;
			}
			hasSetIsThirdpartyAntiCheatActive = true;
#endif

			// Non-LAN servers need to wait for a callback when connected to the Steam backend.
			bool waitingForSteamServers = !Dedicator.offlineOnly;
			if (waitingForSteamServers)
			{
				provider.multiplayerService.serverMultiplayerService.ready += handleServerReady;
			}

			try
			{
				provider.multiplayerService.serverMultiplayerService.open(ip, port, authenticationMode);
			}
			catch (Exception exception)
			{
				QuitGame($"server init failed ({exception.Message})");
				return;
			}

			serverTransport = NetTransportFactory.CreateServerTransport();
			UnturnedLog.info("Initializing {0}", serverTransport.GetType().Name);
			serverTransport.Initialize(OnServerTransportConnectionFailure);

			// SteamGameServerUtils isn't available until server is initialized.
			backendRealtimeSeconds = SteamGameServerUtils.GetServerRealTime();
			authorityHoliday = _modeConfigData.Gameplay.Allow_Holidays ? HolidayUtil.GetScheduledHoliday() : ENPCHoliday.NONE;

			if (waitingForSteamServers)
			{
				CommandWindow.Log("Waiting for Steam servers...");
			}
			else
			{
				// Otherwise called by handleServerReady callback.
				initializeDedicatedUGC();
			}
		}

		private static void closeGameServer()
		{
			if (!isServer)
			{
				UnturnedLog.error("Failed to close game server: no session in progress.");
				return;
			}

			broadcastServerShutdown();

			_isServer = false;

			//for(int index = 0; index < clients.Count; index++)
			//{
			//			 UnturnedLog.info("done playing with you!");
			//	SteamGameServer.EndAuthSession(clients[index].playerID.steamID);
			//}

			provider.multiplayerService.serverMultiplayerService.close();
		}

#endregion

		#region GAMECLIENT

		private static uint STEAM_FAVORITE_FLAG_FAVORITE = 0x01;
		internal static uint STEAM_FAVORITE_FLAG_HISTORY = 0x02;

		private class CachedFavorite
		{
			public uint ip;
			public ushort queryPort;
			public bool isFavorited;

			public bool matchesServer(uint ip, ushort queryPort)
			{
				return this.ip == ip && this.queryPort == queryPort;
			}
		}

		private static List<CachedFavorite> cachedFavorites = new List<CachedFavorite>();

		/// <summary>
		/// Check whether a server is one of our favorites or not.
		/// </summary>
		public static bool GetServerIsFavorited(uint ip, ushort queryPort)
		{
			foreach (CachedFavorite cachedFavorite in cachedFavorites)
			{
				if (cachedFavorite.matchesServer(ip, queryPort))
				{
					return cachedFavorite.isFavorited;
				}
			}

			//UnturnedLog.info("Is server favorited? {0}:{1}", Parser.getIPFromUInt32(ip), port);
			//UnturnedLog.info("Num favorites: {0}", SteamMatchmaking.GetFavoriteGameCount());
			for (int index = 0; index < SteamMatchmaking.GetFavoriteGameCount(); index++)
			{
				AppId_t favoriteAppID;
				uint favoriteIP;
				ushort favoriteConnectionPort;
				ushort favoriteQueryPort;
				uint favoriteFlags;
				uint favoriteTime;
				SteamMatchmaking.GetFavoriteGame(index, out favoriteAppID, out favoriteIP, out favoriteConnectionPort, out favoriteQueryPort, out favoriteFlags, out favoriteTime);

				bool isFavorited = (favoriteFlags | STEAM_FAVORITE_FLAG_FAVORITE) == favoriteFlags;
				//bool isInHistory = (favoriteFlags | STEAM_FAVORITE_FLAG_HISTORY) == favoriteFlags;

				if (isFavorited)
				{
					// As of 2018-05-16 it seems that Steam returns the query port for both favoriteGamePort and favoriteQueryPort.
					if (favoriteIP == ip && favoriteQueryPort == queryPort)
					{
						return true;
					}
				}
			}

			return false;
		}

		/// <summary>
		/// Set whether a server is one of our favorites or not.
		/// </summary>
		public static void SetServerIsFavorited(uint ip, ushort connectionPort, ushort queryPort, bool newFavorited)
		{
			bool foundCachedFavorite = false;
			foreach (CachedFavorite cachedFavorite in cachedFavorites)
			{
				if (cachedFavorite.matchesServer(ip, queryPort))
				{
					cachedFavorite.isFavorited = newFavorited;
					foundCachedFavorite = true;
					break;
				}
			}

			if (!foundCachedFavorite)
			{
				CachedFavorite cachedFavorite = new CachedFavorite();
				cachedFavorite.ip = ip;
				cachedFavorite.queryPort = port;
				cachedFavorite.isFavorited = newFavorited;
				cachedFavorites.Add(cachedFavorite);
			}

			if (newFavorited)
			{
				SteamMatchmaking.AddFavoriteGame(APP_ID, ip, connectionPort, queryPort, STEAM_FAVORITE_FLAG_FAVORITE, SteamUtils.GetServerRealTime());
				UnturnedLog.info($"Added favorite server IP: {new IPv4Address(ip)} Connection Port: {connectionPort} Query Port: {queryPort}");
			}
			else
			{
				SteamMatchmaking.RemoveFavoriteGame(APP_ID, ip, connectionPort, queryPort, STEAM_FAVORITE_FLAG_FAVORITE);
				UnturnedLog.info($"Removed favorite server IP: {new IPv4Address(ip)} Connection Port: {connectionPort} Query Port: {queryPort}");
			}
		}

		[System.Obsolete("Please use WebUtils.OpenURL instead.")]
		public static void openURL(string url)
		{ }

		/// <summary>
		/// Steam's favorites list requires that we know the server's IPv4 address and port,
		/// so we can't favorite when joining by Steam ID.
		/// </summary>
		public static bool CanFavoriteCurrentServer
		{
			get
			{
				if (isServer || clientTransport == null)
				{
					return false;
				}
				else
				{
					bool hasConnectionDetails = clientTransport.TryGetIPv4Address(out IPv4Address address);
					hasConnectionDetails &= clientTransport.TryGetConnectionPort(out ushort connectionPort);
					hasConnectionDetails &= clientTransport.TryGetQueryPort(out ushort queryPort);
					hasConnectionDetails &= !SteamNetworkingUtils.IsFakeIPv4(address.value);
					return hasConnectionDetails;
				}
			}
		}

		public static bool CanBookmarkCurrentServer
		{
			get
			{
				if (isServer || currentServerWorkshopResponse == null)
				{
					return false;
				}
				else
				{
					return currentServerWorkshopResponse.server.BPersistentGameServerAccount();
				}
			}
		}

		public static bool isCurrentServerFavorited
		{
			get
			{
				if (isServer || clientTransport == null)
				{
					return false;
				}
				else
				{
					clientTransport.TryGetIPv4Address(out IPv4Address address);
					clientTransport.TryGetQueryPort(out ushort queryPort);
					return GetServerIsFavorited(address.value, queryPort);
				}
			}
		}

		public static bool IsCurrentServerBookmarked
		{
			get
			{
#if DEDICATED_SERVER
				return false;
#else
				if (isServer || currentServerWorkshopResponse == null)
				{
					return false;
				}
				else
				{
					return ServerBookmarksManager.FindBookmarkDetails(currentServerWorkshopResponse.server) != null;
				}
#endif // !DEDICATED_SERVER
			}
		}

		/// <summary>
		/// Toggle whether we've favorited the server we're currently playing on.
		/// </summary>
		public static void toggleCurrentServerFavorited()
		{
			if (isServer || clientTransport == null)
				return;

			bool hasConnectionDetails = clientTransport.TryGetIPv4Address(out IPv4Address address);
			hasConnectionDetails &= clientTransport.TryGetConnectionPort(out ushort connectionPort);
			hasConnectionDetails &= clientTransport.TryGetQueryPort(out ushort queryPort);

			if (hasConnectionDetails)
			{
				bool newValue = !GetServerIsFavorited(address.value, queryPort);
				SetServerIsFavorited(address.value, connectionPort, queryPort, newValue);
			}
			else
			{
				UnturnedLog.info("Unable to toggle server favorite because connection details are unavailable");
			}
		}

		/// <summary>
		/// Toggle whether we've bookmarked the server we're currently playing on.
		/// </summary>
		public static void ToggleCurrentServerBookmarked()
		{
#if !DEDICATED_SERVER
			if (isServer || currentServerWorkshopResponse == null)
				return;

			if (IsCurrentServerBookmarked)
			{
				ServerBookmarksManager.RemoveBookmark(currentServerWorkshopResponse.server);
			}
			else
			{
				clientTransport.TryGetIPv4Address(out IPv4Address address);
				clientTransport.TryGetQueryPort(out ushort queryPort);
				if (SteamNetworkingUtils.IsFakeIPv4(address.value))
				{
					// Port is randomized on startup when using Fake IP.
					queryPort = 0;
				}

				ServerBookmarksManager.AddBookmark(currentServerWorkshopResponse.server,
						currentServerWorkshopResponse.bookmarkHost, queryPort, currentServerWorkshopResponse.serverName,
						currentServerWorkshopResponse.serverDescription, currentServerWorkshopResponse.thumbnailUrl);
			}
#endif // !DEDICATED_SERVER
		}

		public delegate void ClientConnected();
		public delegate void ClientDisconnected();

		public static ClientConnected onClientConnected;
		public static ClientDisconnected onClientDisconnected;

		public delegate void EnemyConnected(SteamPlayer player);
		public delegate void EnemyDisconnected(SteamPlayer player);

		public static EnemyConnected onEnemyConnected;
		public static EnemyDisconnected onEnemyDisconnected;

		private static void broadcastEnemyConnected(SteamPlayer player)
		{
			try
			{
				onEnemyConnected?.Invoke(player);
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Exception during onEnemyConnected:");
				UnturnedLog.exception(e);
			}
		}

		private static void broadcastEnemyDisconnected(SteamPlayer player)
		{
			try
			{
				onEnemyDisconnected?.Invoke(player);
			}
			catch (Exception e)
			{
				UnturnedLog.warn("Exception during onEnemyDisconnected:");
				UnturnedLog.exception(e);
			}
		}

#pragma warning disable
		private static Callback<PersonaStateChange_t> personaStateChange;
#pragma warning restore
		private static void onPersonaStateChange(PersonaStateChange_t callback)
		{
			UnityEngine.Profiling.Profiler.BeginSample("onPersonaStateChange");
			if (callback.m_nChangeFlags == EPersonaChange.k_EPersonaChangeName && callback.m_ulSteamID == client.m_SteamID)
			{
				_clientName = SteamFriends.GetPersonaName();
			}
			UnityEngine.Profiling.Profiler.EndSample();
		}

#pragma warning disable
		private static Callback<GetTicketForWebApiResponse_t> getTicketForWebApiResponseCallback;
#pragma warning restore
		private static void OnGetTicketForWebApiResponse(GetTicketForWebApiResponse_t callback)
		{
			string identity;
			if (!pluginTicketHandles.TryGetValue(callback.m_hAuthTicket, out identity))
			{
				UnturnedLog.info($"Received Steam auth ticket for web API for handle {callback.m_hAuthTicket}, but no linked identity (Result: {callback.m_eResult})");
				SteamUser.CancelAuthTicket(callback.m_hAuthTicket);
				return;
			}

			if (callback.m_eResult != EResult.k_EResultOK)
			{
				UnturnedLog.warn($"Error getting Steam auth ticket for web API identity \"{identity}\": {callback.m_eResult}");
				pluginTicketHandles.Remove(callback.m_hAuthTicket);
				SteamUser.CancelAuthTicket(callback.m_hAuthTicket);
				return;
			}

			UnturnedLog.info($"Received Steam web API ticket response for identity \"{identity}\" (length: {callback.m_cubTicket})");
			SteamPlayer.SendGetSteamAuthTicketForWebApiResponse.Invoke(ENetReliability.Reliable,
				SendGetSteamAuthTicketForWebApiResponse_Write, identity, callback.m_rgubTicket, callback.m_cubTicket);
		}

		private static void SendGetSteamAuthTicketForWebApiResponse_Write(NetPakWriter writer, string identity, byte[] ticketData, int ticketLength)
		{
			writer.WriteString(identity, lengthBitCount: 5);
			writer.WriteUInt16((ushort) ticketLength);
			writer.WriteBytes(ticketData, ticketLength);
		}

#pragma warning disable
		private static Callback<GameServerChangeRequested_t> gameServerChangeRequested;
#pragma warning restore
		private static void onGameServerChangeRequested(GameServerChangeRequested_t callback)
		{
			if (isConnected)
			{
				return;
			}

			// This callback is invoked in two cases:
			// 1. The game is already running when a steam://connect/ip:port link is clicked.
			// 2. When clicking connect in the legacy Steam server browser. Unfortunately this passes the connection port
			//    rather than the query port. As a hack we set game port to query port in GameServer.Init to fix.

			UnturnedLog.info("onGameServerChangeRequested {0} {1}", callback.m_rgchServer, callback.m_rgchPassword);
			UnityEngine.Profiling.Profiler.BeginSample("onGameServerChangeRequested");
			SteamConnectionInfo info = new SteamConnectionInfo(callback.m_rgchServer, callback.m_rgchPassword);
			UnturnedLog.info("External connect IP: {0} Port: {1} Password: '{2}'", Parser.getIPFromUInt32(info.ip), info.port, info.password);

			MenuPlayConnectUI.connect(info, false, MenuPlayServerInfoUI.EServerInfoOpenContext.CONNECT);
			UnityEngine.Profiling.Profiler.EndSample();
		}

#pragma warning disable
		private static Callback<GameRichPresenceJoinRequested_t> gameRichPresenceJoinRequested;
#pragma warning restore
		private static void onGameRichPresenceJoinRequested(GameRichPresenceJoinRequested_t callback)
		{
			if (isConnected)
			{
				return;
			}

			UnturnedLog.info("onGameRichPresenceJoinRequested {0}", callback.m_rgchConnect);
			UnityEngine.Profiling.Profiler.BeginSample("onGameRichPresenceJoinRequested");
			uint connectIP;
			ushort connectQueryPort;
			string connectPassword;
			CSteamID connectServerCode;
			if (CommandLine.TryGetSteamConnect(callback.m_rgchConnect, out connectIP, out connectQueryPort, out connectPassword, out connectServerCode))
			{
				if (connectServerCode.IsValid())
				{
					UnturnedLog.info("Rich presence connect code: {0} Password: '{1}'", connectServerCode, connectPassword);
					if (connectServerCode.BGameServerAccount())
					{
						ServerConnectParameters connectParameters = new ServerConnectParameters(connectServerCode, connectPassword);
						connect(connectParameters, null, null);
					}
					else
					{
						UnturnedLog.warn($"Rich presence connect non-gameserver code ({connectServerCode.GetEAccountType()})");
					}
				}
				else
				{
					SteamConnectionInfo info = new SteamConnectionInfo(connectIP, connectQueryPort, connectPassword);
					UnturnedLog.info("Rich presence connect IP: {0} Port: {1} Password: '{2}'", Parser.getIPFromUInt32(info.ip), info.port, info.password);

					MenuPlayConnectUI.connect(info, false, MenuPlayServerInfoUI.EServerInfoOpenContext.CONNECT);
				}
			}
			UnityEngine.Profiling.Profiler.EndSample();
		}

#pragma warning disable
		private static Callback<NewUrlLaunchParameters_t> newUrlLaunchParametersCallback;
#pragma warning restore
		private static void OnNewUrlLaunchParametersPosted(NewUrlLaunchParameters_t callback)
		{
			if (isConnected)
			{
				return;
			}

			// Note: see also SteamLaunchArguments.Init.
			const int bufferSize = 2048;
			int resultLength = SteamApps.GetLaunchCommandLine(out string result, bufferSize);
			if (resultLength > 0 && !string.IsNullOrEmpty(result))
			{
				UnturnedLog.info($"OnNewUrlLaunchParametersPosted: \"{result}\"");

				uint connectIP;
				ushort connectQueryPort;
				string connectPassword;
				CSteamID connectServerCode;
				if (CommandLine.TryGetSteamConnect(result, out connectIP, out connectQueryPort, out connectPassword, out connectServerCode))
				{
					if (connectServerCode.IsValid())
					{
						UnturnedLog.info("URL connect code: {0} Password: '{1}'", connectServerCode, connectPassword);
						if (connectServerCode.BGameServerAccount())
						{
							ServerConnectParameters connectParameters = new ServerConnectParameters(connectServerCode, connectPassword);
							connect(connectParameters, null, null);
						}
						else
						{
							UnturnedLog.warn($"URL connect non-gameserver code ({connectServerCode.GetEAccountType()})");
						}
					}
					else
					{
						SteamConnectionInfo info = new SteamConnectionInfo(connectIP, connectQueryPort, connectPassword);
						UnturnedLog.info("URL connect IP: {0} Port: {1} Password: '{2}'", Parser.getIPFromUInt32(info.ip), info.port, info.password);

						MenuPlayConnectUI.connect(info, false, MenuPlayServerInfoUI.EServerInfoOpenContext.CONNECT);
					}
				}
			}
			else
			{
				UnturnedLog.info("OnNewUrlLaunchParametersPosted: empty");
			}
		}

		private static HAuthTicket ticketHandle = HAuthTicket.Invalid;
		private static Dictionary<HAuthTicket, string> pluginTicketHandles = new Dictionary<HAuthTicket, string>();
		//private static float lastPacketTick;
		private static float lastPingRequestTime;
		private static float lastQueueNotificationTime;

		public static float timeLastPacketWasReceivedFromServer
		{
			get;
			internal set;
		}

		internal static float timeLastPingRequestWasSentToServer;

		public static readonly float EPSILON = 0.01f;
		//public static readonly float MARGIN = 0.3f;
		public static readonly float UPDATE_TIME = 0.08f;
		public static readonly float UPDATE_DELAY = 0.1f;
		public static readonly float UPDATE_DISTANCE = 0.01f;
		public static readonly uint UPDATES = 1;
		public static readonly float LERP = 3f;
		internal const float INTERP_SPEED = 10.0f;

		private static float[] pings;
		private static float _ping;
		public static float ping => _ping;

		/// <summary>
		/// Ping from client to server, measured in milliseconds.
		/// </summary>
		public static int ClientPingMs
		{
			get
			{
				if (clientTransport != null && clientTransport.TryGetPing(out int transportPingMs))
				{
					return transportPingMs;
				}
				else
				{
					return Mathf.Max(0, Mathf.FloorToInt(_ping * 1000.0f));
				}
			}
		}

		internal static void lag(float value)
		{
			value = Mathf.Clamp01(value);
			_ping = value;

			for (int index = pings.Length - 1; index > 0; index--)
			{
				pings[index] = pings[index - 1];

				if (pings[index] > 0.001f)
				{
					_ping += pings[index];
				}
			}

			_ping /= pings.Length;
			pings[0] = value;
		}

		internal static byte[] openTicket(SteamNetworkingIdentity serverIdentity)
		{
			if (ticketHandle != HAuthTicket.Invalid)
			{
				return null;
			}

			uint size;
			byte[] temp = new byte[1024];

			string identityString;
			serverIdentity.ToString(out identityString);
			UnturnedLog.info($"Calling GetAuthSessionTicket with identity {identityString}");
			ticketHandle = SteamUser.GetAuthSessionTicket(temp, temp.Length, out size, ref serverIdentity);

			if (size == 0)
			{
				UnturnedLog.info("GetAuthSessionTicket returned size zero");
				return null;
			}

			UnturnedLog.info($"GetAuthSessionTicket ticket handle is valid: {ticketHandle != HAuthTicket.Invalid} (size: {size})");

			byte[] ticket = new byte[size];
			Buffer.BlockCopy(temp, 0, ticket, 0, (int) size);

			return ticket;
		}

		internal static void RequestSteamAuthTicketForWebApi(string identity)
		{
			foreach (KeyValuePair<HAuthTicket, string> pair in pluginTicketHandles)
			{
				if (string.Equals(pair.Value, identity))
				{
					UnturnedLog.error($"Ignoring duplicate Steam web API ticket request for identity \"{identity}\"");
					return;
				}
			}

			HAuthTicket pluginTicketHandle = SteamUser.GetAuthTicketForWebApi(identity);
			if (pluginTicketHandle != HAuthTicket.Invalid)
			{
				pluginTicketHandles.Add(pluginTicketHandle, identity);
				UnturnedLog.info($"Added handle {pluginTicketHandle} for Steam web API ticket request for identity \"{identity}\"");
			}
			else
			{
				UnturnedLog.error($"GetAuthTicketForWebApi for identity \"{identity}\" returned invalid handle");
			}
		}

		private static void CancelAllSteamAuthTickets()
		{
			if (ticketHandle != HAuthTicket.Invalid)
			{

				SteamUser.CancelAuthTicket(ticketHandle);
				ticketHandle = HAuthTicket.Invalid;
				UnturnedLog.info("Cancelled main Steam auth ticket");
			}

			foreach (KeyValuePair<HAuthTicket, string> pair in pluginTicketHandles)
			{
				SteamUser.CancelAuthTicket(pair.Key);
				UnturnedLog.info($"Cancelled Steam web API auth ticket for identity \"{pair.Value}\"");
			}
			pluginTicketHandles.Clear();
		}

#endregion

		#region INSTANCE

		private static Provider steam;
		public static IProvider provider
		{
			get;
			protected set;
		}

		private static bool _isInitialized;
		public static bool isInitialized => _isInitialized;

		private static uint timeOffset;
		private static uint _time;
		public static uint time
		{
			get => _time + (uint) (Time.realtimeSinceStartup - timeOffset);

			set
			{
				_time = value;
				timeOffset = (uint) Time.realtimeSinceStartup;
			}
		}

		private static uint initialBackendRealtimeSeconds;
		private static float initialLocalRealtime;
		/// <summary>
		/// Number of seconds since January 1st, 1970 GMT as reported by backend servers.
		/// </summary>
		public static uint backendRealtimeSeconds
		{
			get => initialBackendRealtimeSeconds + (uint) (Time.realtimeSinceStartup - initialLocalRealtime);

			private set
			{
				initialBackendRealtimeSeconds = value;
				initialLocalRealtime = Time.realtimeSinceStartup;

				onBackendRealtimeAvailable?.Invoke();
			}
		}

		/// <summary>
		/// Current UTC as reported by backend servers.
		/// Used by holiday events to keep timing somewhat synced between players. 
		/// </summary>
		private static DateTime unixEpochDateTime = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc); // UTC == GMT
		public static DateTime backendRealtimeDate => unixEpochDateTime.AddSeconds(backendRealtimeSeconds);

		/// <summary>
		/// Has the initial backend realtime been queried yet?
		/// Not available immediately on servers because SteamGameServerUtils cannot be used until the actual Steam instance is available.
		/// </summary>
		public static bool isBackendRealtimeAvailable => initialBackendRealtimeSeconds > 0;

		public delegate void BackendRealtimeAvailableHandler();

		/// <summary>
		/// Invoked after backend realtime becomes available.
		/// </summary>
		public static BackendRealtimeAvailableHandler onBackendRealtimeAvailable;

		private IEnumerator QuitAfterDelay(float seconds) // don't remove this
		{
			yield return new WaitForSeconds(seconds);

#if UNITY_EDITOR
			if (Application.isEditor)
			{
				UnityEditor.EditorApplication.isPlaying = false;
				yield break;
			}
#endif // UNITY_EDITOR

			// 2023-05-24: hack manually call quit callback and unbind because it doesn't
			// seem to be called when WindowsConsole.RegisterCtrlHandler routine is invoked.
			Application.quitting -= onApplicationQuitting;
			onApplicationQuitting();
			QuitGame("server shutdown");
		}

		private static Steamworks.SteamAPIWarningMessageHook_t apiWarningMessageHook;
		private static void onAPIWarningMessage(int severity, System.Text.StringBuilder warning)
		{
			CommandWindow.LogWarning("Steam API Warning Message:");
			CommandWindow.LogWarning("Severity: " + severity);
			CommandWindow.LogWarning("Warning: " + warning);
		}

		private static int debugUpdates;
		public static int debugUPS;
		private static float debugLastUpdate;
		private static int debugTicks;
		public static int debugTPS;
		private static float debugLastTick;

		private void updateDebug()
		{
			debugUpdates++;

			if (Time.realtimeSinceStartup - debugLastUpdate > 1)
			{
				debugUPS = (int) (debugUpdates / (Time.realtimeSinceStartup - debugLastUpdate));

				debugLastUpdate = Time.realtimeSinceStartup;
				debugUpdates = 0;
			}
		}

		private void tickDebug()
		{
			debugTicks++;

			if (Time.realtimeSinceStartup - debugLastTick > 1)
			{
				debugTPS = (int) (debugTicks / (Time.realtimeSinceStartup - debugLastTick));

				debugLastTick = Time.realtimeSinceStartup;
				debugTicks = 0;
			}
		}

		private static Dictionary<string, Texture2D> downloadedIconCache = new Dictionary<string, Texture2D>();
		private static Dictionary<string, PendingIconRequest> pendingCachableIconRequests = new Dictionary<string, PendingIconRequest>();
		public delegate void IconQueryCallback(Texture2D icon, bool responsibleForDestroy);

		public struct IconQueryParams
		{
			public string url;
			public IconQueryCallback callback;
			public bool shouldCache;

			public IconQueryParams(string url, IconQueryCallback callback, bool shouldCache = true)
			{
				this.url = url;
				this.callback = callback;
				this.shouldCache = shouldCache;
			}
		}

		private class PendingIconRequest
		{
			public string url;
			public IconQueryCallback callback;
			public bool shouldCache;
		}

		internal static CommandLineFlag allowWebRequests = new CommandLineFlag(true, "-NoWebRequests");

		private IEnumerator downloadIcon(PendingIconRequest iconQueryParams)
		{
			const bool nonReadableOnCPU = true;
			using (UnityWebRequest request = UnityWebRequestTexture.GetTexture(iconQueryParams.url, nonReadableOnCPU))
			{
				request.timeout = 15;

				yield return request.SendWebRequest();

				Texture2D resultTexture = null;
				bool responsibleForDestroy = false;
				if (request.result != UnityWebRequest.Result.Success)
				{
					UnturnedLog.warn($"{request.result} downloading \"{iconQueryParams.url}\" for icon query: \"{request.error}\"");
				}
				else
				{
					Texture2D downloadedTexture = DownloadHandlerTexture.GetContent(request);
					downloadedTexture.hideFlags = HideFlags.HideAndDontSave;
					downloadedTexture.filterMode = FilterMode.Trilinear;

					if (iconQueryParams.shouldCache)
					{
						if (downloadedIconCache.TryGetValue(iconQueryParams.url, out resultTexture))
						{
							// Another request at the same time downloaded the icon first!
							// Destroy ours and use theirs instead.
							Destroy(downloadedTexture);
						}
						else
						{
							downloadedIconCache.Add(iconQueryParams.url, downloadedTexture);
							resultTexture = downloadedTexture;
						}
						responsibleForDestroy = false;
					}
					else
					{
						resultTexture = downloadedTexture;
						responsibleForDestroy = true;
					}
				}

				if (iconQueryParams.callback == null)
				{
					if (responsibleForDestroy && resultTexture != null)
					{
						Destroy(resultTexture);
					}
				}
				else
				{
					try
					{
						iconQueryParams.callback(resultTexture, responsibleForDestroy);
					}
					catch (System.Exception exception)
					{
						UnturnedLog.exception(exception, "Caught exception during texture downloaded callback:");
					}
				}

				if (iconQueryParams.shouldCache)
				{
					pendingCachableIconRequests.Remove(iconQueryParams.url);
				}
			}
		}

		public static void destroyCachedIcon(string url)
		{
			Texture2D cachedTexture;
			if (downloadedIconCache.TryGetValue(url, out cachedTexture))
			{
				Destroy(cachedTexture);
				downloadedIconCache.Remove(url);
			}
		}

		public static void refreshIcon(IconQueryParams iconQueryParams)
		{
			if (iconQueryParams.callback == null)
				return;

			if (string.IsNullOrEmpty(iconQueryParams.url) || !allowWebRequests)
			{
				iconQueryParams.callback(null, /*responsibleForDestroy*/ false);
				return;
			}

			// Trim so that accidental whitespace doesn't miss cached icon.
			// 2023-01-25: I was tempted to make url lowercase as well for the same reason, however
			// while scheme/host are case-insensitive the path/query IS case sensitive.
			iconQueryParams.url = iconQueryParams.url.Trim();
			if (string.IsNullOrEmpty(iconQueryParams.url))
			{
				iconQueryParams.callback(null, /*responsibleForDestroy*/ false);
				return;
			}

			if (iconQueryParams.shouldCache)
			{
				Texture2D cachedTexture;
				if (downloadedIconCache.TryGetValue(iconQueryParams.url, out cachedTexture))
				{
					iconQueryParams.callback(cachedTexture, /*responsibleForDestroy*/ false);
					return;
				}

				PendingIconRequest pendingRequest;
				if (pendingCachableIconRequests.TryGetValue(iconQueryParams.url, out pendingRequest))
				{
					// Add our callback to existing request.
					pendingRequest.callback += iconQueryParams.callback;
					return;
				}
			}

			PendingIconRequest request = new PendingIconRequest();
			request.url = iconQueryParams.url;
			request.callback = iconQueryParams.callback;
			request.shouldCache = iconQueryParams.shouldCache;

			if (iconQueryParams.shouldCache)
			{
				pendingCachableIconRequests.Add(iconQueryParams.url, request);
			}

			steam.StartCoroutine(steam.downloadIcon(request));
		}

		private void Update()
		{
			if (!isInitialized)
			{
				return;
			}

			// Warning intended to help associate unusual server events (e.g. failed to close pending connection)
			// with blocked game thread.
			// 2021-02-23: changed to info rather than warn to prevent CI from failing.
			// 2022-10-25: removed the wait between logs because it made it hard to tell if server was occasionally
			//				hitching or just constantly running at low framerate.
			if (Time.unscaledDeltaTime > 1.5f)
			{
				UnturnedLog.info("Long delay between Updates: {0}s", Time.unscaledDeltaTime);
			}

#if WITH_THIRDPARTYAC
			RunThirdpartyAntiCheatFrame();
#endif

			if (isConnected)
			{
				listen();
			}

			updateDebug();

			UnityEngine.Profiling.Profiler.BeginSample("Provider Update");
			provider.update();
			UnityEngine.Profiling.Profiler.EndSample();

			if (countShutdownTimer > 0)
			{
				if (Time.realtimeSinceStartup - lastTimerMessage > 1.0f)
				{
					lastTimerMessage = Time.realtimeSinceStartup;
					countShutdownTimer--;

					if (countShutdownTimer == 300 || countShutdownTimer == 60 || countShutdownTimer == 30 || countShutdownTimer == 15 || countShutdownTimer == 3 || countShutdownTimer == 2 || countShutdownTimer == 1)
					{
						ChatManager.say(localization.format("Shutdown", countShutdownTimer), ChatManager.welcomeColor);
					}
				}
			}
			else if (countShutdownTimer == 0)
			{
				UnturnedLog.info("Server shutdown timer reached zero");
				didServerShutdownTimerReachZero = true;
				countShutdownTimer = -1;

				broadcastCommenceShutdown();

				bool shouldDelayQuit = _clients.Count > 0;
				if (_clients.Count > 0)
				{
					NetMessages.SendMessageToClients(EClientMessage.Shutdown, ENetReliability.Reliable, GatherRemoteClientConnections(), (NetPakWriter writer) =>
					{
						writer.WriteString(shutdownMessage);
					});
				}

				foreach (SteamPlayer client in _clients)
				{
					SteamGameServer.EndAuthSession(client.playerID.steamID);
				}

				// Wait for reliable shutdown messages to be delivered.
				float quitDelaySeconds = shouldDelayQuit ? 1.0f : 0.0f;
				if (shouldDelayQuit)
				{
					UnturnedLog.info($"Delaying server quit by {quitDelaySeconds}s to ensure shutdown message reaches clients");
				}
				StartCoroutine(QuitAfterDelay(quitDelaySeconds));
			}
		}

		private void FixedUpdate()
		{
			if (!isInitialized)
			{
				return;
			}

			tickDebug();
		}

		/// <summary>
		/// In here because we want to call this very early in startup after initializing provider,
		/// but with plenty of time to hopefully install maps prior to reaching the main menu.
		/// </summary>
		public static void initAutoSubscribeMaps()
		{
			if (statusData == null || statusData.Maps == null)
				return;

			foreach (AutoSubscribeMap map in statusData.Maps.Auto_Subscribe)
			{
				if (LocalNews.hasAutoSubscribedToWorkshopItem(map.Workshop_File_Id))
				{
					// Already did the auto-sub, and maybe player chose to unsubscribe.
					continue;
				}

				DateTimeRange range = new DateTimeRange(map.Start, map.End);
				if (range.isNowWithinRange() == false)
					continue;

				// Mark auto-sub first in case subscribing throws an exception somehow.
				LocalNews.markAutoSubscribedToWorkshopItem(map.Workshop_File_Id);

				provider.workshopService.setSubscribed(map.Workshop_File_Id, true);
			}

			if (statusData.Maps.Auto_Unsubscribe != null)
			{
				IConvenientSavedata cs = ConvenientSavedata.get();
				foreach (ulong workshopFileId in statusData.Maps.Auto_Unsubscribe)
				{
					string flag = $"Auto_Unsubscribed_Workshop_Item_{workshopFileId}";
					if (cs.hasFlag(flag))
					{
						continue;
					}

					cs.setFlag(flag);
					provider.workshopService.setSubscribed(workshopFileId, false);
				}
			}

			// Force a save because some players were crashing during startup and could not unsubscribe without being
			// re-subscribed because we had not saved yet.
			ConvenientSavedata.SaveIfDirty();
		}

		/// <summary>
		/// This file is of particular importance to the dedicated server because otherwise Steam networking sockets
		/// will say the certificate is for the wrong app. When launching the game outside Steam this sets the app.
		/// </summary>
		private void WriteSteamAppIdFileAndEnvironmentVariables()
		{
			// 2022-01-18 feeling paranoid about this because there have been reports of an extra zero appended to the file.
			string appIdString = APP_ID.m_AppId.ToString(System.Globalization.CultureInfo.InvariantCulture);
			UnturnedLog.info($"Unturned overriding Steam AppId with \"{appIdString}\"");

			try
			{
				Environment.SetEnvironmentVariable("SteamOverlayGameId", appIdString, EnvironmentVariableTarget.Process);
				Environment.SetEnvironmentVariable("SteamGameId", appIdString, EnvironmentVariableTarget.Process);
				Environment.SetEnvironmentVariable("SteamAppId", appIdString, EnvironmentVariableTarget.Process);
			}
			catch (Exception ex)
			{
				UnturnedLog.exception(ex, "Caught exception writing Steam environment variables:");
			}

			DirectoryInfo dir;
#if UNITY_EDITOR
			dir = UnityPaths.ProjectDirectory;
#else // !UNITY_EDITOR
			dir = UnityPaths.GameDirectory;
#endif // !UNITY_EDITOR
			string path = PathEx.Join(dir, "steam_appid.txt");
			try
			{
				// Share write access because multiple processes can be opened at once, and all have the same appid.
				using (FileStream fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite))
				using (StreamWriter writer = new StreamWriter(fs, System.Text.ASCIIEncoding.ASCII))
				{
					writer.Write(appIdString);
				}
			}
			catch (Exception ex)
			{
				UnturnedLog.exception(ex, "Caught exception writing steam_appid.txt file:");
			}
		}

		/// <summary>
		/// Hackily exposed as an easy way for editor code to check the verison number.
		/// </summary>
		public static StatusData LoadStatusData()
		{
			if (ReadWrite.fileExists("/Status.json", false, true))
			{
				try
				{
					return ReadWrite.deserializeJSON<StatusData>("/Status.json", false, true);
				}
				catch (Exception e)
				{
					UnturnedLog.exception(e, "Unable to parse Status.json! consider validating with a JSON linter");
				}
			}

			return null;
		}

		private ModInfo LoadModInfo()
		{
			if (ReadWrite.fileExists("/ModInfo.json", false, true))
			{
				try
				{
					return ReadWrite.deserializeJSON<ModInfo>("/ModInfo.json", false, true);
				}
				catch (Exception e)
				{
					UnturnedLog.exception(e, "Unable to parse ModInfo.json! consider validating with a JSON linter");
				}
			}

			return null;
		}

#if !DEDICATED_SERVER
		private void LoadPreferences()
		{
			string preferencesFilePath = PathEx.Join(UnturnedPaths.RootDirectory, "Preferences.json");
			if (ReadWrite.fileExists(preferencesFilePath, false, false))
			{
				try
				{
					_preferenceData = ReadWrite.deserializeJSON<PreferenceData>(preferencesFilePath, false, false);
				}
				catch (Exception exception)
				{
					UnturnedLog.exception(exception, "Unable to parse Preferences.json! consider validating with a JSON linter");
					_preferenceData = null;
				}

				if (preferenceData == null)
				{
					_preferenceData = new PreferenceData();
				}
			}
			else
			{
				_preferenceData = new PreferenceData();
			}
			_preferenceData.Viewmodel.Clamp();
			SleekCustomization.defaultTextContrast = _preferenceData.Graphics.Default_Text_Contrast;
			SleekCustomization.inconspicuousTextContrast = _preferenceData.Graphics.Inconspicuous_Text_Contrast;
			SleekCustomization.colorfulTextContrast = _preferenceData.Graphics.Colorful_Text_Contrast;

			// Catch exception because if IO fails (e.g. if user marked file read-only) we do not want to break startup. 
			try
			{
				ReadWrite.serializeJSON(preferencesFilePath, false, false, preferenceData);
			}
			catch (System.Exception exception)
			{
				UnturnedLog.exception(exception, "Caught exception re-serializing Preferences.json:");
			}
		}
#endif // !DEDICATED_SERVER

		public void awake()
		{
			_statusData = LoadStatusData();
			if (statusData == null)
			{
				_statusData = new StatusData();
			}
			_modInfo = LoadModInfo();
			HolidayUtil.scheduleHolidays(statusData.Holidays);

			APP_VERSION = statusData.Game.FormatApplicationVersion();
			APP_VERSION_PACKED = Parser.getUInt32FromIP(APP_VERSION);

			if (isInitialized)
			{
				Destroy(gameObject);
				return;
			}

			_isInitialized = true;
			DontDestroyOnLoad(gameObject);

			steam = this;
			Level.onLevelLoaded += onLevelLoaded;

			Application.quitting += onApplicationQuitting;
			Application.wantsToQuit += onApplicationWantsToQuit;

			if (Dedicator.IsDedicatedServer)
			{
				try
				{
					WriteSteamAppIdFileAndEnvironmentVariables();
					provider = new SDG.SteamworksProvider.SteamworksProvider(new SDG.SteamworksProvider.SteamworksAppInfo(APP_ID.m_AppId, APP_NAME, APP_VERSION, true));
					provider.intialize();
				}
				catch (Exception exception)
				{
					UnturnedLog.exception(exception, "Steam init exception:");
					QuitGame("Steam init exception");
					return;
				}

				string newLanguage;
				if (!CommandLine.tryGetLanguage(out newLanguage, out _path))
				{
					_path = ReadWrite.PATH + "/Localization/";
					newLanguage = "English";
				}
				language = newLanguage;
				localizationRoot = path + language;

				localization = Localization.read("/Server/ServerConsole.dat");

				//gsPolicyResponse = Callback<GSPolicyResponse_t>.CreateGameServer(onGSPolicyResponse);
				p2pSessionConnectFail = Callback<P2PSessionConnectFail_t>.CreateGameServer(onP2PSessionConnectFail);
				validateAuthTicketResponse = Callback<ValidateAuthTicketResponse_t>.CreateGameServer(onValidateAuthTicketResponse);
				clientGroupStatus = Callback<GSClientGroupStatus_t>.CreateGameServer(onClientGroupStatus);

				_isPro = true;

				CommandWindow.Log($"Game version: {APP_VERSION} Engine version: {Application.unityVersion}");
				if (_modInfo != null)
				{
					CommandWindow.Log($"Mod name: \"{_modInfo.Name}\" Mod version: {_modInfo.FormatModVersion()}");
				}

				maxPlayers = 8;
				queueSize = 8;
#if EXPERIMENTAL
				serverName = "Unturned Experimental";
#else
				serverName = "Unturned";
#endif
				serverPassword = "";

				ip = 0;
				port = 27015;

				map = "PEI";

#if UNITY_EDITOR
				string autoLoadLevel = EditorPrefs.GetString("AutoLoadLevel");
				if (!string.IsNullOrEmpty(autoLoadLevel))
				{
					map = autoLoadLevel;
				}
#endif // UNITY_EDITOR

				isPvP = true;
				isWhitelisted = false;
				hideAdmins = false;
				hasCheats = false;
				filterName = false;
				mode = EGameMode.NORMAL;
				isGold = false;
				gameMode = null;
				cameraMode = ECameraMode.FIRST;

				Commander.init();

				SteamWhitelist.load();
				SteamBlacklist.load();
				SteamAdminlist.load();

				string[] commands = CommandLine.getCommands();
				UnturnedLog.info($"Executing {commands.Length} potential game command(s) from the command-line:");
				for (int index = 0; index < commands.Length; index++)
				{
					if (!Commander.execute(CSteamID.Nil, commands[index]))
					{
						UnturnedLog.info($"Did not match \"{commands[index]}\" with any commands");
					}
				}

				if (ServerSavedata.fileExists("/Server/Commands.dat"))
				{
					FileStream stream = null;
					StreamReader reader = null;

					try
					{
						stream = new FileStream(ReadWrite.PATH + "/Servers/" + serverID + "/Server/Commands.dat", FileMode.Open, FileAccess.Read, FileShare.Read);
						reader = new StreamReader(stream);

						string command;
						while ((command = reader.ReadLine()) != null)
						{
							if (string.IsNullOrWhiteSpace(command))
								continue;

							if (command.StartsWith("//"))
								continue;

							bool success = Commander.execute(CSteamID.Nil, command);
							if (!success)
							{
								UnturnedLog.error("Unknown entry in Commands.dat: '{0}'", command);
							}
						}
					}
					finally
					{
						if (stream != null)
						{
							stream.Close();
						}

						if (reader != null)
						{
							reader.Close();
						}
					}
				}
				else
				{
					Data data = new Data();

					ServerSavedata.writeData("/Server/Commands.dat", data);
				}

				if (!ServerSavedata.folderExists("/Bundles"))
				{
					ServerSavedata.createFolder("/Bundles");
				}

				if (!ServerSavedata.folderExists("/Maps"))
				{
					ServerSavedata.createFolder("/Maps");
				}

				if (!ServerSavedata.folderExists("/Workshop/Content"))
				{
					ServerSavedata.createFolder("/Workshop/Content");
				}

				if (!ServerSavedata.folderExists("/Workshop/Maps"))
				{
					ServerSavedata.createFolder("/Workshop/Maps");
				}

				_configData = ConfigData.CreateDefault(false);
				_modeConfigDataOverrides.Clear();
				LoadGameplayConfig(false);
				_modeConfigData = _configData.getModeConfig(mode);
				if (_modeConfigData == null)
				{
					_modeConfigData = new ModeConfigData(mode);
				}

				ServerMessageHandler_ReadyToConnect.joinRateLimiter.window = configData.Server.Join_Rate_Limit_Window_Seconds;
				badMessageRateLimiter.window = configData.Server.Bad_Packet_Rate_Limit_Window_Seconds;
				badMessageRateLimiter.threshold = configData.Server.Bad_Packet_Rate_Limit_Threshold;

				if (!Dedicator.offlineOnly)
				{
					SDG.HostBans.HostBansManager.Get().Refresh();
				}

#if !UNITY_EDITOR
				LogSystemInfo();
#endif // !UNITY_EDITOR

				return;
			}

			try
			{
				WriteSteamAppIdFileAndEnvironmentVariables();
				provider = new SDG.SteamworksProvider.SteamworksProvider(new SDG.SteamworksProvider.SteamworksAppInfo(APP_ID.m_AppId, APP_NAME, APP_VERSION, false));
				provider.intialize();
			}
			catch (Exception exception)
			{
				UnturnedLog.exception(exception, "Steam init exception:");
				QuitGame("Steam init exception");
#if UNITY_EDITOR
				EditorUtility.DisplayDialog("Error Initializing Steam", "Please ensure you have Steam running! Play mode will now exit.", "OK");
				EditorApplication.ExitPlaymode();
#endif
				return;
			}

#if UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST
			if (steamAppInstallDirectory == null)
			{
#if UNITY_EDITOR
				UnturnedLog.error("Exiting play mode because Steam app install is needed for core game assets");
				EditorUtility.DisplayDialog("Error Finding Game", "Please ensure you have Unturned installed on Steam! Play mode will now exit.", "OK");
				EditorApplication.ExitPlaymode();
#else // UNITY_EDITOR
				QuitGame("Missing Steam app install");
#endif // UNITY_EDITOR
				return;
			}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST

#if !DEDICATED_SERVER
			SteamLaunchArguments.Init();
#endif

			backendRealtimeSeconds = SteamUtils.GetServerRealTime();
			authorityHoliday = HolidayUtil.GetScheduledHoliday();

			apiWarningMessageHook = new Steamworks.SteamAPIWarningMessageHook_t(onAPIWarningMessage);
			SteamUtils.SetWarningMessageHook(apiWarningMessageHook);

			screenshotRequestedCallback = Callback<ScreenshotRequested_t>.Create(OnSteamScreenshotRequested);
			SteamScreenshots.HookScreenshots(true);

			time = SteamUtils.GetServerRealTime();

			personaStateChange = Callback<PersonaStateChange_t>.Create(onPersonaStateChange);
			getTicketForWebApiResponseCallback = Callback<GetTicketForWebApiResponse_t>.Create(OnGetTicketForWebApiResponse);
			gameServerChangeRequested = Callback<GameServerChangeRequested_t>.Create(onGameServerChangeRequested);
			gameRichPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(onGameRichPresenceJoinRequested);
			newUrlLaunchParametersCallback = Callback<NewUrlLaunchParameters_t>.Create(OnNewUrlLaunchParametersPosted);

			_user = SteamUser.GetSteamID();
			_client = user;
			_clientHash = Hash.SHA1(client);
			_clientName = SteamFriends.GetPersonaName();

			if (clResetSteamStatsAndAchievements)
			{
				if (SteamUserStats.ResetAllStats(true))
				{
					UnturnedLog.info("All Steam stats and achievements have been reset");
				}
				else
				{
					UnturnedLog.error("Request to reset all Steam stats and achievements failed");
				}
			}
			else if (clUnlockSteamAchievements)
			{
				uint achievementCount = SteamUserStats.GetNumAchievements();
				UnturnedLog.info($"Unlocking {achievementCount} Steam achievements:");
				for (uint achievementIndex = 0; achievementIndex < achievementCount; ++achievementIndex)
				{
					string achievementName = SteamUserStats.GetAchievementName(achievementIndex);
					if (SteamUserStats.SetAchievement(achievementName))
					{
						UnturnedLog.info($"{achievementIndex + 1} of {achievementCount}: \"{achievementName}\" Unlocked");
					}
					else
					{
						UnturnedLog.error($"{achievementIndex + 1} of {achievementCount}: \"{achievementName}\" Failed to unlock");
					}
				}
				SteamUserStats.StoreStats();
			}

			provider.statisticsService.userStatisticsService.requestStatistics();
			provider.statisticsService.globalStatisticsService.requestStatistics();

			provider.workshopService.refreshUGC();
			provider.workshopService.refreshPublished();

			if (shouldCheckForGoldUpgrade)
			{
				_isPro = SteamApps.BIsSubscribedApp(PRO_ID);
			}

			UnturnedLog.info($"Game version: {APP_VERSION} Engine version: {Application.unityVersion}");
			if (_modInfo != null)
			{
				UnturnedLog.info($"Mod name: \"{_modInfo.Name}\" Mod version: {_modInfo.FormatModVersion()}");
			}

			isLoadingInventory = true;

			provider.economyService.GrantPromoItems();

			// Notify Steam that we might be using SteamNetworkingSockets.
			Steamworks.SteamNetworkingSockets.InitAuthentication();

#if !DEDICATED_SERVER
			// 2022-04-21: HostBans assembly cannot currently reference CommandLineFlag, so disabling web request is handled here.
			if (SteamUser.BLoggedOn() && allowWebRequests)
			{
				SDG.HostBans.HostBansManager.Get().Refresh();
			}

			LiveConfig.Refresh();
#endif // !DEDICATED_SERVER

			if (allowWebRequests)
			{
				ServerListCuration.Get().StartupLoadWebUrlsAndLiveConfig();
			}

#if !DISABLESTEAMWORKS
			ProfanityFilter.InitSteam();
#endif // !DISABLESTEAMWORKS

			string clLanguage;
			if (CommandLine.tryGetLanguage(out clLanguage, out _path))
			{
				language = clLanguage;
				localizationRoot = path + language;
			}
			else
			{
				string local = SteamUtils.GetSteamUILanguage();
#if UNITY_EDITOR
				string languageOverride = EditorPrefs.GetString("LanguageOverride");
				if (!string.IsNullOrEmpty(languageOverride))
				{
					local = languageOverride;
				}
#endif // UNITY_EDITOR
				language = local.Substring(0, 1).ToUpper() + local.Substring(1, local.Length - 1).ToLower();

				bool foundSteamLang = false;
				foreach (SteamContent workshopItem in provider.workshopService.ugc)
				{
					if (workshopItem.type == ESteamUGCType.LOCALIZATION)
					{
						if (ReadWrite.folderExists(workshopItem.path + "/" + language, false))
						{
							_path = workshopItem.path + "/";
							localizationRoot = path + language;
							foundSteamLang = true;
							UnturnedLog.info("Found Steam language '{0}' in workshop item {1}", local, workshopItem.publishedFileID);
							break;
						}
					}
				}

				if (!foundSteamLang)
				{
					if (ReadWrite.folderExists("/Localization/" + language))
					{
						_path = ReadWrite.PATH + "/Localization/";
						localizationRoot = path + language;
						foundSteamLang = true;
						UnturnedLog.info("Found Steam language '{0}' in root Localization directory", local);
					}
				}

#if UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST
				if (!foundSteamLang && steamAppInstallDirectory != null)
				{
					string steamPath = PathEx.Join(steamAppInstallDirectory, "Localization");
					string rootPath = Path.Join(steamPath, language);
					if (Directory.Exists(rootPath))
					{
						_path = steamPath;
						localizationRoot = rootPath;
						foundSteamLang = true;
						UnturnedLog.info("Found Steam language '{0}' in app install Localization directory", local);
					}
				}
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST

				if (!foundSteamLang)
				{
					if (ReadWrite.folderExists("/Sandbox/" + language))
					{
						_path = ReadWrite.PATH + "/Sandbox/";
						localizationRoot = path + language;
						foundSteamLang = true;
						UnturnedLog.info("Found Steam language '{0}' in Sandbox directory", local);
					}
				}

				if (!foundSteamLang)
				{
					// Item is not structured how we expect, but we load it so that we are more compatible.
					foreach (SteamContent workshopItem in provider.workshopService.ugc)
					{
						bool containsEditor = ReadWrite.folderExists(workshopItem.path + "/Editor", false);
						bool containsMenu = ReadWrite.folderExists(workshopItem.path + "/Menu", false);
						bool containsPlayer = ReadWrite.folderExists(workshopItem.path + "/Player", false);
						bool containsServer = ReadWrite.folderExists(workshopItem.path + "/Server", false);
						bool containsShared = ReadWrite.folderExists(workshopItem.path + "/Shared", false);

						// If it contains all these root-level localization folders, it's probably a root-level localization folder.
						if (containsEditor && containsMenu && containsPlayer && containsServer && containsShared)
						{
							_path = null;
							localizationRoot = workshopItem.path;
							foundSteamLang = true;
							UnturnedLog.info("Found language files for unknown language in workshop item {0}", workshopItem.publishedFileID);
						}
					}
				}

				// Nelson 2025-08-28: previously, this reverted back to English, but that meant
				// per-mod language files were ignored. Nowadays the game loads the English
				// file as a fallback anyway so we can keep the user language here.
				if (!foundSteamLang)
				{
					_path = ReadWrite.PATH + "/Localization/";
					localizationRoot = path + language;
					UnturnedLog.info($"No installed translation for Steam language ({local})");
				}
			}

			provider.economyService.loadTranslationEconInfo();

			localization = Localization.read("/Server/ServerConsole.dat");

			updateRichPresence();

			_configData = ConfigData.CreateDefault(true);
			_modeConfigDataOverrides.Clear();
			_modeConfigData = configData.getModeConfig(EGameMode.NORMAL);

#if !DEDICATED_SERVER
			LoadPreferences();
#endif // !DEDICATED_SERVER

			if (ReadWrite.fileExists("/StreamerNames.json", false, true))
			{
				try
				{
					streamerNames = ReadWrite.deserializeJSON<List<string>>("/StreamerNames.json", false, true);
				}
				catch (Exception e)
				{
					UnturnedLog.exception(e, "Unable to parse StreamerNames.json! consider validating with a JSON linter");
					streamerNames = null;
				}

				if (streamerNames == null)
				{
					streamerNames = new List<string>();
				}
			}
			else
			{
				streamerNames = new List<string>();
			}

#if !UNITY_EDITOR
			LogSystemInfo();
#endif // !UNITY_EDITOR
		}

		public void start()
		{
#if DEVELOPMENT_BUILD
			if(ContinuousIntegration.isRunning)
			{
				CommandWindow.Log("Running CI");
			}
#endif // DEVELOPMENT_BUILD
		}

#if !UNITY_EDITOR
		private void LogSystemInfo()
		{
			try
			{
				UnturnedLog.info("Platform: {0}", Application.platform);
				UnturnedLog.info("Operating System: " + SystemInfo.operatingSystem); // Operating system name with version.
				UnturnedLog.info("System Memory: " + SystemInfo.systemMemorySize + "MB"); // This is the approximate amount of system memory in megabytes.
#if !DEDICATED_SERVER
				UnturnedLog.info("Graphics Device Name: " + SystemInfo.graphicsDeviceName); // This is the name of user's graphics card, as reported by the graphics driver.
				UnturnedLog.info("Graphics Device Type: " + SystemInfo.graphicsDeviceType); // The graphics API type used by the graphics device.
				UnturnedLog.info("Graphics Memory: " + SystemInfo.graphicsMemorySize + "MB"); // This is the approximate amount of graphics memory in megabytes.
				UnturnedLog.info("Graphics Multi-Threaded: " + SystemInfo.graphicsMultiThreaded); // Is graphics device using multi-threaded rendering?
				UnturnedLog.info("Render Threading Mode: " + SystemInfo.renderingThreadingMode); // Application's actual rendering threading mode.
				UnturnedLog.info("Supports Audio: " + SystemInfo.supportsAudio); // Is there an Audio device available for playback?
				UnturnedLog.info("Supports Instancing: " + SystemInfo.supportsInstancing); // Is GPU draw call instancing supported?
				UnturnedLog.info("Supports Motion Vectors: " + SystemInfo.supportsMotionVectors); // Whether motion vectors are supported on this platform.
				UnturnedLog.info("Supports Ray Tracing: " + SystemInfo.supportsRayTracing); // Checks if ray tracing is supported by the current configuration.
#endif // !DEDICATED_SERVER
			}
			catch (System.Exception exception)
			{
				// I doubt these would throw an exception, but I do not want to risk breaking startup.
				UnturnedLog.exception(exception, "Caught exception while logging system info:");
			}
		}
#endif // !UNITY_EDITOR

		/// <summary>
		/// Has the onApplicationQuitting callback been invoked?
		/// </summary>
		public static bool isApplicationQuitting
		{
			get;
			private set;
		}

		/// <summary>
		/// Moved from OnApplicationQuit when that was deprecated.
		/// </summary>
		private void onApplicationQuitting()
		{
			UnturnedLog.info("Application quitting");
			isApplicationQuitting = true;

			if (!Dedicator.IsDedicatedServer)
			{
				ConvenientSavedata.save();
#if !DEDICATED_SERVER
				MenuSettings.save();
				LocalPlayerBlocklist.SaveIfDirty();
				ServerBookmarksManager.SaveIfDirty();
#endif // !DEDICATED_SERVER
			}

			if (!isInitialized)
			{
				return;
			}

			RequestDisconnect("application quitting");

			provider.shutdown();

			UnturnedLog.info("Finished quitting");
		}

		public static void QuitGame(string reason)
		{
			UnturnedLog.info($"Quit game: {reason}");
			wasQuitGameCalled = true;
			Application.Quit();
		}

#if UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST
		/// <summary>
		/// Useful to load files from Steam install of the game while running in the editor.
		/// </summary>
		internal static DirectoryInfo steamAppInstallDirectory;
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD || !WITH_NOREDIST

		private static bool wasQuitGameCalled;
		public static bool WasQuitGameCalled
		{
			get => wasQuitGameCalled;
		}

		/// <summary>
		/// Moved from OnApplicationQuit when Application.CancelQuit was deprecated.
		/// </summary>
		private bool onApplicationWantsToQuit()
		{
			if (wasQuitGameCalled)
			{
				// Allow quit if we instigated it. There was a bug with the pause menu quit button due to this
				// protection against pressing Alt-F4.
				return true;
			}

			if (Dedicator.IsDedicatedServer)
			{
				// In future we should prevent quitting without saving.
				return true;
			}
			else
			{
#if UNITY_EDITOR
				// Editor has no quit restrictions.
				return true;
#else
				if(!isServer && isPvP && clients.Count > 1 && Player.LocalPlayer != null && !Player.LocalPlayer.movement.isSafe && Player.LocalPlayer.life.IsAlive)
				{
					// Client cannot quit.
					return false;
				}
				else
				{
					return true;
				}
#endif // UNITY_EDITOR
			}
		}
		#endregion

		/// <summary>
		/// A couple of players have reported the PRO_DESYNC kick because their client thinks they own the gold upgrade,
		/// but the Steam backend thinks otherwise. This option is a bit of a hack to work around the problem for them.
		/// </summary>
		private static CommandLineFlag shouldCheckForGoldUpgrade = new CommandLineFlag(true, "-NoGoldUpgrade");

		/// <summary>
		/// If specified, all Steam achievements and stats progress is lost.
		/// </summary>
		private static CommandLineFlag clResetSteamStatsAndAchievements = new CommandLineFlag(false, "-ResetSteamStatsAndAchievements");

		/// <summary>
		/// If specified, all Steam achievements are unlocked during startup.
		/// </summary>
		private static CommandLineFlag clUnlockSteamAchievements = new CommandLineFlag(false, "-UnlockSteamAchievements");

		[System.Obsolete("Removed", true)]
		public static void resetConfig()
		{ }
	}
}
