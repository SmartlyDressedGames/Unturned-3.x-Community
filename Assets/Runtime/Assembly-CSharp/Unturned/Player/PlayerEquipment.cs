////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
#if UNITY_EDITOR || DEVELOPMENT_BUILD
// #define LOG_EQUIPMENT_ATTACK_INPUTS
// #define LOG_EQUIPMENT_START_STOP
#endif // UNITY_EDITOR || DEVELOPMENT_BUILD
using SDG.NetTransport;
using SDG.Provider;
using Steamworks;
using System.Collections.Generic;
using UnityEngine;
using Unturned.SystemEx;
using Unturned.UnityEx;

namespace SDG.Unturned
{
	public delegate void HotkeysUpdated();

	public delegate void PlayerEquipRequestHandler(PlayerEquipment equipment, ItemJar jar, ItemAsset asset, ref bool shouldAllow);
	public delegate void PlayerDequipRequestHandler(PlayerEquipment equipment, ref bool shouldAllow);

	public class HotkeyInfo
	{
		/// <summary>
		/// Which item ID we thought was there. If the item ID currently at the coordinates doesn't match we clear this hotkey.
		/// </summary>
		public ushort id;

		public byte page;
		public byte x;
		public byte y;

		public HotkeyInfo()
		{
			id = 0;
			page = 255;
			x = 255;
			y = 255;
		}
	}

	/// <summary>
	/// Start/Stop input is encoded as 2 bits, 1 bit for Start flag and 1 bit for Stop flag.
	/// 
	/// Prior to 2023-03-16 it was a single bit. The server would Start if true and the previous frame was false,
	/// and vice versa call Stop if false and the previous frame was true. The problem with that approach was when
	/// the client FPS is higher than the simulation FPS a series of repeated attack presses would be treated as a
	/// continuous held attack input. Semi-auto guns were difficult to shoot at their max rate of fire. Sending both
	/// allows the server to theoretically call Start every simulation frame as opposed to only half.
	///
	/// First approach was to OR Start if held, otherwise OR Stop. This doesn't work because for example when Aim is
	/// pressed the Stop flag will already be enabled, so the gun Starts aiming, Stops aiming, Starts aiming, and then
	/// stays aiming rather than just Start and stay aiming. Instead we only want Stop to be sent once.
	/// </summary>
	[System.Flags]
	public enum EAttackInputFlags
	{
		None = 0,

		/// <summary>
		/// Wants to "start" primary or secondary input. (e.g., Useable.startPrimary)
		/// </summary>
		Start = 1,

		/// <summary>
		/// Wants to "stop" primary or secondary input. (e.g., Useable.stopPrimary)
		/// </summary>
		Stop = 2,
	}

	public class PlayerEquipment : PlayerCaller
	{
		public static readonly byte SAVEDATA_VERSION = 1;

		private static readonly float DAMAGE_BARRICADE = 2;
		private static readonly float DAMAGE_STRUCTURE = 2;
		private static readonly float DAMAGE_VEHICLE = 0;//5;
		private static readonly float DAMAGE_RESOURCE = 20;
		private static readonly float DAMAGE_OBJECT = 5;

		private static readonly PlayerDamageMultiplier DAMAGE_PLAYER_MULTIPLIER = new PlayerDamageMultiplier(15, 0.6f, 0.6f, 0.8f, 1.1f);
		private static readonly ZombieDamageMultiplier DAMAGE_ZOMBIE_MULTIPLIER = new ZombieDamageMultiplier(15, 0.3f, 0.3f, 0.6f, 1.1f);
		private static readonly AnimalDamageMultiplier DAMAGE_ANIMAL_MULTIPLIER = new AnimalDamageMultiplier(15, 0.3f, 0.6f, 1.1f);

		public PlayerEquipRequestHandler onEquipRequested;
		public PlayerDequipRequestHandler onDequipRequested;

		/// <summary>
		/// Invoked from tellEquip after change.
		/// </summary>
		public static event System.Action<PlayerEquipment> OnUseableChanged_Global;
		public static event System.Action<PlayerEquipment> OnInspectingUseable_Global;

		public ushort itemID => asset?.id ?? 0;

		private ERagdollEffect skinRagdollEffect;
		/// <summary>
		/// Skin applied to the currently equipped useable.
		/// </summary>
		private SkinAsset useableSkin;

		private byte[] _state;
		public byte[] state
		{
			set => _state = value;
			get => _state;
		}

		private byte _quality;
		public byte quality
		{
			get
			{
				if (isTurret)
				{
					// Nelson 2024-03-29: received a bug report where shooting a vehicle's turret was dealing 250 damage
					// rather than 500, but after shooting another gun the turret was dealing full 500 damage. This was
					// because the quality setter is ignored in a turret, so the first time it kept its default value of
					// zero until equipping another item initialized quality. It looks like the original intention was
					// for turrets to set the item quality to 100, so now I've added this special case to always report
					// the turret's quality as 100%.
					return 100;
				}

				return _quality;
			}

			set
			{
				if (isTurret)
				{
					return;
				}

				_quality = value;
			}
		}

		private Transform[] thirdSlots;
		private bool[] thirdSkinneds;
		private List<Mesh>[] tempThirdMeshes;
		private Material[] tempThirdMaterials;
		private MythicalEffectController[] thirdMythics;
		private Transform[] characterSlots;
		private bool[] characterSkinneds;
		private List<Mesh>[] tempCharacterMeshes;
		private Material[] tempCharacterMaterials;
		private MythicalEffectController[] characterMythics;

		private Transform _firstModel;
		public Transform firstModel => _firstModel;

		private bool firstSkinned;
		private List<Mesh> tempFirstMesh;
		private Material tempFirstMaterial;
		private MythicalEffectController firstMythic;

		private Transform _thirdModel;
		public Transform thirdModel => _thirdModel;

		private bool thirdSkinned;
		private List<Mesh> tempThirdMesh;
		private Material tempThirdMaterial;
		private MythicalEffectController thirdMythic;

		private Transform _characterModel;
		public Transform characterModel => _characterModel;

		private bool characterSkinned;
		private List<Mesh> tempCharacterMesh;
		private Material tempCharacterMaterial;
		private MythicalEffectController characterMythic;

		private ItemAsset _asset;
		public ItemAsset asset => _asset;

		private Useable _useable;
		public Useable useable => _useable;

		private UseableEventHook firstEventComponent;
		private UseableEventHook thirdEventComponent;
		private UseableEventHook characterEventComponent;

		private Transform _thirdPrimaryMeleeSlot;
		public Transform thirdPrimaryMeleeSlot => _thirdPrimaryMeleeSlot;

		private Transform _thirdPrimaryLargeGunSlot;
		public Transform thirdPrimaryLargeGunSlot => _thirdPrimaryLargeGunSlot;

		private Transform _thirdPrimarySmallGunSlot;
		public Transform thirdPrimarySmallGunSlot => _thirdPrimarySmallGunSlot;

		private Transform _thirdSecondaryMeleeSlot;
		public Transform thirdSecondaryMeleeSlot => _thirdSecondaryMeleeSlot;

		private Transform _thirdSecondaryGunSlot;
		public Transform thirdSecondaryGunSlot => _thirdSecondaryGunSlot;

		private Transform _characterPrimaryMeleeSlot;
		public Transform characterPrimaryMeleeSlot => _characterPrimaryMeleeSlot;

		private Transform _characterPrimaryLargeGunSlot;
		public Transform characterPrimaryLargeGunSlot => _characterPrimaryLargeGunSlot;

		private Transform _characterPrimarySmallGunSlot;
		public Transform characterPrimarySmallGunSlot => _characterPrimarySmallGunSlot;

		private Transform _characterSecondaryMeleeSlot;
		public Transform characterSecondaryMeleeSlot => _characterSecondaryMeleeSlot;

		private Transform _characterSecondaryGunSlot;
		public Transform characterSecondaryGunSlot => _characterSecondaryGunSlot;

		private Transform _firstSpine;
		private Transform _firstSpineHook;
		private Transform _firstLeftHook;
		public Transform firstLeftHook => _firstLeftHook;
		private Transform _firstRightHook;
		public Transform firstRightHook => _firstRightHook;

		private Transform _thirdSpine;
		private Transform _thirdSpineHook;
		private Transform _thirdLeftHook;
		public Transform thirdLeftHook => _thirdLeftHook;
		private Transform _thirdRightHook;
		public Transform thirdRightHook => _thirdRightHook;

		private Transform _characterSpine;
		private Transform _characterSpineHook;
		private Transform _characterLeftHook;
		public Transform characterLeftHook => _characterLeftHook;
		private Transform _characterRightHook;
		public Transform characterRightHook => _characterRightHook;

		private HotkeyInfo[] _hotkeys;
		public HotkeyInfo[] hotkeys => _hotkeys;

		public HotkeysUpdated onHotkeysUpdated;

		public bool isItemHotkeyed(byte page, byte index, ItemJar jar, out byte button)
		{
			if (page < PlayerInventory.SLOTS)
			{
				button = page;
				return true;
			}

			for (byte hotkeyIndex = 0; hotkeyIndex < hotkeys.Length; hotkeyIndex++)
			{
				HotkeyInfo info = hotkeys[hotkeyIndex];

				if (info.page == page && info.x == jar.x && info.y == jar.y && info.id == jar.item.id)
				{
					button = (byte) (hotkeyIndex + 2);
					return true;
				}
			}

			button = 0;
			return false;
		}

		public bool wasTryingToSelect;

		public bool HasValidUseable => useable != null;

		public bool IsEquipAnimationFinished
		{
			get
			{
				if (channel.IsLocalPlayer || Provider.isServer)
				{
					return player.input.simulation - equipAnimStartedFrame >= equipAnimLengthFrames;
				}
				else
				{
					return Time.timeAsDouble >= equipAnimCompletedTime;
				}
			}
		}

		public bool isTurret
		{
			get;
			private set;
		}

		/// <summary>
		/// Does equipped useable have a menu open?
		/// If so pause menu, dashboard, and other menus cannot be opened.
		/// </summary>
		public bool isUseableShowingMenu => useable != null && useable.isUseableShowingMenu;

		public bool isBusy;
		public bool canEquip;

		private byte slot = 255;

		internal bool arePrimaryAndSecondaryInputsReversedByHallucination;

		private byte _equippedPage;
		public byte equippedPage => _equippedPage;

		private byte _equipped_x;
		public byte equipped_x => _equipped_x;

		private byte _equipped_y;
		public byte equipped_y => _equipped_y;

		private bool wasUsablePrimaryStarted;
		private bool wasUsableSecondaryStarted;

		/// <summary>
		/// For aiming toggle input.
		/// </summary>
		private bool localWantsToAim;

		[System.Obsolete]
		public bool primary => false;

		[System.Obsolete]
		public bool secondary => false;

		private bool hasVision;

		public float lastPunching
		{
			get;
			private set;
		}

		private double equipAnimCompletedTime;
		private uint equipAnimStartedFrame;
		private uint equipAnimLengthFrames;
		private float lastEquip;
		private uint lastPunch;
		private static float lastInspect;
		private static float inspectTime;

		private bool localWasPrimaryHeldLastFrame;
		private bool localWasPrimaryPressedBetweenSimulationFrames;
		private bool localWasPrimaryReleasedBetweenSimulationFrames;
		private bool localWasSecondaryHeldLastFrame;
		private bool localWasSecondaryPressedBetweenSimulationFrames;
		private bool localWasSecondaryReleasedBetweenSimulationFrames;

		public bool isInspecting => Time.realtimeSinceStartup - lastInspect < inspectTime;

		public bool canInspect => HasValidUseable && IsEquipAnimationFinished && !isBusy && player.animator.checkExists("Inspect") && !isInspecting && useable.canInspect;

		/// <summary>
		/// Get ragdoll effect to use when the current weapon deals damage.
		/// </summary>
		public ERagdollEffect getUseableRagdollEffect()
		{
			// Player can temporarily disable ragdoll effects along with their cosmetic/skin effects for stealth.
			if (player.clothing.isMythic)
			{
				return skinRagdollEffect;
			}
			else
			{
				return ERagdollEffect.None;
			}
		}

		internal AudioReference GetUseableSpecialAudioOverride()
		{
			if (player.clothing.isMythic && useableSkin != null)
			{
				return useableSkin.specialAudioOverride;
			}
			else
			{
				return default;
			}
		}

		/// <summary>
		/// It should be safe to call this immediately because hotkeys are loaded in InitializePlayer.
		/// </summary>
		public void ServerBindItemHotkey(byte hotkeyIndex, ItemAsset expectedItem, byte page, byte x, byte y)
		{
			SendItemHotkeySuggestion.Invoke(GetNetId(), ENetReliability.Reliable, channel.GetOwnerTransportConnection(), hotkeyIndex, expectedItem?.GUID ?? System.Guid.Empty, page, x, y);
		}

		public void ServerClearItemHotkey(byte hotkeyIndex)
		{
			SendItemHotkeySuggestion.Invoke(GetNetId(), ENetReliability.Reliable, channel.GetOwnerTransportConnection(), hotkeyIndex, System.Guid.Empty, byte.MaxValue, byte.MaxValue, byte.MaxValue);
		}

		private static readonly ClientInstanceMethod<byte, System.Guid, byte, byte, byte> SendItemHotkeySuggestion = ClientInstanceMethod<byte, System.Guid, byte, byte, byte>.Get(typeof(PlayerEquipment), nameof(ReceiveItemHotkeySuggeston));
		[SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
		public void ReceiveItemHotkeySuggeston(in ClientInvocationContext context, byte hotkeyIndex, System.Guid expectedAssetGuid, byte page, byte x, byte y)
		{
			if (hotkeys == null || hotkeyIndex >= hotkeys.Length)
			{
				context.LogWarning($"Hotkey index ({hotkeyIndex}) out of bounds ({hotkeys?.Length})");
				return;
			}

			ushort expectedItemId = 0;
			if (!expectedAssetGuid.IsEmpty())
			{
				ItemAsset itemAsset = Assets.find<ItemAsset>(expectedAssetGuid);
				if (itemAsset != null)
				{
					expectedItemId = itemAsset.id;
				}
				else
				{
					UnturnedLog.warn($"Unable to use server item hotkey suggestion because asset was not found ({expectedAssetGuid})");
				}
			}

			if (expectedItemId == 0)
			{
				page = byte.MaxValue;
				x = byte.MaxValue;
				y = byte.MaxValue;
			}

			HotkeyInfo hotkeyInfo = hotkeys[hotkeyIndex];
			hotkeyInfo.id = expectedItemId;
			hotkeyInfo.page = page;
			hotkeyInfo.x = x;
			hotkeyInfo.y = y;

			ClearDuplicateHotkeys(hotkeyIndex);

			onHotkeysUpdated?.Invoke();
		}

		/// <summary>
		/// Prevent multiple hotkeys from referencing the same item.
		/// </summary>
		private void ClearDuplicateHotkeys(int newHotkeyIndex)
		{
			HotkeyInfo newHotkey = hotkeys[newHotkeyIndex];
			for (int index = 0; index < hotkeys.Length; index++)
			{
				if (index == newHotkeyIndex)
				{
					continue;
				}

				HotkeyInfo other = hotkeys[index];
				if (other.page == newHotkey.page && other.x == newHotkey.x && other.y == newHotkey.y)
				{
					other.id = 0;
					other.page = 255;
					other.x = 255;
					other.y = 255;
				}
			}
		}

		public bool getUseableStatTrackerValue(out EStatTrackerType type, out int kills)
		{
			return channel.owner.getStatTrackerValue(asset?.sharedSkinLookupID ?? itemID, out type, out kills);
		}

		protected bool getSlot0StatTrackerValue(out EStatTrackerType type, out int kills)
		{
			ItemJar jar = player.inventory.getItem(0, 0);
			if (jar != null)
			{
				return channel.owner.getStatTrackerValue(jar.GetAsset()?.sharedSkinLookupID ?? jar.item.id, out type, out kills);
			}
			else
			{
				type = EStatTrackerType.NONE;
				kills = -1;
				return false;
			}
		}

		protected bool getSlot1StatTrackerValue(out EStatTrackerType type, out int kills)
		{
			ItemJar jar = player.inventory.getItem(1, 0);
			if (jar != null)
			{
				return channel.owner.getStatTrackerValue(jar.GetAsset()?.sharedSkinLookupID ?? jar.item.id, out type, out kills);
			}
			else
			{
				type = EStatTrackerType.NONE;
				kills = -1;
				return false;
			}
		}

		/// <summary>
		/// Left-handed characters need the stat tracker to be flipped on the X axis so that the text reads properly.
		/// ItemTool doesn't know about left/right handedness, so for the moment that's handled here because only players need this fixed up.
		/// </summary>
		protected void fixStatTrackerHookScale(Transform itemModelTransform)
		{
			if (!channel.owner.IsLeftHanded)
				return; // Doesn't need to fix

			Transform statTrackerTransform = itemModelTransform.Find("Stat_Tracker");
			if (!statTrackerTransform)
				return;

			statTrackerTransform.localScale = new Vector3(-statTrackerTransform.localScale.x, statTrackerTransform.localScale.y, statTrackerTransform.localScale.z);
		}

		private void ApplyEquipableLocalScale(ItemAsset asset, Transform itemModelTransform)
		{
			if (!channel.owner.IsLeftHanded || asset.shouldLeftHandedCharactersMirrorEquippedItem)
			{
				itemModelTransform.localScale = Vector3.one;
			}
			else
			{
				// Un-mirror the model.
				itemModelTransform.localScale = new Vector3(-1.0f, 1.0f, 1.0f);
			}
		}

		/// <summary>
		/// Match stat tracker gameobject's isActive to whether skins are visible.
		/// </summary>
		protected void syncStatTrackTrackerVisibility(Transform itemModelTransform)
		{
			if (itemModelTransform == null)
				return;

			Transform statTrackerTransform = itemModelTransform.Find("Stat_Tracker");
			if (!statTrackerTransform)
				return;

			statTrackerTransform.gameObject.SetActive(player.clothing.isSkinned);
		}

		/// <summary>
		/// Match all stat tracker visibilities to whether skins are visible.
		/// </summary>
		protected void syncAllStatTrackerVisibility()
		{
			syncStatTrackTrackerVisibility(firstModel);
			syncStatTrackTrackerVisibility(thirdModel);
			syncStatTrackTrackerVisibility(characterModel);

			if (thirdSlots != null)
			{
				foreach (Transform model in thirdSlots)
				{
					syncStatTrackTrackerVisibility(model);
				}
			}

			if (characterSlots != null)
			{
				foreach (Transform model in characterSlots)
				{
					syncStatTrackTrackerVisibility(model);
				}
			}
		}

		public void inspect()
		{
			player.animator.setAnimationSpeed("Inspect", 1f);
			lastInspect = Time.realtimeSinceStartup;
			inspectTime = player.animator.GetAnimationLength("Inspect");
			player.animator.play("Inspect", false);

#if !DEDICATED_SERVER
			// Stopping existing Inspect audio is handled by PlayerAnimator.play.
			if (asset != null)
			{
				inspectAudioHandle = player.PlayAudioReference(asset.inspectAudio);
			}
#endif // !DEDICATED_SERVER

			foreach (UseableEventHook eventComponent in EnumerateEventComponents())
				eventComponent.OnInspectStarted?.TryInvoke(this);
		}

#if !DEDICATED_SERVER
		internal OneShotAudioHandle inspectAudioHandle;
		private OneShotAudioHandle equipAudioHandle;
#endif // !DEDICATED_SERVER

		internal void InvokeOnInspectingUseable()
		{
			OnInspectingUseable_Global.TryInvoke("OnInspectingUseable_Global", this);
		}

		public void uninspect()
		{
#if !DEDICATED_SERVER
			inspectAudioHandle.Stop();
#endif // !DEDICATED_SERVER

			lastInspect = 0;
			player.animator.setAnimationSpeed("Inspect", float.MaxValue);
		}

		public bool checkSelection(byte page)
		{
			return page == equippedPage;
		}

		public bool checkSelection(byte page, byte x, byte y)
		{
			return page == equippedPage && x == equipped_x && y == equipped_y;
		}

		public void applySkinVisual()
		{
			if (firstModel != null)
			{
				if (firstSkinned != player.clothing.isSkinned)
				{
					firstSkinned = player.clothing.isSkinned;

					if (tempFirstMaterial != null)
					{
						Attachments attachments = firstModel.GetComponent<Attachments>();
						if (attachments != null)
						{
							attachments.isSkinned = firstSkinned;
							attachments.applyVisual();
						}

						if (tempFirstMesh.Count > 0)
						{
							HighlighterTool.remesh(firstModel, tempFirstMesh, tempFirstMesh);
						}

						HighlighterTool.rematerialize(firstModel, tempFirstMaterial, out tempFirstMaterial);
					}
				}
			}

			if (thirdModel != null)
			{
				if (thirdSkinned != player.clothing.isSkinned)
				{
					thirdSkinned = player.clothing.isSkinned;

					if (tempThirdMaterial != null)
					{
						Attachments attachments = thirdModel.GetComponent<Attachments>();
						if (attachments != null)
						{
							attachments.isSkinned = thirdSkinned;
							attachments.applyVisual();
						}

						if (tempThirdMesh.Count > 0)
						{
							HighlighterTool.remesh(thirdModel, tempThirdMesh, tempThirdMesh);
						}

						HighlighterTool.rematerialize(thirdModel, tempThirdMaterial, out tempThirdMaterial);
					}
				}
			}

			if (characterModel != null)
			{
				if (characterSkinned != player.clothing.isSkinned)
				{
					characterSkinned = player.clothing.isSkinned;

					if (tempCharacterMaterial != null)
					{
						Attachments attachments = characterModel.GetComponent<Attachments>();
						if (attachments != null)
						{
							attachments.isSkinned = characterSkinned;
							attachments.applyVisual();
						}

						if (tempCharacterMesh.Count > 0)
						{
							HighlighterTool.remesh(characterModel, tempCharacterMesh, tempCharacterMesh);
						}

						HighlighterTool.rematerialize(characterModel, tempCharacterMaterial, out tempCharacterMaterial);
					}
				}
			}

			if (thirdSlots != null)
			{
				for (byte index = 0; index < thirdSlots.Length; index++)
				{
					if (thirdSlots[index] != null)
					{
						if (thirdSkinneds[index] != player.clothing.isSkinned)
						{
							thirdSkinneds[index] = player.clothing.isSkinned;

							if (tempThirdMaterials[index] != null)
							{
								Attachments attachments = thirdSlots[index].GetComponent<Attachments>();
								if (attachments != null)
								{
									attachments.isSkinned = thirdSkinneds[index];
									attachments.applyVisual();
								}

								if (tempThirdMeshes[index].Count > 0)
								{
									HighlighterTool.remesh(thirdSlots[index], tempThirdMeshes[index], tempThirdMeshes[index]);
								}

								HighlighterTool.rematerialize(thirdSlots[index], tempThirdMaterials[index], out tempThirdMaterials[index]);
							}
						}
					}

					if (characterSlots != null)
					{
						if (characterSlots[index] != null)
						{
							if (characterSkinneds[index] != player.clothing.isSkinned)
							{
								characterSkinneds[index] = player.clothing.isSkinned;

								if (tempCharacterMaterials[index] != null)
								{
									Attachments attachments = characterSlots[index].GetComponent<Attachments>();
									if (attachments != null)
									{
										attachments.isSkinned = characterSkinneds[index];
										attachments.applyVisual();
									}

									if (tempCharacterMeshes[index].Count > 0)
									{
										HighlighterTool.remesh(characterSlots[index], tempCharacterMeshes[index], tempCharacterMeshes[index]);
									}

									HighlighterTool.rematerialize(characterSlots[index], tempCharacterMaterials[index], out tempCharacterMaterials[index]);
								}
							}
						}
					}
				}
			}

			syncAllStatTrackerVisibility();
		}

		public void applyMythicVisual()
		{
			if (firstMythic != null)
			{
				firstMythic.IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
			}

			if (thirdMythic != null)
			{
				thirdMythic.IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
			}

			if (characterMythic != null)
			{
				characterMythic.IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
			}

			if (thirdSlots != null)
			{
				for (byte index = 0; index < thirdSlots.Length; index++)
				{
					if (thirdMythics[index] != null)
					{
						thirdMythics[index].IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
					}

					if (characterSlots != null)
					{
						if (characterMythics[index] != null)
						{
							characterMythics[index].IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
						}
					}
				}
			}
		}

		private void updateSlot(byte slot, ushort id, byte[] state)
		{
			if (Dedicator.IsDedicatedServer)
			{
				return;
			}

			if (slot >= PlayerInventory.SLOTS)
			{
				// Invalid index. Maybe packet was corrupted?
				return;
			}

			if (thirdSlots == null)
			{
				return;
			}

			if (thirdSlots[slot] != null)
			{
				Destroy(thirdSlots[slot].gameObject);

				thirdSkinneds[slot] = false;
				tempThirdMaterials[slot] = null;
				thirdMythics[slot] = null;
			}

			if (characterSlots != null)
			{
				if (characterSlots[slot] != null)
				{
					Destroy(characterSlots[slot].gameObject);

					characterSkinneds[slot] = false;
					tempCharacterMaterials[slot] = null;
					characterMythics[slot] = null;
				}
			}

			if (channel.IsLocalPlayer)
			{
				if (slot == 0)
				{
					Characters.active.primaryItem = id;
					Characters.active.primaryState = state;
				}
				else if (slot == 1)
				{
					Characters.active.secondaryItem = id;
					Characters.active.secondaryState = state;
				}
			}

			if (id == 0)
			{
				return;
			}

			ItemAsset asset = Assets.find(EAssetType.ITEM, id) as ItemAsset;

			if (asset != null)
			{
				int item = 0;
				ushort skinID = 0;
				ushort mythicID = 0;
				bool isSharedSkin = asset != null && asset.sharedSkinLookupID != asset.id;
				ushort skinLookupId = isSharedSkin ? asset.sharedSkinLookupID : asset.id;
				if (channel.owner.skinItems != null && channel.owner.itemSkins != null && channel.owner.itemSkins.TryGetValue(skinLookupId, out item))
				{
					if (!isSharedSkin || asset.SharedSkinShouldApplyVisuals)
					{
						skinID = Provider.provider.economyService.getInventorySkinID(item);
					}
					mythicID = Provider.provider.economyService.getInventoryMythicID(item);
					if (mythicID == 0)
					{
						mythicID = channel.owner.getParticleEffectForItemDef(item);
					}
				}

				SkinAsset skinAsset = Assets.find(EAssetType.SKIN, skinID) as SkinAsset;

				GetStatTrackerValueHandler statTrackerCallback = null;
				if (slot == 0)
				{
					statTrackerCallback = getSlot0StatTrackerValue;
				}
				else if (slot == 1)
				{
					statTrackerCallback = getSlot1StatTrackerValue;
				}

				Transform model = ItemTool.InstantiateItem(100, state, false, asset, skinAsset, /*shouldDestroyColliders*/ true, tempThirdMeshes[slot], out tempThirdMaterials[slot], statTrackerCallback);

				// Without rigidbody the vehicle physics get stuck driving straight...
				// Keep this for mods that disable collider removal.
				Rigidbody rb = model.GetOrAddComponent<Rigidbody>();
				rb.useGravity = false;
				rb.isKinematic = true;

				fixStatTrackerHookScale(model);
				syncStatTrackTrackerVisibility(model);

				if (slot == 0)
				{
					if (asset.type == EItemType.MELEE)
					{
						model.transform.parent = thirdPrimaryMeleeSlot;
					}
					else
					{
						if (asset.slot == ESlotType.PRIMARY)
						{
							model.transform.parent = thirdPrimaryLargeGunSlot;
						}
						else
						{
							model.transform.parent = thirdPrimarySmallGunSlot;
						}
					}
				}
				else if (slot == 1)
				{
					if (asset.type == EItemType.MELEE)
					{
						model.transform.parent = thirdSecondaryMeleeSlot;
					}
					else
					{
						model.transform.parent = thirdSecondaryGunSlot;
					}
				}

				model.localPosition = Vector3.zero;
				model.localRotation = Quaternion.Euler(0, 0, 90);
				model.localScale = Vector3.one;

				model.gameObject.SetActive(false);
				model.gameObject.SetActive(true);

				Layerer.enemy(model);

				if (mythicID != 0)
				{
					thirdMythics[slot] = ItemTool.ApplyMythicalEffect(model, mythicID, EEffectType.THIRD);
				}
				else
				{
					thirdMythics[slot] = null;
				}

				thirdSlots[slot] = model;
				thirdSkinneds[slot] = true;
				applySkinVisual();

				if (thirdMythics[slot] != null)
				{
					thirdMythics[slot].IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
				}

				if (characterSlots != null)
				{
					// characterModel keeps colliders so it can be clicked in the inventory screen.
					model = ItemTool.getItem(id, skinID, 100, state, false, asset, skinAsset, tempCharacterMeshes[slot], out tempCharacterMaterials[slot], statTrackerCallback);
					fixStatTrackerHookScale(model);
					syncStatTrackTrackerVisibility(model);

					if (slot == 0)
					{
						if (asset.type == EItemType.MELEE)
						{
							model.transform.parent = characterPrimaryMeleeSlot;
						}
						else
						{
							if (asset.slot == ESlotType.PRIMARY)
							{
								model.transform.parent = characterPrimaryLargeGunSlot;
							}
							else
							{
								model.transform.parent = characterPrimarySmallGunSlot;
							}
						}
					}
					else if (slot == 1)
					{
						if (asset.type == EItemType.MELEE)
						{
							model.transform.parent = characterSecondaryMeleeSlot;
						}
						else
						{
							model.transform.parent = characterSecondaryGunSlot;
						}
					}

					model.localPosition = Vector3.zero;
					model.localRotation = Quaternion.Euler(0, 0, 90);
					model.localScale = Vector3.one;

					model.gameObject.SetActive(false);
					model.gameObject.SetActive(true);

					Layerer.enemy(model);

					if (mythicID != 0)
					{
						characterMythics[slot] = ItemTool.ApplyMythicalEffect(model, mythicID, EEffectType.THIRD);
					}
					else
					{
						characterMythics[slot] = null;
					}

					characterSlots[slot] = model;
					characterSkinneds[slot] = true;
					applySkinVisual();

					if (characterMythics[slot] != null)
					{
						characterMythics[slot].IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
					}
				}
			}
		}

		[System.Obsolete]
		public void askToggleVision(CSteamID steamID)
		{
			ReceiveToggleVisionRequest();
		}

		private static readonly ServerInstanceMethod SendToggleVisionRequest = ServerInstanceMethod.Get(typeof(PlayerEquipment), nameof(ReceiveToggleVisionRequest));
		[SteamCall(ESteamCallValidation.ONLY_FROM_OWNER, ratelimitHz = 2, legacyName = nameof(askToggleVision))]
		public void ReceiveToggleVisionRequest()
		{
			if (!hasVision)
			{
				return;
			}

			if (player.clothing.glassesState.Length != 1)
			{
				return;
			}

			SendToggleVision.InvokeAndLoopback(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections());

			if (player.clothing.glassesAsset != null)
			{
				if (player.clothing.glassesAsset.vision == ELightingVision.HEADLAMP)
				{
					EffectManager.TriggerFiremodeEffect(transform.position);
				}
				else if (player.clothing.glassesAsset.vision == ELightingVision.CIVILIAN || player.clothing.glassesAsset.vision == ELightingVision.MILITARY)
				{
					EffectAsset beep = BeepRef.Find();
					if (beep != null)
					{
						TriggerEffectParameters triggerEffectParameters = new TriggerEffectParameters(beep);
						triggerEffectParameters.relevantDistance = EffectManager.SMALL;
						triggerEffectParameters.position = transform.position;
						EffectManager.triggerEffect(triggerEffectParameters);
					}
				}
			}
		}

		private static readonly AssetReference<EffectAsset> BeepRef = new AssetReference<EffectAsset>("f515fcbe1b5241e39217b52317e68d72"); // (56)

		[System.Obsolete]
		public void tellToggleVision(CSteamID steamID)
		{
			ReceiveToggleVision();
		}

		private static readonly ClientInstanceMethod SendToggleVision = ClientInstanceMethod.Get(typeof(PlayerEquipment), nameof(ReceiveToggleVision));
		[SteamCall(ESteamCallValidation.ONLY_FROM_SERVER, legacyName = nameof(tellToggleVision))]
		public void ReceiveToggleVision()
		{
			if (!hasVision)
			{
				return;
			}

			if (player.clothing.glassesState.Length != 1)
			{
				return;
			}

			player.clothing.glassesState[0] = (byte) (player.clothing.glassesState[0] == 0 ? 1 : 0);
			updateVision();
		}

		[System.Obsolete]
		public void tellSlot(CSteamID steamID, byte slot, ushort id, byte[] state)
		{
			ReceiveSlot(slot, id, state);
		}

		private static readonly ClientInstanceMethod<byte, ushort, byte[]> SendSlot = ClientInstanceMethod<byte, ushort, byte[]>.Get(typeof(PlayerEquipment), nameof(ReceiveSlot));
		[SteamCall(ESteamCallValidation.ONLY_FROM_SERVER, legacyName = nameof(tellSlot))]
		public void ReceiveSlot(byte slot, ushort id, byte[] state)
		{
			updateSlot(slot, id, state);
		}

		[System.Obsolete]
		public void tellUpdateStateTemp(CSteamID steamID, byte[] newState)
		{
			ReceiveUpdateStateTemp(newState);
		}

		private static readonly ClientInstanceMethod<byte[]> SendUpdateStateTemp = ClientInstanceMethod<byte[]>.Get(typeof(PlayerEquipment), nameof(ReceiveUpdateStateTemp));
		[SteamCall(ESteamCallValidation.ONLY_FROM_SERVER, legacyName = nameof(tellUpdateStateTemp))]
		public void ReceiveUpdateStateTemp(byte[] newState)
		{
			_state = newState;

			if (useable != null)
			{
				try
				{
					useable.updateState(state);
				}
				catch (System.Exception exception)
				{
					UnturnedLog.warn("{0} raised an exception during ReceiveUpdateStateTemp.updateState:", asset);
					UnturnedLog.exception(exception);
				}
			}
		}

		[System.Obsolete]
		public void tellUpdateState(CSteamID steamID, byte page, byte index, byte[] newState)
		{
			ReceiveUpdateState(page, index, newState);
		}

		private static readonly ClientInstanceMethod<byte, byte, byte[]> SendUpdateState = ClientInstanceMethod<byte, byte, byte[]>.Get(typeof(PlayerEquipment), nameof(ReceiveUpdateState));
		[SteamCall(ESteamCallValidation.ONLY_FROM_SERVER, legacyName = nameof(tellUpdateState))]
		public void ReceiveUpdateState(byte page, byte index, byte[] newState)
		{
			if (thirdSlots == null)
			{
				return;
			}

			_state = newState;

			if (slot != 255)
			{
				if (slot < thirdSlots.Length && thirdSlots[slot] != null)
				{
					updateSlot(slot, itemID, newState);
					thirdSlots[slot].gameObject.SetActive(false);

					if (characterSlots != null)
					{
						characterSlots[slot].gameObject.SetActive(false);
					}
				}
			}

			if (channel.IsLocalPlayer || Provider.isServer)
			{
				player.inventory.updateState(page, index, state);
			}

			if (useable != null)
			{
				try
				{
					useable.updateState(state);
				}
				catch (System.Exception exception)
				{
					UnturnedLog.warn("{0} raised an exception during tellUpdateState.updateState:", asset);
					UnturnedLog.exception(exception);
				}
			}

			if (characterModel != null)
			{
				Destroy(characterModel.gameObject);

				ItemAsset asset = Assets.find(EAssetType.ITEM, itemID) as ItemAsset;

				if (asset != null)
				{
					int item = 0;
					ushort skinID = 0;
					ushort mythicID = 0;
					bool isSharedSkin = asset != null && asset.sharedSkinLookupID != asset.id;
					ushort skinLookupId = isSharedSkin ? asset.sharedSkinLookupID : asset.id;
					if (channel.owner.skinItems != null && channel.owner.itemSkins != null && channel.owner.itemSkins.TryGetValue(skinLookupId, out item))
					{
						if (!isSharedSkin || asset.SharedSkinShouldApplyVisuals)
						{
							skinID = Provider.provider.economyService.getInventorySkinID(item);
						}
						mythicID = Provider.provider.economyService.getInventoryMythicID(item);
						if (mythicID == 0)
						{
							mythicID = channel.owner.getParticleEffectForItemDef(item);
						}
					}

					SkinAsset skinAsset = Assets.find(EAssetType.SKIN, skinID) as SkinAsset;

					GetStatTrackerValueHandler statTrackerCallback = null;
					if (slot == 0)
					{
						statTrackerCallback = getSlot0StatTrackerValue;
					}
					else if (slot == 1)
					{
						statTrackerCallback = getSlot1StatTrackerValue;
					}

					GameObject prefab = asset.equipablePrefab != null ? asset.equipablePrefab : asset.item;

					// characterModel keeps colliders so it can be clicked in the inventory screen.
					_characterModel = ItemTool.getItem(100, state, false, asset, skinAsset, tempCharacterMesh, out tempCharacterMaterial, getUseableStatTrackerValue, prefabOverride: prefab);
					fixStatTrackerHookScale(_characterModel);
					syncStatTrackTrackerVisibility(_characterModel);
					characterEventComponent = _characterModel.GetComponent<UseableEventHook>();

					Transform characterModelParent;
					switch (asset.EquipableModelParent)
					{
						default:
						case EEquipableModelParent.RightHook:
							characterModelParent = characterRightHook;
							break;

						case EEquipableModelParent.LeftHook:
							characterModelParent = characterLeftHook;
							break;

						case EEquipableModelParent.Spine:
							characterModelParent = _characterSpine;
							break;

						case EEquipableModelParent.SpineHook:
							characterModelParent = _characterSpineHook;
							break;
					}
					characterModel.transform.parent = characterModelParent;

					characterModel.localPosition = Vector3.zero;
					characterModel.localRotation = Quaternion.Euler(0, 0, 90);
					characterModel.localScale = Vector3.one;
					characterModel.gameObject.AddComponent<Rigidbody>();
					characterModel.GetComponent<Rigidbody>().useGravity = false;
					characterModel.GetComponent<Rigidbody>().isKinematic = true;

					if (mythicID != 0)
					{
						characterMythic = ItemTool.ApplyMythicalEffect(characterModel, mythicID, EEffectType.THIRD);
					}
					else
					{
						characterMythic = null;
					}

					characterSkinned = true;
					applySkinVisual();

					if (characterMythic != null)
					{
						characterMythic.IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
					}
				}
			}
		}

		[System.Obsolete]
		public void tellEquip(CSteamID steamID, byte page, byte x, byte y, ushort id, byte newQuality, byte[] newState)
		{
			Asset asset = Assets.find(EAssetType.ITEM, id);
			ReceiveEquip(page, x, y, asset?.GUID ?? System.Guid.Empty, newQuality, newState, new NetId());
		}

		private static readonly ClientInstanceMethod<byte, byte, byte, System.Guid, byte, byte[], NetId> SendEquip
			= ClientInstanceMethod<byte, byte, byte, System.Guid, byte, byte[], NetId>.Get(typeof(PlayerEquipment), nameof(ReceiveEquip));
		[SteamCall(ESteamCallValidation.ONLY_FROM_SERVER, legacyName = nameof(tellEquip))]
		public void ReceiveEquip(byte page, byte x, byte y, System.Guid newAssetGuid, byte newQuality, byte[] newState, NetId useableNetId)
		{
			if (thirdSlots == null)
			{
				return;
			}

			if (slot != 255)
			{
				if (slot < thirdSlots.Length && thirdSlots[slot] != null)
				{
					thirdSlots[slot].gameObject.SetActive(true);

					if (characterSlots != null)
					{
						characterSlots[slot].gameObject.SetActive(true);
					}
				}
			}

			slot = page;

			if (slot != 255)
			{
				if (slot < thirdSlots.Length && thirdSlots[slot] != null)
				{
					thirdSlots[slot].gameObject.SetActive(false);

					if (characterSlots != null)
					{
						characterSlots[slot].gameObject.SetActive(false);
					}
				}
			}

			if (useable != null)
			{
				try
				{
					useable.dequip();
				}
				catch (System.Exception exception)
				{
					UnturnedLog.warn("{0} raised an exception during tellEquip.dequip:", asset);
					UnturnedLog.exception(exception);
				}

				_useable.ReleaseNetId();

				// In the past we used DestroyImmediate rather than Destroy for the useable
				// because otherwise SteamChannel finds it when getting components, but now
				// we mark it as "not editable" as a "pending destroy" flag.
				useable.hideFlags |= HideFlags.NotEditable;
				Destroy(useable);

				_useable = null;
				channel.markDirty(); // Useable had RPCs, so calls array needs rebuilding.
			}

			firstEventComponent = null;
			thirdEventComponent = null;
			characterEventComponent = null;

			AnimationClip[] oldAnims = useableSkin?.overrideAnimations ?? asset?.animations;

			skinRagdollEffect = ERagdollEffect.None;
			useableSkin = null;

			if (firstModel != null)
			{
				Destroy(firstModel.gameObject);
			}

			firstSkinned = false;
			tempFirstMaterial = null;
			firstMythic = null;

			if (thirdModel != null)
			{
				Destroy(thirdModel.gameObject);
			}

			thirdSkinned = false;
			tempThirdMaterial = null;
			thirdMythic = null;

			if (characterModel != null)
			{
				Destroy(characterModel.gameObject);
			}

			characterSkinned = false;
			tempCharacterMaterial = null;
			characterMythic = null;

			if (oldAnims != null && oldAnims.Length > 0)
			{
				for (int index = 0; index < oldAnims.Length; index++)
				{
					player.animator.removeAnimation(oldAnims[index]);
				}
			}

			isBusy = false;

			lastInspect = 0;
#if !DEDICATED_SERVER
			// Stop audio if playing.
			inspectAudioHandle.Stop();
			equipAudioHandle.Stop();
#endif // !DEDICATED_SERVER

			if (newAssetGuid.IsEmpty())
			{
				_equippedPage = 255;
				_equipped_x = 255;
				_equipped_y = 255;
				_asset = null;

				OnUseableChanged_Global.TryInvoke("OnUseableChanged_Global", this);
				return;
			}

			_equippedPage = page;
			_equipped_x = x;
			_equipped_y = y;

			_asset = Assets.find(newAssetGuid) as ItemAsset;
			if (asset != null && asset.useableType != null)
			{
				quality = newQuality;
				_state = newState;

				int item = 0;
				ushort skinID = 0;
				ushort mythicID = 0;
				bool isSharedSkin = asset != null && asset.sharedSkinLookupID != asset.id;
				ushort skinLookupId = isSharedSkin ? asset.sharedSkinLookupID : asset.id;
				if (channel.owner.skinItems != null && channel.owner.itemSkins != null && channel.owner.itemSkins.TryGetValue(skinLookupId, out item))
				{
					if (!isSharedSkin || asset.SharedSkinShouldApplyVisuals)
					{
						skinID = Provider.provider.economyService.getInventorySkinID(item);
					}
					mythicID = Provider.provider.economyService.getInventoryMythicID(item);
					if (mythicID == 0)
					{
						mythicID = channel.owner.getParticleEffectForItemDef(item);
					}
				}
				useableSkin = Assets.find(EAssetType.SKIN, skinID) as SkinAsset;

				skinRagdollEffect = ERagdollEffect.None;
				if (!channel.owner.getRagdollEffect(asset.sharedSkinLookupID, out skinRagdollEffect) && useableSkin != null)
				{
					skinRagdollEffect = useableSkin.ragdollEffect;
				}

				GameObject prefab = asset.equipablePrefab != null ? asset.equipablePrefab : asset.item;
				if (useableSkin?.overrideAnimations?.Length > 0)
				{
					// Hack for classic canned beans skin. (Equipable prefab has incompatible interior details.)
					prefab = asset.item;
				}

				if (channel.IsLocalPlayer)
				{
					ClientAssetIntegrity.QueueRequest(_asset);

					_firstModel = ItemTool.InstantiateItem(quality, state, true, asset, useableSkin, /*shouldDestroyColliders*/ true, tempFirstMesh, out tempFirstMaterial, getUseableStatTrackerValue, prefabOverride: prefab);
					fixStatTrackerHookScale(_firstModel);
					syncStatTrackTrackerVisibility(_firstModel);
					firstEventComponent = firstModel.GetComponent<UseableEventHook>();

					Transform firstModelParent;
					switch (asset.EquipableModelParent)
					{
						default:
						case EEquipableModelParent.RightHook:
							firstModelParent = firstRightHook;
							break;

						case EEquipableModelParent.LeftHook:
							firstModelParent = firstLeftHook;
							break;

						case EEquipableModelParent.Spine:
							firstModelParent = _firstSpine;
							break;

						case EEquipableModelParent.SpineHook:
							firstModelParent = _firstSpineHook;
							break;
					}
					firstModel.transform.parent = firstModelParent;

					firstModel.localPosition = Vector3.zero;
					firstModel.localRotation = Quaternion.Euler(0, 0, 90);
					ApplyEquipableLocalScale(_asset, firstModel);

					firstModel.gameObject.SetActive(false);
					firstModel.gameObject.SetActive(true);

					firstModel.DestroyRigidbody();

					if (mythicID != 0)
					{
						firstMythic = ItemTool.ApplyMythicalEffect(firstModel, mythicID, EEffectType.FIRST);
					}
					else
					{
						firstMythic = null;
					}

					firstSkinned = true;
					applySkinVisual();

					if (firstMythic != null)
					{
						firstMythic.IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
					}

					// characterModel keeps colliders so it can be clicked in the inventory screen.
					_characterModel = ItemTool.getItem(quality, state, false, asset, useableSkin, tempCharacterMesh, out tempCharacterMaterial, getUseableStatTrackerValue, prefabOverride: prefab);
					fixStatTrackerHookScale(_characterModel);
					syncStatTrackTrackerVisibility(_characterModel);

					Transform characterModelParent;
					switch (asset.EquipableModelParent)
					{
						default:
						case EEquipableModelParent.RightHook:
							characterModelParent = characterRightHook;
							break;

						case EEquipableModelParent.LeftHook:
							characterModelParent = characterLeftHook;
							break;

						case EEquipableModelParent.Spine:
							characterModelParent = _characterSpine;
							break;

						case EEquipableModelParent.SpineHook:
							characterModelParent = _characterSpineHook;
							break;
					}
					characterModel.transform.parent = characterModelParent;

					characterModel.localPosition = Vector3.zero;
					characterModel.localRotation = Quaternion.Euler(0, 0, 90);
					ApplyEquipableLocalScale(_asset, characterModel);

					Rigidbody characterRigidbody = characterModel.gameObject.GetOrAddComponent<Rigidbody>();
					characterRigidbody.useGravity = false;
					characterRigidbody.isKinematic = true;

					if (mythicID != 0)
					{
						characterMythic = ItemTool.ApplyMythicalEffect(characterModel, mythicID, EEffectType.THIRD);
					}
					else
					{
						characterMythic = null;
					}

					characterSkinned = true;
					applySkinVisual();

					if (characterMythic != null)
					{
						characterMythic.IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
					}
				}

				_thirdModel = ItemTool.InstantiateItem(quality, state, false, asset, useableSkin, /*shouldDestroyColliders*/ true, tempThirdMesh, out tempThirdMaterial, getUseableStatTrackerValue, prefabOverride: prefab);
				fixStatTrackerHookScale(_thirdModel);
				syncStatTrackTrackerVisibility(_thirdModel);
				thirdEventComponent = _thirdModel.GetComponent<UseableEventHook>();

				Transform thirdModelParent;
				switch (asset.EquipableModelParent)
				{
					default:
					case EEquipableModelParent.RightHook:
						thirdModelParent = thirdRightHook;
						break;

					case EEquipableModelParent.LeftHook:
						thirdModelParent = thirdLeftHook;
						break;

					case EEquipableModelParent.Spine:
						thirdModelParent = _thirdSpine;
						break;

					case EEquipableModelParent.SpineHook:
						thirdModelParent = _thirdSpineHook;
						break;
				}
				thirdModel.transform.parent = thirdModelParent;

				thirdModel.localPosition = Vector3.zero;
				thirdModel.localRotation = Quaternion.Euler(0, 0, 90);
				ApplyEquipableLocalScale(_asset, thirdModel);

				thirdModel.gameObject.SetActive(false);
				thirdModel.gameObject.SetActive(true);

				// Without rigidbody the vehicle physics get stuck driving straight...
				// Keep this for mods that disable collider removal.
				Rigidbody rb = thirdModel.GetOrAddComponent<Rigidbody>();
				rb.useGravity = false;
				rb.isKinematic = true;

				Layerer.enemy(thirdModel);

				if (mythicID != 0)
				{
					thirdMythic = ItemTool.ApplyMythicalEffect(thirdModel, mythicID, EEffectType.THIRD);
				}
				else
				{
					thirdMythic = null;
				}

				thirdSkinned = true;
				applySkinVisual();

				if (thirdMythic != null)
				{
					thirdMythic.IsMythicalEffectEnabled = player.clothing.isSkinned && player.clothing.isMythic;
				}

				AnimationClip[] anims = useableSkin?.overrideAnimations ?? asset?.animations;
				if (anims != null && anims.Length > 0)
				{
					for (int index = 0; index < anims.Length; index++)
					{
						player.animator.AddEquippedItemAnimation(anims[index], _firstModel, _thirdModel, _characterModel);
					}
				}

				_useable = gameObject.AddComponent(asset.useableType) as Useable;
				_useable.AssignNetId(useableNetId);
				wasUsablePrimaryStarted = false;
				wasUsableSecondaryStarted = false;

				// New useable has RPCs, so calls array needs to be rebuilt.
				// Dirtied before equipping in case equip uses an RPC.
				channel.markDirty();

				try
				{
					useable.equip();
				}
				catch (System.Exception exception)
				{
					UnturnedLog.warn("{0} raised an exception during tellEquip.equip:", asset);
					UnturnedLog.exception(exception);
				}

				equipAnimStartedFrame = player.input.simulation;
				float equipAnimLengthSeconds = player.animator.GetAnimationLength("Equip");
				equipAnimLengthFrames = MathfEx.CeilToUInt(equipAnimLengthSeconds / PlayerInput.RATE);
				equipAnimCompletedTime = Time.timeAsDouble + equipAnimLengthSeconds;

#if !DEDICATED_SERVER
				if (!Dedicator.IsDedicatedServer)
				{
					if (asset.equip != null)
					{
						equipAudioHandle = player.playSound(asset.equip, 1f, 0.05f);
					}
				}
#endif // !DEDICATED_SERVER

				OnUseableChanged_Global.TryInvoke("OnUseableChanged_Global", this);
			}
		}

		[System.Obsolete("Renamed to ServerEquip")]
		public void tryEquip(byte page, byte x, byte y)
		{
			ServerEquip(page, x, y);
		}

		[System.Obsolete("No longer necessary after hash check was converted to newer system")]
		public void tryEquip(byte page, byte x, byte y, byte[] hash)
		{
			ServerEquip(page, x, y);
		}

		public void ServerEquip(byte page, byte x, byte y)
		{
			if (isBusy || !canEquip || player.life.isDead || player.stance.stance == EPlayerStance.CLIMB || player.stance.stance == EPlayerStance.DRIVING) // || player.stance.stance == EPlayerStance.SWIM
			{
				return;
			}

			if (HasValidUseable && !IsEquipAnimationFinished)
			{
				return;
			}

			if (isTurret)
			{
				return;
			}

			if ((page == equippedPage && x == equipped_x && y == equipped_y) || page == 255)
			{
				bool shouldAllow = true;
				onDequipRequested?.Invoke(this, ref shouldAllow);

				if (!shouldAllow)
				{
					return;
				}

				dequip();
			}
			else
			{
				if (page < 0 || page >= PlayerInventory.PAGES - 2)
				{
					return;
				}

				byte index = player.inventory.getIndex(page, x, y);

				if (index == 255)
				{
					return;
				}

				ItemJar jar = player.inventory.getItem(page, index);

				if (jar == null)
				{
					return;
				}

				if (ItemTool.checkUseable(page, jar.item.id))
				{
					ItemAsset asset = jar.GetAsset();

					if (asset == null)
					{
						return;
					}

					if (player.stance.isSubmerged || player.stance.stance == EPlayerStance.SWIM)
					{
						if (asset.canUseUnderwater == false)
						{
							return;
						}
					}

					if (player.animator.gesture == EPlayerGesture.ARREST_START)
					{
						return;
					}

					bool shouldAllow = true;
					onEquipRequested?.Invoke(this, jar, asset, ref shouldAllow);

					if (!shouldAllow)
					{
						return;
					}

					NetId useableNetId = NetIdRegistry.Claim();
					if (jar.item.state != null)
					{
						SendEquip.InvokeAndLoopback(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), page, x, y, asset.GUID, jar.item.quality, jar.item.state, useableNetId);
					}
					else
					{
						SendEquip.InvokeAndLoopback(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), page, x, y, asset.GUID, jar.item.quality, new byte[0], useableNetId);
					}
				}
			}
		}

		public void turretEquipClient()
		{
			isTurret = true;
		}

		public void turretEquipServer(ushort id, byte[] state)
		{
			// NOT loopback because we do not want a new copy of the array!
			Asset asset = Assets.find(EAssetType.ITEM, id);
			System.Guid newAssetGuid = asset?.GUID ?? System.Guid.Empty;
			NetId useableNetId = NetIdRegistry.Claim();
			SendEquip.Invoke(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), 254, 254, 254, newAssetGuid, 100, state, useableNetId);
			ReceiveEquip(254, 254, 254, newAssetGuid, 100, state, useableNetId);
		}

		public void turretDequipClient()
		{
			isTurret = false;
		}

		public void turretDequipServer()
		{
			SendEquip.InvokeAndLoopback(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), 255, 255, 255, System.Guid.Empty, 0, new byte[0], new NetId());
		}

		[System.Obsolete]
		public void askEquip(CSteamID steamID, byte page, byte x, byte y, byte[] hash)
		{
			ServerEquip(page, x, y);
		}

		private static readonly ServerInstanceMethod<byte, byte, byte> SendEquipRequest = ServerInstanceMethod<byte, byte, byte>.Get(typeof(PlayerEquipment), nameof(ReceiveEquipRequest));
		[SteamCall(ESteamCallValidation.ONLY_FROM_OWNER, ratelimitHz = 5, legacyName = nameof(askEquip))]
		public void ReceiveEquipRequest(byte page, byte x, byte y)
		{
			ServerEquip(page, x, y);
		}

		[System.Obsolete]
		public void askEquipment(CSteamID steamID)
		{ }

		internal void SendInitialPlayerState(SteamPlayer client)
		{
			for (byte slot = 0; slot < PlayerInventory.SLOTS; slot++)
			{
				ItemJar jar = player.inventory.getItem(slot, 0);

				if (jar != null)
				{
					if (jar.item.state != null)
					{
						SendSlot.Invoke(GetNetId(), ENetReliability.Reliable, client.transportConnection, slot, jar.item.id, jar.item.state);
					}
					else
					{
						SendSlot.Invoke(GetNetId(), ENetReliability.Reliable, client.transportConnection, slot, jar.item.id, new byte[0]);
					}
				}
				else
				{
					SendSlot.Invoke(GetNetId(), ENetReliability.Reliable, client.transportConnection, slot, 0, new byte[0]);
				}
			}

			if (HasValidUseable)
			{
				System.Guid assetGuid = asset?.GUID ?? System.Guid.Empty;
				NetId useableNetId = useable.GetNetId();
				if (state != null)
				{
					SendEquip.Invoke(GetNetId(), ENetReliability.Reliable, client.transportConnection, equippedPage, equipped_x, equipped_y, assetGuid, quality, state, useableNetId);
				}
				else
				{
					SendEquip.Invoke(GetNetId(), ENetReliability.Reliable, client.transportConnection, equippedPage, equipped_x, equipped_y, assetGuid, quality, new byte[0], useableNetId);
				}
			}
		}

		internal void SendInitialPlayerState(List<ITransportConnection> transportConnections)
		{
			for (byte slot = 0; slot < PlayerInventory.SLOTS; slot++)
			{
				ItemJar jar = player.inventory.getItem(slot, 0);

				if (jar != null)
				{
					if (jar.item.state != null)
					{
						SendSlot.Invoke(GetNetId(), ENetReliability.Reliable, transportConnections, slot, jar.item.id, jar.item.state);
					}
					else
					{
						SendSlot.Invoke(GetNetId(), ENetReliability.Reliable, transportConnections, slot, jar.item.id, new byte[0]);
					}
				}
				else
				{
					SendSlot.Invoke(GetNetId(), ENetReliability.Reliable, transportConnections, slot, 0, new byte[0]);
				}
			}

			if (HasValidUseable)
			{
				System.Guid assetGuid = asset?.GUID ?? System.Guid.Empty;
				NetId useableNetId = useable.GetNetId();
				if (state != null)
				{
					SendEquip.Invoke(GetNetId(), ENetReliability.Reliable, transportConnections, equippedPage, equipped_x, equipped_y, assetGuid, quality, state, useableNetId);
				}
				else
				{
					SendEquip.Invoke(GetNetId(), ENetReliability.Reliable, transportConnections, equippedPage, equipped_x, equipped_y, assetGuid, quality, new byte[0], useableNetId);
				}
			}
		}

		public void updateState()
		{
			if (isTurret)
			{
				return;
			}

			byte index = player.inventory.getIndex(equippedPage, equipped_x, equipped_y);
			if (index != byte.MaxValue)
			{
				player.inventory.updateState(equippedPage, index, state);
			}
		}

		public void updateQuality()
		{
			if (isTurret)
			{
				return;
			}

			byte index = player.inventory.getIndex(equippedPage, equipped_x, equipped_y);
			if (index != byte.MaxValue)
			{
				player.inventory.updateQuality(equippedPage, index, quality);
			}
		}

		public void sendUpdateState()
		{
			if (isTurret)
			{
				// NOT loopback because we do not want a new copy of the array!
				SendUpdateStateTemp.Invoke(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), state);
				ReceiveUpdateStateTemp(state);
			}
			else
			{
				byte index = player.inventory.getIndex(equippedPage, equipped_x, equipped_y);
				if (index != byte.MaxValue)
				{
					SendUpdateState.InvokeAndLoopback(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), equippedPage, index, state);
				}
			}
		}

		public void sendUpdateQuality()
		{
			if (isTurret)
			{
				return;
			}

			player.inventory.sendUpdateQuality(equippedPage, equipped_x, equipped_y, quality);
		}

		public void sendSlot(byte slot)
		{
			ItemJar jar = player.inventory.getItem(slot, 0);

			if (jar != null)
			{
				if (jar.item.state != null)
				{
					SendSlot.InvokeAndLoopback(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), slot, jar.item.id, jar.item.state);
				}
				else
				{
					SendSlot.InvokeAndLoopback(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), slot, jar.item.id, new byte[0]);
				}
			}
			else
			{
				SendSlot.InvokeAndLoopback(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), slot, 0, new byte[0]);
			}
		}

		/// <summary>
		/// Called clientside to ask server to equip an item in the inventory.
		/// </summary>
		public void equip(byte page, byte x, byte y)
		{
			if (page < 0 || page >= PlayerInventory.PAGES - 2)
			{
				return;
			}

			if (isBusy || !canEquip || player.life.isDead || player.stance.stance == EPlayerStance.CLIMB || player.stance.stance == EPlayerStance.DRIVING)// || player.stance.stance == EPlayerStance.SWIM
			{
				return;
			}

			if (HasValidUseable && !IsEquipAnimationFinished)
			{
				return;
			}

			byte index = player.inventory.getIndex(page, x, y);

			if (index == 255)
			{
				return;
			}

			ItemJar jar = player.inventory.getItem(page, index);

			if (jar == null)
			{
				return;
			}

			ItemAsset asset = jar.GetAsset();

			if (asset == null)
			{
				return;
			}

			if (player.stance.isSubmerged || player.stance.stance == EPlayerStance.SWIM)
			{
				if (asset.canUseUnderwater == false)
				{
					return;
				}
			}

			if (player.animator.gesture == EPlayerGesture.ARREST_START)
			{
				return;
			}

			lastEquip = Time.realtimeSinceStartup;

			SendEquipRequest.Invoke(GetNetId(), ENetReliability.Unreliable, page, x, y);
		}

		/// <summary>
		/// Hacked-in to bypass regular clientside checks when client would predict the item at given coords.
		/// </summary>
		internal void ClientEquipAfterItemDrag(byte page, byte x, byte y)
		{
			SendEquipRequest.Invoke(GetNetId(), ENetReliability.Unreliable, page, x, y);
		}

		public void dequip()
		{
			if (isTurret)
			{
				return;
			}

			if (ignoreDequip_A)
				return;

			if (Provider.isServer)
			{
				SendEquip.InvokeAndLoopback(GetNetId(), ENetReliability.Reliable, Provider.GatherRemoteClientConnections(), 255, 255, 255, System.Guid.Empty, 0, new byte[0], new NetId());
			}
			else
			{
				if (isBusy)
				{
					return;
				}

				SendEquipRequest.Invoke(GetNetId(), ENetReliability.Unreliable, 255, 255, 255);
			}
		}

		public void use()
		{
			if (HasValidUseable)
			{
				ItemAsset findAsset = asset;

				byte index = player.inventory.getIndex(equippedPage, equipped_x, equipped_y);
				ItemJar jar = player.inventory.getItem(equippedPage, index);

				byte page = equippedPage;
				byte x = equipped_x;
				byte y = equipped_y;
				byte rot = jar.rot;

				player.inventory.removeItem(equippedPage, index);
				dequip();

				if (player.inventory.FindFirstItemByAsset(findAsset, out PlayerInventorySearchResultV2 searchResult))
				{
					player.inventory.ReceiveDragItem(searchResult.Page, searchResult.Jar.x, searchResult.Jar.y, page, x, y, rot);
					ServerEquip(page, x, y);
				}
				else
				{
					sendSlot(equippedPage);
				}
			}
		}

		protected byte page_A;
		protected byte x_A;
		protected byte y_A;
		protected byte rot_A;
		protected bool ignoreDequip_A;

		/// <summary>
		/// Remove the item from inventory so that if we die before <see cref="useStepB"/> the item isn't dropped
		/// </summary>
		public void useStepA()
		{
			if (HasValidUseable)
			{
				byte index = player.inventory.getIndex(equippedPage, equipped_x, equipped_y);
				ItemJar jar = player.inventory.getItem(equippedPage, index);

				page_A = equippedPage;
				x_A = equipped_x;
				y_A = equipped_y;
				rot_A = jar.rot;

				ignoreDequip_A = true;
				player.inventory.removeItem(equippedPage, index);
				ignoreDequip_A = false;
			}
		}

		/// <summary>
		/// Finish dequipping from <see cref="useStepA"/>
		/// </summary>
		public void useStepB()
		{
			if (HasValidUseable)
			{
				ItemAsset findAsset = asset;
				dequip();
				if (player.inventory.FindFirstItemByAsset(findAsset, out PlayerInventorySearchResultV2 searchResult))
				{
					player.inventory.ReceiveDragItem(searchResult.Page, searchResult.Jar.x, searchResult.Jar.y, page_A, x_A, y_A, rot_A);
					ServerEquip(page_A, x_A, y_A);
				}
				else
				{
					sendSlot(page_A);
				}
			}
		}

		/// <summary>
		/// Invoked before dealing damage regardless of whether the punch impacted anything.
		/// </summary>
		public static System.Action<PlayerEquipment, EPlayerPunch> OnPunch_Global;

		private static MasterBundleReference<AudioClip> punchClipRef = new MasterBundleReference<AudioClip>("core.masterbundle", "Sounds/MeleeAttack_02.mp3");

		internal void PlayPunchAudioClip()
		{
			AudioClip punchClip = punchClipRef.loadAsset();
			if (punchClip == null)
			{
				UnturnedLog.warn("Missing built-in punching audio");
			}

			player.playSound(punchClip);
		}

		private void punch(EPlayerPunch mode)
		{
			if (channel.IsLocalPlayer)
			{
				PlayPunchAudioClip();

				Ray ray = new Ray(player.look.aim.position, player.look.aim.forward);
				RaycastInfo info = DamageTool.raycast(ray, 1.75f, RayMasks.DAMAGE_CLIENT, ignorePlayer: player);

				if (info.player != null && DAMAGE_PLAYER_MULTIPLIER.damage > 1 && DamageTool.isPlayerAllowedToDamagePlayer(player, info.player))
				{
					PlayerUI.hitmark(info.point, false, info.limb == ELimb.SKULL ? EPlayerHit.CRITICAL : EPlayerHit.ENTITIY);
				}
				else if ((info.zombie != null && DAMAGE_ZOMBIE_MULTIPLIER.damage > 1) || (info.animal != null && DAMAGE_ANIMAL_MULTIPLIER.damage > 1))
				{
					PlayerUI.hitmark(info.point, false, info.limb == ELimb.SKULL ? EPlayerHit.CRITICAL : EPlayerHit.ENTITIY);
				}
				else if (info.transform != null && info.transform.CompareTag("Barricade") && DAMAGE_BARRICADE > 1)
				{
					BarricadeDrop barricade = BarricadeDrop.FindByRootFast(info.transform);
					if (barricade != null)
					{
						ItemBarricadeAsset asset = barricade.asset;
						if (asset != null && asset.canBeDamaged && asset.isVulnerable)
						{
							PlayerUI.hitmark(info.point, false, EPlayerHit.BUILD);
						}
					}
				}
				else if (info.transform != null && info.transform.CompareTag("Structure") && DAMAGE_STRUCTURE > 1)
				{
					StructureDrop structure = StructureDrop.FindByRootFast(info.transform);
					if (structure != null)
					{
						ItemStructureAsset asset = structure.asset;
						if (asset != null && asset.canBeDamaged && asset.isVulnerable)
						{
							PlayerUI.hitmark(info.point, false, EPlayerHit.BUILD);
						}
					}
				}
				else if (info.vehicle != null && !info.vehicle.isDead && DAMAGE_VEHICLE > 1)
				{
					if (info.vehicle.asset != null && info.vehicle.canBeDamaged && info.vehicle.asset.isVulnerable)
					{
						PlayerUI.hitmark(info.point, false, EPlayerHit.BUILD);
					}
				}
				else if (info.transform != null && info.transform.CompareTag("Resource") && DAMAGE_RESOURCE > 1)
				{
					byte x;
					byte y;
					ushort index;
					if (ResourceManager.tryGetRegion(info.transform, out x, out y, out index))
					{
						ResourceSpawnpoint spawnpoint = ResourceManager.getResourceSpawnpoint(x, y, index);

						if (spawnpoint != null && !spawnpoint.isDead && spawnpoint.asset.vulnerableToFists)
						{
							PlayerUI.hitmark(info.point, false, EPlayerHit.BUILD);
						}
					}
				}
				else if (info.transform != null && DAMAGE_OBJECT > 1)
				{
					InteractableObjectRubble rubble = info.transform.GetComponentInParent<InteractableObjectRubble>();
					if (rubble != null)
					{
						info.transform = rubble.transform;
						info.section = rubble.getSection(info.collider.transform);
						if (rubble.IsSectionIndexValid(info.section) && !rubble.isSectionDead(info.section) && rubble.asset.rubbleBladeID == 0)
						{
							if (rubble.asset.rubbleIsVulnerable)
							{
								PlayerUI.hitmark(info.point, false, EPlayerHit.BUILD);
							}
						}
					}
				}

				player.input.sendRaycast(info, ERaycastInfoUsage.Punch);
			}

			if (mode == EPlayerPunch.LEFT)
			{
				player.animator.play("Punch_Left", false);

				if (Provider.isServer)
				{
					player.animator.sendGesture(EPlayerGesture.PUNCH_LEFT, false);
				}
			}
			else if (mode == EPlayerPunch.RIGHT)
			{
				player.animator.play("Punch_Right", false);

				if (Provider.isServer)
				{
					player.animator.sendGesture(EPlayerGesture.PUNCH_RIGHT, false);
				}
			}

			OnPunch_Global.TryInvoke("OnPunch_Global", this, mode);

			if (Provider.isServer)
			{
				if (!player.input.hasInputs())
				{
					return;
				}

				InputInfo info = player.input.getInput(true, ERaycastInfoUsage.Punch);

				if (info == null)
				{
					return;
				}

				if ((info.point - player.look.aim.position).sqrMagnitude > 36)
				{
					return;
				}

				if (!string.IsNullOrEmpty(info.materialName))
				{
					DamageTool.ServerSpawnLegacyImpact(info.point,
						info.normal,
						info.materialName,
						info.colliderTransform,
						channel.GatherOwnerAndClientConnectionsWithinSphere(info.point, EffectManager.SMALL));
				}

				EPlayerKill kill = EPlayerKill.NONE;
				uint xp = 0;

				float times = 1;
				times *= 1f + (channel.owner.player.skills.mastery((int) EPlayerSpeciality.OFFENSE, (int) EPlayerOffense.OVERKILL) * 0.5f);

				if (info.type == ERaycastInfoType.PLAYER)
				{
					lastPunching = Time.realtimeSinceStartup;

					if (info.player != null)
					{
						if (DamageTool.isPlayerAllowedToDamagePlayer(player, info.player))
						{
							DamagePlayerParameters parameters = DamagePlayerParameters.make(info.player, EDeathCause.PUNCH, info.direction, DAMAGE_PLAYER_MULTIPLIER, info.limb);
							parameters.killer = channel.owner.playerID.steamID;
							parameters.times = times;
							parameters.respectArmor = true;
							parameters.trackKill = true;

							if (player.input.IsUnderFakeLagPenalty)
							{
								parameters.times *= Provider.configData.Server.Fake_Lag_Damage_Penalty_Multiplier;
							}

							DamageTool.damagePlayer(parameters, out kill);
						}
					}
				}
				else if (info.type == ERaycastInfoType.ZOMBIE)
				{
					if (info.zombie != null)
					{
						IDamageMultiplier multiplier = DAMAGE_ZOMBIE_MULTIPLIER;
						DamageZombieParameters parameters = DamageZombieParameters.make(info.zombie, info.direction, multiplier, info.limb);
						parameters.times = times;
						parameters.allowBackstab = true;
						parameters.respectArmor = true;
						parameters.instigator = player;

						if (player.movement.nav != 255)
						{
							parameters.AlertPosition = transform.position;
						}

						DamageTool.damageZombie(parameters, out kill, out xp);
					}
				}
				else if (info.type == ERaycastInfoType.ANIMAL)
				{
					lastPunching = Time.realtimeSinceStartup;

					if (info.animal != null)
					{
						IDamageMultiplier multiplier = DAMAGE_ANIMAL_MULTIPLIER;
						DamageAnimalParameters parameters = DamageAnimalParameters.make(info.animal, info.direction, multiplier, info.limb);
						parameters.times = times;
						parameters.instigator = player;
						parameters.AlertPosition = transform.position;

						DamageTool.damageAnimal(parameters, out kill, out xp);
					}
				}
				else if (info.type == ERaycastInfoType.VEHICLE)
				{
					lastPunching = Time.realtimeSinceStartup;

					if (info.vehicle != null)
					{
						if (info.vehicle.asset != null && info.vehicle.canBeDamaged && info.vehicle.asset.isVulnerable)
						{
							DamageTool.damage(info.vehicle, false, Vector3.zero, false, DAMAGE_VEHICLE, times * Provider.modeConfigData.Vehicles.Melee_Damage_Multiplier, true, out kill, instigatorSteamID: channel.owner.playerID.steamID, damageOrigin: EDamageOrigin.Punch);
						}
					}
				}
				else if (info.type == ERaycastInfoType.BARRICADE)
				{
					lastPunching = Time.realtimeSinceStartup;

					if (info.transform != null && info.transform.CompareTag("Barricade"))
					{
						BarricadeDrop barricade = BarricadeDrop.FindByRootFast(info.transform);
						if (barricade != null)
						{
							ItemBarricadeAsset asset = barricade.asset;
							if (asset != null && asset.canBeDamaged && asset.isVulnerable)
							{
								DamageTool.damage(info.transform, false, DAMAGE_BARRICADE, times * Provider.modeConfigData.Barricades.Melee_Damage_Multiplier, out kill, instigatorSteamID: channel.owner.playerID.steamID, damageOrigin: EDamageOrigin.Punch);
							}
						}
					}
				}
				else if (info.type == ERaycastInfoType.STRUCTURE)
				{
					lastPunching = Time.realtimeSinceStartup;

					if (info.transform != null && info.transform.CompareTag("Structure"))
					{
						StructureDrop structure = StructureDrop.FindByRootFast(info.transform);
						if (structure != null)
						{
							ItemStructureAsset asset = structure.asset;
							if (asset != null && asset.canBeDamaged && asset.isVulnerable)
							{
								DamageTool.damage(info.transform, false, info.direction, DAMAGE_STRUCTURE, times * Provider.modeConfigData.Structures.Melee_Damage_Multiplier, out kill, instigatorSteamID: channel.owner.playerID.steamID, damageOrigin: EDamageOrigin.Punch);
							}
						}
					}
				}
				else if (info.type == ERaycastInfoType.RESOURCE)
				{
					lastPunching = Time.realtimeSinceStartup;

					if (info.transform != null && info.transform.CompareTag("Resource"))
					{
						byte x;
						byte y;
						ushort index;
						if (ResourceManager.tryGetRegion(info.transform, out x, out y, out index))
						{
							ResourceSpawnpoint spawnpoint = ResourceManager.getResourceSpawnpoint(x, y, index);

							if (spawnpoint != null && !spawnpoint.isDead && spawnpoint.asset.vulnerableToFists)
							{
								DamageTool.damage(info.transform, info.direction, DAMAGE_RESOURCE, times, 1f, out kill, out xp, instigatorSteamID: channel.owner.playerID.steamID, damageOrigin: EDamageOrigin.Punch);
							}
						}
					}
				}
				else if (info.type == ERaycastInfoType.OBJECT)
				{
					if (info.transform != null && info.section < byte.MaxValue)
					{
						InteractableObjectRubble rubble = info.transform.GetComponentInParent<InteractableObjectRubble>();
						if (rubble != null && rubble.IsSectionIndexValid(info.section) && !rubble.isSectionDead(info.section) && rubble.asset.rubbleBladeID == 0)
						{
							if (rubble.asset.rubbleIsVulnerable)
							{
								DamageTool.damage(rubble.transform, info.direction, info.section, DAMAGE_OBJECT, times, out kill, out xp, instigatorSteamID: channel.owner.playerID.steamID, damageOrigin: EDamageOrigin.Punch);
							}
						}
					}
				}

				// only do aggressor check if we didn't shoot a player (because we would already be marked aggressor) and if we weren't saving them from a zombie
				if (info.type != ERaycastInfoType.PLAYER && info.type != ERaycastInfoType.ZOMBIE && info.type != ERaycastInfoType.ANIMAL)
				{
					if (!player.life.isAggressor)
					{
						float bulletRange = 2.0f + Provider.modeConfigData.Players.Ray_Aggressor_Distance;
						bulletRange *= bulletRange;
						float rayAggressor = Provider.modeConfigData.Players.Ray_Aggressor_Distance;
						rayAggressor *= rayAggressor;

						Vector3 bulletNorm = player.look.aim.forward;

						for (int enemyIndex = 0; enemyIndex < Provider.clients.Count; enemyIndex++)
						{
							if (Provider.clients[enemyIndex] == channel.owner)
							{
								continue;
							}

							Player enemy = Provider.clients[enemyIndex].player;

							if (enemy == null)
							{
								continue;
							}

							Vector3 enemyOffset = enemy.look.aim.position - player.look.aim.position;
							Vector3 bulletProj = Vector3.Project(enemyOffset, bulletNorm);

							if (bulletProj.sqrMagnitude < bulletRange && (bulletProj - enemyOffset).sqrMagnitude < rayAggressor) // shot within 4 meters of enemy
							{
								player.life.markAggressive(false);
							}
						}
					}
				}

				if (Level.info.type == ELevelType.HORDE)
				{
					if (info.zombie != null)
					{
						if (info.limb == ELimb.SKULL)
						{
							player.skills.askPay(10);
						}
						else
						{
							player.skills.askPay(5);
						}
					}

					if (kill == EPlayerKill.ZOMBIE)
					{
						if (info.limb == ELimb.SKULL)
						{
							player.skills.askPay(50);
						}
						else
						{
							player.skills.askPay(25);
						}
					}
				}
				else
				{
					if (kill == EPlayerKill.PLAYER)
					{
						if (Level.info.type == ELevelType.ARENA)
						{
							player.skills.askPay(100);
						}
					}

					player.sendStat(kill);

					if (xp > 0)
					{
						player.skills.askPay(xp);
					}
				}
			}
		}

		/// <summary>
		/// (Temporarily?) separated out from simulate to try and get a better exception call stack.
		/// </summary>
		private bool simulate_MustDequip()
		{
			if (player.stance.stance == EPlayerStance.DRIVING && !isTurret)
			{
				// Player cannot enter vehicle while isBusy.
				return true;
			}
			else if (player.stance.stance == EPlayerStance.CLIMB)
			{
				// Player cannot begin climbing while isBusy.
				return !isBusy;
			}
			else if (player.stance.isSubmerged || player.stance.stance == EPlayerStance.SWIM)
			{
				if (asset != null && asset.canUseUnderwater == false)
				{
					// We let them finish reloading (or any isBusy) to avoid bugs.
					return !isBusy;
				}
			}

			return false;
		}

		private bool StartUsablePrimary()
		{
			bool started = false;
			UnityEngine.Profiling.Profiler.BeginSample("Start Primary");
			try
			{
				started = useable.startPrimary();
#if LOG_EQUIPMENT_START_STOP
				if (started)
				{
					UnturnedLog.info("Started primary");
				}
#endif // LOG_EQUIPMENT_START_STOP
			}
			catch (System.Exception exception)
			{
				UnturnedLog.warn("{0} raised an exception during simulate.startPrimary:", asset);
				UnturnedLog.exception(exception);
			}
			UnityEngine.Profiling.Profiler.EndSample();
			return started;
		}

		private void StopUsablePrimary()
		{
			UnityEngine.Profiling.Profiler.BeginSample("Stop Primary");
			try
			{
				useable.stopPrimary();
#if LOG_EQUIPMENT_START_STOP
				UnturnedLog.info("Stopped primary");
#endif // LOG_EQUIPMENT_START_STOP
			}
			catch (System.Exception exception)
			{
				UnturnedLog.warn("{0} raised an exception during simulate.stopPrimary:", asset);
				UnturnedLog.exception(exception);
			}
			UnityEngine.Profiling.Profiler.EndSample();
		}

		private bool StartUsableSecondary()
		{
			bool started = false;
			UnityEngine.Profiling.Profiler.BeginSample("Start Secondary");
			try
			{
				started = useable.startSecondary();
#if LOG_EQUIPMENT_START_STOP
				if (started)
				{
					UnturnedLog.info("Started secondary");
				}
#endif // LOG_EQUIPMENT_START_STOP
			}
			catch (System.Exception exception)
			{
				UnturnedLog.warn("{0} raised an exception during useable.startSecondary:", asset);
				UnturnedLog.exception(exception);
			}
			UnityEngine.Profiling.Profiler.EndSample();
			return started;
		}

		private void StopUsableSecondary()
		{
			UnityEngine.Profiling.Profiler.BeginSample("Stop Secondary");
			try
			{
				useable.stopSecondary();
#if LOG_EQUIPMENT_START_STOP
				UnturnedLog.info("Stopped secondary");
#endif // LOG_EQUIPMENT_START_STOP
			}
			catch (System.Exception exception)
			{
				UnturnedLog.warn("{0} raised an exception during useable.stopSecondary:", asset);
				UnturnedLog.exception(exception);
			}
			UnityEngine.Profiling.Profiler.EndSample();
		}

		/// <summary>
		/// (Temporarily?) separated out from simulate to try and get a better exception call stack.
		/// </summary>
		private void simulate_UseableInput(uint simulation, EAttackInputFlags inputPrimary, EAttackInputFlags inputSecondary, bool inputSteady)
		{
#if LOG_EQUIPMENT_ATTACK_INPUTS
			UnturnedLog.info($"[{simulation}] Primary: {inputPrimary} Secondary: {inputSecondary}");
#endif // LOG_EQUIPMENT_ATTACK_INPUTS

			UnityEngine.Profiling.Profiler.BeginSample("Useable");
			if (inputPrimary.HasFlag(EAttackInputFlags.Start) && HasValidUseable && IsEquipAnimationFinished)
			{
				if (!wasUsablePrimaryStarted)
				{
					wasUsablePrimaryStarted = StartUsablePrimary();
				}
			}

			if (inputPrimary.HasFlag(EAttackInputFlags.Stop) && HasValidUseable && IsEquipAnimationFinished)
			{
				if (wasUsablePrimaryStarted)
				{
					wasUsablePrimaryStarted = false;
					StopUsablePrimary();
				}
			}

			if (inputSecondary.HasFlag(EAttackInputFlags.Start) && HasValidUseable && IsEquipAnimationFinished)
			{
				if (!wasUsableSecondaryStarted)
				{
					wasUsableSecondaryStarted = StartUsableSecondary();
				}
			}

			if (inputSecondary.HasFlag(EAttackInputFlags.Stop) && HasValidUseable && IsEquipAnimationFinished)
			{
				if (wasUsableSecondaryStarted)
				{
					wasUsableSecondaryStarted = false;
					StopUsableSecondary();
				}
			}

			if (HasValidUseable && IsEquipAnimationFinished) // check isSelected again in case dequipped during start/stop
			{
				UnityEngine.Profiling.Profiler.BeginSample("Simulate");
				try
				{
					useable.simulate(simulation, inputSteady);
				}
				catch (System.Exception exception)
				{
					UnturnedLog.warn("{0} raised an exception during useable.simulate:", asset);
					UnturnedLog.exception(exception);
				}
				UnityEngine.Profiling.Profiler.EndSample();
			}

			if (Provider.isServer && HasValidUseable && IsEquipAnimationFinished) // Check again in case we dequipped during simulate.
			{
				if (asset != null && asset.shouldDeleteAtZeroQuality && quality == 0)
				{
					ItemAsset usedAsset = asset;

					// We apply deletion after simulation finished rather than immediately,
					// as damage from melee hit or whatnot might not have been dealt when
					// quality change is done.
					use();

					EffectAsset effect = usedAsset.FindDeletedAtZeroQualityEffect();
					if (effect != null)
					{
						TriggerEffectParameters effectParameters = new TriggerEffectParameters(effect);
						effectParameters.relevantDistance = EffectManager.SMALL;
						effectParameters.position = transform.position + Vector3.up;
						effectParameters.reliable = true;
						EffectManager.triggerEffect(effectParameters);
					}

					usedAsset.DeletedAtZeroQualityRewards.Grant(player);
				}
			}
			UnityEngine.Profiling.Profiler.EndSample();
		}

		/// <summary>
		/// (Temporarily?) separated out from simulate to try and get a better exception call stack.
		/// </summary>
		private void simulate_PunchInput(uint simulation, EAttackInputFlags inputPrimary, EAttackInputFlags inputSecondary)
		{
			if (inputPrimary.HasFlag(EAttackInputFlags.Start))
			{
				if (!isBusy && player.stance.stance != EPlayerStance.PRONE)
				{
					UnityEngine.Profiling.Profiler.BeginSample("Punch");
					if (simulation - lastPunch > 5)
					{
						lastPunch = simulation;

						punch(EPlayerPunch.LEFT);
					}
					UnityEngine.Profiling.Profiler.EndSample();
				}
			}

			if (inputSecondary.HasFlag(EAttackInputFlags.Start))
			{
				if (!isBusy && player.stance.stance != EPlayerStance.PRONE)
				{
					UnityEngine.Profiling.Profiler.BeginSample("Punch");
					if (simulation - lastPunch > 5)
					{
						lastPunch = simulation;

						punch(EPlayerPunch.RIGHT);
					}
					UnityEngine.Profiling.Profiler.EndSample();
				}
			}
		}

		public void simulate(uint simulation, EAttackInputFlags inputPrimary, EAttackInputFlags inputSecondary, bool inputSteady)
		{
			bool mustDequip = simulate_MustDequip();
			if (mustDequip)
			{
				if (HasValidUseable && Provider.isServer)
				{
					dequip();
				}

				return;
			}

			if (Time.realtimeSinceStartup - lastEquip < 0.1f || player.life.isDead)
			{
				return;
			}

			if (player.movement.isSafe)
			{
				if (asset == null)
				{
					if (player.movement.isSafeInfo == null || player.movement.isSafeInfo.noWeapons)
					{
						return; // no punching
					}
				}
				else
				{
					if (player.movement.isSafeInfo == null || !asset.canBeUsedInSafezone(player.movement.isSafeInfo, channel.owner.isAdmin))
					{
						inputPrimary = EAttackInputFlags.Stop;
						inputSecondary = EAttackInputFlags.Stop;
					}
				}
			}

			if (Level.info != null && Level.info.type != ELevelType.SURVIVAL)
			{
				if (asset == null)
				{
					return; // no punching in arena/horde
				}
			}

			if (player.stance.isSubmerged || player.stance.stance == EPlayerStance.SWIM)
			{
				if (asset == null)
				{
					// No punching while swimming, and delay punching slightly after finished swimming.
					lastPunch = simulation;
					return;
				}
			}

			if (player.animator.gesture == EPlayerGesture.ARREST_START)
			{
				return; // no anything when arrested
			}

			if (isTurret)
			{
				if (player.movement.getVehicle() == null || !player.movement.getVehicle().canUseTurret)
				{
					// When vehicle is dead stop shooting and aiming.
					inputPrimary = EAttackInputFlags.Stop;
					inputSecondary = EAttackInputFlags.Stop;
				}
			}

			UnityEngine.Profiling.Profiler.BeginSample("Input");
			if (HasValidUseable)
			{
				simulate_UseableInput(simulation, inputPrimary, inputSecondary, inputSteady);
			}
			else
			{
				simulate_PunchInput(simulation, inputPrimary, inputSecondary);
			}
			UnityEngine.Profiling.Profiler.EndSample();
		}

		public void tock(uint clock)
		{
			if (HasValidUseable)
			{
				if (IsEquipAnimationFinished)
				{
					try
					{
						useable.tock(clock);
					}
					catch (System.Exception exception)
					{
						UnturnedLog.warn("{0} raised an exception during tock.tock:", asset);
						UnturnedLog.exception(exception);
					}
				}
			}
		}

		internal void updateVision()
		{
			if (hasVision && player.clothing.glassesState != null && player.clothing.glassesState.Length > 0 && player.clothing.glassesState[0] != 0)
			{
				if (player.clothing.glassesAsset.vision == ELightingVision.HEADLAMP)
				{
					player.enableHeadlamp(player.clothing.glassesAsset.lightConfig);

					if (channel.IsLocalPlayer && !player.look.isSingleRenderScopeVisionAppliedToLighting)
					{
						LevelLighting.vision = ELightingVision.NONE;

						LevelLighting.updateLighting();
						LevelLighting.ForceRefreshForLatestViewer();
						PlayerLifeUI.updateGrayscale();
					}
				}
				else
				{
					player.disableHeadlamp();

					if (channel.IsLocalPlayer && !player.look.isSingleRenderScopeVisionAppliedToLighting)
					{
						ELightingVision lightingVision = player.clothing.glassesAsset.vision;
						if (player.look.perspective != EPlayerPerspective.FIRST && !player.clothing.glassesAsset.isNightvisionAllowedInThirdPerson)
						{
							lightingVision = ELightingVision.NONE;
						}

						LevelLighting.vision = lightingVision;
						LevelLighting.nightvisionColor = player.clothing.glassesAsset.nightvisionColor;
						LevelLighting.nightvisionFogIntensity = player.clothing.glassesAsset.nightvisionFogIntensity;

						LevelLighting.updateLighting();
						LevelLighting.ForceRefreshForLatestViewer();
						PlayerLifeUI.updateGrayscale();
					}
				}

				player.updateGlassesLights(true);
			}
			else
			{
				player.disableHeadlamp();

				if (channel.IsLocalPlayer && !player.look.isSingleRenderScopeVisionAppliedToLighting)
				{
					LevelLighting.vision = ELightingVision.NONE;

					LevelLighting.updateLighting();
					LevelLighting.ForceRefreshForLatestViewer();
					PlayerLifeUI.updateGrayscale();
				}

				player.updateGlassesLights(false);
			}
		}

		private void onVisionUpdated(bool isViewing)
		{
			if (isViewing)
			{
				arePrimaryAndSecondaryInputsReversedByHallucination = Random.value < 0.25f;
			}
			else
			{
				arePrimaryAndSecondaryInputsReversedByHallucination = false;
			}
		}

		private void onPerspectiveUpdated(EPlayerPerspective perspective)
		{
			if (hasVision)
			{
				updateVision();
			}
		}

		private void onGlassesUpdated(ushort id, byte quality, byte[] state)
		{
			hasVision = id != 0 && player.clothing.glassesAsset != null && player.clothing.glassesAsset.vision != ELightingVision.NONE;

			updateVision();
		}

		private void OnVisualToggleChanged(PlayerClothing sender)
		{
			if (hasVision)
			{
				updateVision();
			}
		}

		private void onLifeUpdated(bool isDead)
		{
			if (isDead)
			{
				bool loseWeapons = player.life.wasPvPDeath ? Provider.modeConfigData.Players.Lose_Weapons_PvP : Provider.modeConfigData.Players.Lose_Weapons_PvE;

				if (loseWeapons)
				{
					for (byte slot = 0; slot < PlayerInventory.SLOTS; slot++)
					{
						updateSlot(slot, 0, new byte[0]);
					}
				}

				if (Provider.isServer)
				{
					dequip();
				}

				isBusy = false;
				canEquip = true;

				_equippedPage = 255;
				_equipped_x = 255;
				_equipped_y = 255;
			}
		}

		/// <summary>
		/// Allow UI to process input [0, 9] key press when cursor is visible.
		/// </summary>
		private void bindHotkey(byte button)
		{
			if (button < PlayerInventory.SLOTS)
			{
				// Cannot bind #1 or #2 key to an item because they are reserved for primary/secondary weapon.
				return;
			}

			// Nelson 2023-08-02: previously this only checked InventoryUI.active, but that can be true while the
			// dashboard is inactive, so we need to check both. (public issue #4024)
			if (!PlayerDashboardUI.active || !PlayerDashboardInventoryUI.active)
			{
				// Not in the inventory menu.
				return;
			}

			byte hotkeyIndex = (byte) (button - 2);
			if (PlayerDashboardInventoryUI.selectedPage >= PlayerInventory.SLOTS && PlayerDashboardInventoryUI.selectedPage < PlayerInventory.STORAGE) // we are binding a hotkey
			{
				if (ItemTool.checkUseable(PlayerDashboardInventoryUI.selectedPage, PlayerDashboardInventoryUI.selectedJar.item.id))
				{
					HotkeyInfo hotkeyInfo = hotkeys[hotkeyIndex];

					hotkeyInfo.id = PlayerDashboardInventoryUI.selectedJar.item.id;
					hotkeyInfo.page = PlayerDashboardInventoryUI.selectedPage;
					hotkeyInfo.x = PlayerDashboardInventoryUI.selected_x;
					hotkeyInfo.y = PlayerDashboardInventoryUI.selected_y;
					PlayerDashboardInventoryUI.closeSelection();

					ClearDuplicateHotkeys(hotkeyIndex);

					onHotkeysUpdated?.Invoke();
				}
			}
			else if (PlayerDashboardInventoryUI.selectedPage == 255) // we are clearing a hotkey
			{
				HotkeyInfo hotkeyInfo = hotkeys[hotkeyIndex];

				hotkeyInfo.id = 0;
				hotkeyInfo.page = 255;
				hotkeyInfo.x = 255;
				hotkeyInfo.y = 255;

				onHotkeysUpdated?.Invoke();
			}
		}

		/// <summary>
		/// Process input [0, 9] key press.
		/// </summary>
		private void hotkey(byte button)
		{
			if (PlayerUI.window.showCursor)
			{
				bindHotkey(button);
			}
			else if (!isBusy)
			{
				if (button < PlayerInventory.SLOTS) // Primary or secondary weapon
				{
					ItemJar jar = player.inventory.getItem(button, 0); // use button (0-1) as page to get the slot items

					if (jar != null)
					{
						equip(button, jar.x, jar.y);
					}
					else if (HasValidUseable && IsEquipAnimationFinished)
					{
						dequip();
					}
				}
				else
				{
					byte hotkeyIndex = (byte) (button - 2);
					HotkeyInfo hotkeyInfo = hotkeys[hotkeyIndex];

					if (hotkeyInfo.id != 0)
					{
						equip(hotkeyInfo.page, hotkeyInfo.x, hotkeyInfo.y);
					}
					else if (HasValidUseable && IsEquipAnimationFinished)
					{
						dequip();
					}
				}
			}
		}

		/// <summary>
		/// If equipped item is bound to a hotkey, return the button [0, 9] associated.
		/// Otherwise, return -1.
		/// </summary>
		internal int FindEquippedHotkeyButton()
		{
			if (asset != null)
			{
				if (equippedPage < 2)
				{
					return equippedPage;
				}

				for (int hotkeyIndex = 0; hotkeyIndex < _hotkeys.Length; ++hotkeyIndex)
				{
					HotkeyInfo info = _hotkeys[hotkeyIndex];
					if (info.id != 0 && info.id == asset.id && info.page == equippedPage && info.x == equipped_x && info.y == equipped_y)
					{
						return hotkeyIndex + 2;
					}
				}
			}

			return -1;
		}

		private void Update()
		{
			if (channel.IsLocalPlayer)
			{
				bool isPrimaryHeld;
				bool isSecondaryHeld;

				if (!PlayerUI.window.showCursor && !PlayerDashboardInventoryUI.WasEventConsumed && !player.workzone.isBuilding && (player.movement.getVehicle() == null || player.look.perspective == EPlayerPerspective.FIRST))
				{
					KeyCode primaryKeyCode = ControlsSettings.primary;
					KeyCode secondaryKeyCode = ControlsSettings.secondary;
					if (arePrimaryAndSecondaryInputsReversedByHallucination)
					{
						KeyCode temp = primaryKeyCode;
						primaryKeyCode = secondaryKeyCode;
						secondaryKeyCode = temp;
					}

					isPrimaryHeld = InputEx.GetKey(primaryKeyCode);

					if (ControlsSettings.aiming == EControlMode.TOGGLE && asset != null && (asset.type == EItemType.GUN || asset.type == EItemType.OPTIC))
					{
						if (InputEx.GetKeyDown(secondaryKeyCode))
						{
							localWantsToAim = !localWantsToAim;
						}
						isSecondaryHeld = localWantsToAim;
					}
					else
					{
						isSecondaryHeld = InputEx.GetKey(secondaryKeyCode);
					}

					// Disable shooting/attacking temporarily if inbound lag switch is activated.
					if (PlayerManager.IsClientUnderFakeLagPenalty)
					{
						isPrimaryHeld = false;
						isSecondaryHeld = false;
						localWantsToAim = false;
					}

					// Nelson 2023-10-06: hacky workaround. Client creates equipable at a different time than server,
					// so server may think we can begin using the item before the client does. In that case, the raycast
					// inputs don't line up and we can't deal damage. To avoid this we pretend the client isn't pressing
					// attack until it thinks it would be able to start using it.
					if (HasValidUseable && !IsEquipAnimationFinished)
					{
						isPrimaryHeld = false;
						isSecondaryHeld = false;
						localWantsToAim = false;
					}
				}
				else
				{
					isPrimaryHeld = false;
					isSecondaryHeld = false;
					localWantsToAim = false;
				}

				if (isPrimaryHeld != localWasPrimaryHeldLastFrame)
				{
					if (isPrimaryHeld)
					{
						localWasPrimaryPressedBetweenSimulationFrames = true;
					}
					else
					{
						localWasPrimaryReleasedBetweenSimulationFrames = true;
					}
				}
				localWasPrimaryHeldLastFrame = isPrimaryHeld;
				if (isSecondaryHeld != localWasSecondaryHeldLastFrame)
				{
					if (isSecondaryHeld)
					{
						localWasSecondaryPressedBetweenSimulationFrames = true;
					}
					else
					{
						localWasSecondaryReleasedBetweenSimulationFrames = true;
					}
				}
				localWasSecondaryHeldLastFrame = isSecondaryHeld;
			}

			wasTryingToSelect = false;

			if (channel.IsLocalPlayer)
			{
				if (!PlayerUI.window.showCursor && !player.workzone.isBuilding)
				{
					if (InputEx.GetKeyDown(ControlsSettings.vision) && hasVision)
					{
						SendToggleVisionRequest.Invoke(GetNetId(), ENetReliability.Unreliable);
					}

					if (InputEx.GetKeyDown(ControlsSettings.dequip))
					{
						if (HasValidUseable && !isBusy && IsEquipAnimationFinished)
						{
							dequip();
						}
					}
				}

				for (byte hotkeyIndex = 0; hotkeyIndex < ControlsSettings.NUM_ITEM_HOTBAR_KEYS; ++hotkeyIndex)
				{
					if (InputEx.GetKeyDown(ControlsSettings.getEquipmentHotbarKeyCode(hotkeyIndex)))
					{
						hotkey(hotkeyIndex);
					}
				}
			}

			if (HasValidUseable)
			{
				try
				{
					useable.tick();
				}
				catch (System.Exception exception)
				{
					UnturnedLog.warn("{0} raised an exception during Update.tick:", asset);
					UnturnedLog.exception(exception);
				}
			}
		}

		internal void InitializePlayer()
		{
			hasVision = player.clothing.glassesAsset != null && player.clothing.glassesAsset.vision != ELightingVision.NONE;
			updateVision();

			thirdSlots = new Transform[PlayerInventory.SLOTS];
			thirdSkinneds = new bool[PlayerInventory.SLOTS];
			tempThirdMeshes = new List<Mesh>[PlayerInventory.SLOTS];
			for (int index = 0; index < tempThirdMeshes.Length; index++)
			{
				tempThirdMeshes[index] = new List<Mesh>(4);
			}
			tempThirdMaterials = new Material[PlayerInventory.SLOTS];
			thirdMythics = new MythicalEffectController[PlayerInventory.SLOTS];

			tempThirdMesh = new List<Mesh>(4);

			if (channel.IsLocalPlayer && player.character != null)
			{
				tempFirstMesh = new List<Mesh>(4);
				tempCharacterMesh = new List<Mesh>(4);

				characterSlots = new Transform[PlayerInventory.SLOTS];
				characterSkinneds = new bool[PlayerInventory.SLOTS];
				tempCharacterMeshes = new List<Mesh>[PlayerInventory.SLOTS];
				for (int index = 0; index < tempCharacterMeshes.Length; index++)
				{
					tempCharacterMeshes[index] = new List<Mesh>(4);
				}
				tempCharacterMaterials = new Material[PlayerInventory.SLOTS];
				characterMythics = new MythicalEffectController[PlayerInventory.SLOTS];
			}

			arePrimaryAndSecondaryInputsReversedByHallucination = false;

			_equippedPage = 255;
			_equipped_x = 255;
			_equipped_y = 255;

			isBusy = false;
			canEquip = true;

			if (player.third != null)
			{
				_thirdPrimaryMeleeSlot = player.animator.thirdSkeleton.Find("Spine").Find("Primary_Melee");
				_thirdPrimaryLargeGunSlot = player.animator.thirdSkeleton.Find("Spine").Find("Primary_Large_Gun");
				_thirdPrimarySmallGunSlot = player.animator.thirdSkeleton.Find("Spine").Find("Primary_Small_Gun");
				_thirdSecondaryMeleeSlot = player.animator.thirdSkeleton.Find("Right_Hip").Find("Right_Leg").Find("Secondary_Melee");
				_thirdSecondaryGunSlot = player.animator.thirdSkeleton.Find("Right_Hip").Find("Right_Leg").Find("Secondary_Gun");
			}

			if (channel.IsLocalPlayer)
			{
				_characterPrimaryMeleeSlot = player.character.Find("Skeleton").Find("Spine").Find("Primary_Melee");
				_characterPrimaryLargeGunSlot = player.character.Find("Skeleton").Find("Spine").Find("Primary_Large_Gun");
				_characterPrimarySmallGunSlot = player.character.Find("Skeleton").Find("Spine").Find("Primary_Small_Gun");
				_characterSecondaryMeleeSlot = player.character.Find("Skeleton").Find("Right_Hip").Find("Right_Leg").Find("Secondary_Melee");
				_characterSecondaryGunSlot = player.character.Find("Skeleton").Find("Right_Hip").Find("Right_Leg").Find("Secondary_Gun");
			}

			if (player.first != null)
			{
				_firstSpine = player.animator.firstSkeleton.Find("Spine");
				_firstSpineHook = _firstSpine.Find("Spine_Hook");
				Debug.Assert(_firstSpineHook != null, $"Missing Spine_Hook transform under {_firstSpine.GetSceneHierarchyPath()}", player.first);
				_firstLeftHook = _firstSpine.Find("Left_Shoulder").Find("Left_Arm").Find("Left_Hand").Find("Left_Hook");
				_firstRightHook = _firstSpine.Find("Right_Shoulder").Find("Right_Arm").Find("Right_Hand").Find("Right_Hook");
			}

			if (player.third != null)
			{
				_thirdSpine = player.animator.thirdSkeleton.Find("Spine");
				_thirdSpineHook = _thirdSpine.Find("Spine_Hook");
				Debug.Assert(_thirdSpineHook != null, $"Missing Spine_Hook transform under {_thirdSpine.GetSceneHierarchyPath()}", player.third);
				_thirdLeftHook = _thirdSpine.Find("Left_Shoulder").Find("Left_Arm").Find("Left_Hand").Find("Left_Hook");
				_thirdRightHook = _thirdSpine.Find("Right_Shoulder").Find("Right_Arm").Find("Right_Hand").Find("Right_Hook");
			}

			if (channel.IsLocalPlayer && player.character != null)
			{
				_characterSpine = player.character.Find("Skeleton/Spine");
				_characterSpineHook = _characterSpine.Find("Spine_Hook");
				Debug.Assert(_characterSpineHook != null, $"Missing Spine_Hook transform under {_characterSpine.GetSceneHierarchyPath()}", player.character);
				_characterLeftHook = _characterSpine.Find("Left_Shoulder").Find("Left_Arm").Find("Left_Hand").Find("Left_Hook");
				_characterRightHook = _characterSpine.Find("Right_Shoulder").Find("Right_Arm").Find("Right_Hand").Find("Right_Hook");
			}

			if (channel.IsLocalPlayer || Provider.isServer)
			{
				player.life.onVisionUpdated += onVisionUpdated;
			}

			player.clothing.onGlassesUpdated += onGlassesUpdated;
			player.clothing.VisualToggleChanged += OnVisualToggleChanged;

			if (channel.IsLocalPlayer)
			{
				_hotkeys = new HotkeyInfo[8];
				for (byte hotkeyIndex = 0; hotkeyIndex < hotkeys.Length; hotkeyIndex++)
				{
					hotkeys[hotkeyIndex] = new HotkeyInfo();
				}

				load();

				player.look.onPerspectiveUpdated += onPerspectiveUpdated;
			}

			player.life.onLifeUpdated += onLifeUpdated;
		}

		/// <summary>
		/// Called by input when preparing for simulation frame.
		/// </summary>
		internal void CaptureAttackInputs(out EAttackInputFlags primaryAttack, out EAttackInputFlags secondaryAttack)
		{
			primaryAttack = EAttackInputFlags.None;
			secondaryAttack = EAttackInputFlags.None;

			if (localWasPrimaryPressedBetweenSimulationFrames || localWasPrimaryHeldLastFrame)
			{
				primaryAttack |= EAttackInputFlags.Start;
			}

			if (localWasPrimaryReleasedBetweenSimulationFrames)
			{
				primaryAttack |= EAttackInputFlags.Stop;
			}

			if (localWasSecondaryPressedBetweenSimulationFrames || localWasSecondaryHeldLastFrame)
			{
				secondaryAttack |= EAttackInputFlags.Start;
			}

			if (localWasSecondaryReleasedBetweenSimulationFrames)
			{
				secondaryAttack |= EAttackInputFlags.Stop;
			}

			localWasPrimaryPressedBetweenSimulationFrames = false;
			localWasPrimaryReleasedBetweenSimulationFrames = false;
			localWasSecondaryPressedBetweenSimulationFrames = false;
			localWasSecondaryReleasedBetweenSimulationFrames = false;
		}

		private IEnumerable<UseableEventHook> EnumerateEventComponents()
		{
			if (firstEventComponent)
				yield return firstEventComponent;

			if (thirdEventComponent)
				yield return thirdEventComponent;

			if (characterEventComponent)
				yield return characterEventComponent;
		}

		private void OnDestroy()
		{
			if (useable != null)
			{
				try
				{
					useable.dequip();
				}
				catch (System.Exception exception)
				{
					UnturnedLog.warn("{0} raised an exception during OnDestroy.dequip:", asset);
					UnturnedLog.exception(exception);
				}

				_useable.ReleaseNetId();
			}

			if (channel.IsLocalPlayer)
			{
				save();
			}
		}

		private string GetItemHotkeysFilePath()
		{
			if (Provider.clientTransport != null)
			{
				if (Provider.CurrentServerConnectParameters.steamId.IsValid())
				{
					return "/Worlds/Hotkeys/Equip_" + Provider.CurrentServerConnectParameters.steamId + "_" + Provider.map + "_" + Characters.selected + ".dat";
				}
				else
				{
					uint ip = Provider.CurrentServerConnectParameters.address.value;
					ushort port = Provider.CurrentServerConnectParameters.connectionPort;
					return "/Worlds/Hotkeys/Equip_" + ip + "_" + port + "_" + Provider.map + "_" + Characters.selected + ".dat";
				}
			}
			else
			{
				return "/Worlds/Hotkeys/Equip_" + Provider.serverID + "_" + Provider.map + ".dat";
			}
		}

		private void LogItemHotkeys(string message)
		{
			UnturnedLog.info(message);
		}

		private void load()
		{
			string hotkeysFilePath = GetItemHotkeysFilePath();

			if (ReadWrite.fileExists(hotkeysFilePath, false))
			{
				Block block = ReadWrite.readBlock(hotkeysFilePath, false, 0);
				block.readByte(); // version #

				for (byte hotkeyIndex = 0; hotkeyIndex < hotkeys.Length; hotkeyIndex++)
				{
					HotkeyInfo hotkeyInfo = hotkeys[hotkeyIndex];
					hotkeyInfo.id = block.readUInt16();
					hotkeyInfo.page = block.readByte();
					hotkeyInfo.x = block.readByte();
					hotkeyInfo.y = block.readByte();
				}

				LogItemHotkeys("Loaded item hotkeys");
			}
			else
			{
				LogItemHotkeys("No item hotkeys to load");
			}
		}

		private void save()
		{
			if (hotkeys == null)
			{
				LogItemHotkeys("Ignoring request to save item hotkeys because they were not loaded yet");
				return;
			}

			bool hasHotkey = false;
			for (byte hotkeyIndex = 0; hotkeyIndex < hotkeys.Length; hotkeyIndex++)
			{
				HotkeyInfo hotkeyInfo = hotkeys[hotkeyIndex];

				if (hotkeyInfo.id != 0 || (hotkeyInfo.page != 255 && hotkeyInfo.x != 255 && hotkeyInfo.y != 255))
				{
					hasHotkey = true;
					break;
				}
			}

			string hotkeysFilePath = GetItemHotkeysFilePath();

			if (!hasHotkey)
			{
				if (ReadWrite.fileExists(hotkeysFilePath, false))
				{
					LogItemHotkeys("No item hotkeys to save, deleting old item hotkeys file");
					ReadWrite.deleteFile(hotkeysFilePath, false);
				}
				else
				{
					LogItemHotkeys("No item hotkeys to save");
				}
				return;
			}

			Block block = new Block();
			block.writeByte(SAVEDATA_VERSION);

			for (byte hotkeyIndex = 0; hotkeyIndex < hotkeys.Length; hotkeyIndex++)
			{
				HotkeyInfo hotkeyInfo = hotkeys[hotkeyIndex];
				block.writeUInt16(hotkeyInfo.id);
				block.writeByte(hotkeyInfo.page);
				block.writeByte(hotkeyInfo.x);
				block.writeByte(hotkeyInfo.y);
			}

			ReadWrite.writeBlock(hotkeysFilePath, false, block);
			LogItemHotkeys("Saved item hotkeys");
		}

		[System.Obsolete("Renamed to HasValidUseable")]
		public bool isSelected { get => HasValidUseable; }

		[System.Obsolete("Renamed to IsEquipAnimationFinished")]
		public bool isEquipped { get => IsEquipAnimationFinished; }
	}
}
