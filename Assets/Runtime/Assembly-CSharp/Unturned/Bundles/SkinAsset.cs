////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	public class SkinAsset : Asset
	{
		protected bool _isPattern;
		public bool isPattern => _isPattern;

		protected bool _hasSight;
		public bool hasSight => _hasSight;

		protected bool _hasTactical;
		public bool hasTactical => _hasTactical;

		protected bool _hasGrip;
		public bool hasGrip => _hasGrip;

		protected bool _hasBarrel;
		public bool hasBarrel => _hasBarrel;

		protected bool _hasMagazine;
		public bool hasMagazine => _hasMagazine;

		protected Material _primarySkin;
		public Material primarySkin => _primarySkin;

		protected Dictionary<ushort, Material> _secondarySkins;
		public Dictionary<ushort, Material> secondarySkins => _secondarySkins;

		protected Material _attachmentSkin;
		public Material attachmentSkin => _attachmentSkin;

		protected Material _tertiarySkin;
		public Material tertiarySkin => _tertiarySkin;

		/// <summary>
		/// Used by dawn and dusk skins which pull per-level lighting colors.
		/// </summary>
		public ELightingTime? lightingTime { get; private set; }

		/// <summary>
		/// Note: unfortunately it appears the stupid skin system always instantiated materials, but never destroys
		/// them... will need to clean this up, but it will be tricky because the game does not hold a reference to them.
		/// </summary>
		public void SetMaterialProperties(Material instance)
		{
			if (lightingTime.HasValue && LevelLighting.times != null)
			{
				LightingInfo dawn = LevelLighting.times[(int) lightingTime];
				instance.SetVector("_SunColor", dawn.colors[(int) ELightingColor.SUN] * 1.5f);
				instance.SetVector("_RaysColor", dawn.colors[(int) ELightingColor.RAYS] * 1.5f);
				instance.SetVector("_SkyColor", dawn.colors[(int) ELightingColor.SKY_SKY]);
				instance.SetVector("_EquatorColor", dawn.colors[(int) ELightingColor.SKY_EQUATOR]);
				instance.SetVector("_GroundColor", dawn.colors[(int) ELightingColor.SKY_GROUND]);
			}
		}

		public List<Mesh> overrideMeshes;

		/// <summary>
		/// Same as ItemAsset.animations.
		/// Necessary to fix incompatibility between classic canned beans skin and newer eating animations.
		/// </summary>
		public AnimationClip[] overrideAnimations;

		public bool hasStatTrackerTransformOverride;
		public Vector3 statTrackerPosition;
		public Quaternion statTrackerRotation;

		public bool hasIconTransformOverride;
		public Vector3 iconPosition;
		public Quaternion iconRotation;

		public override EAssetType assetCategory => EAssetType.SKIN;

		public ERagdollEffect ragdollEffect
		{
			get;
			protected set;
		}

		/// <summary>
		/// If true, sets the Magazine attachment hook inactive while this skin is applied. (guns only)
		/// 
		/// Nelson 2025-03-10: Adding this to address mismatched Ace bullets with certain skins. (public issue #4923)
		/// It should be fine for vanilla guns because there shouldn't be assumptions about Magazine enable/disable,
		/// but modded guns may have different expectations (particularly with GunAttachmentEventHook).
		/// </summary>
		public bool ShouldHideMagazine
		{
			get;
			protected set;
		}

		/// <summary>
		/// Used by melee skins to override impact sound.
		/// </summary>
		internal AudioReference specialAudioOverride;

		public SkinAsset() : base()
		{ }

		public SkinAsset(bool isPattern, Material primarySkin, Dictionary<ushort, Material> secondarySkins, Material attachmentSkin, Material tertiarySkin)
		{
			_isPattern = isPattern;

			_hasSight = true;
			_hasTactical = true;
			_hasGrip = true;
			_hasBarrel = true;
			_hasMagazine = true;

			_primarySkin = primarySkin;
			_secondarySkins = secondarySkins;
			_attachmentSkin = attachmentSkin;
			_tertiarySkin = tertiarySkin;

			overrideMeshes = new List<Mesh>(0);
		}

		public override void PopulateAsset(in PopulateAssetParameters p)
		{
			base.PopulateAsset(in p);

			if (id < 2000 && !OriginAllowsVanillaLegacyId && !p.data.ContainsKey("Bypass_ID_Limit"))
			{
				throw new System.NotSupportedException("ID < 2000");
			}

			_isPattern = p.data.ContainsKey("Pattern");
			if (p.data.ContainsKey("LightingTime"))
			{
				lightingTime = p.data.ParseEnum<ELightingTime>("LightingTime");
			}
			else
			{
				lightingTime = null;
			}

			_hasSight = p.data.ContainsKey("Sight");
			_hasTactical = p.data.ContainsKey("Tactical");
			_hasGrip = p.data.ContainsKey("Grip");
			_hasBarrel = p.data.ContainsKey("Barrel");
			_hasMagazine = p.data.ContainsKey("Magazine");
			ShouldHideMagazine = p.data.ParseBool("Hide_Magazine");

			ragdollEffect = p.data.ParseEnum("Ragdoll_Effect", defaultValue: ERagdollEffect.None);
			specialAudioOverride = p.data.ReadAudioReference("SpecialAudioOverrideDef", p.bundle);

			if (!Dedicator.IsDedicatedServer)
			{
				_primarySkin = loadRequiredAsset<Material>(p.bundle, "Skin_Primary");

				_secondarySkins = new Dictionary<ushort, Material>();
				ushort secondarySkinCount = p.data.ParseUInt16("Secondary_Skins");
				for (ushort secondarySkinIndex = 0; secondarySkinIndex < secondarySkinCount; secondarySkinIndex++)
				{
					ushort secondarySkin = p.data.ParseUInt16("Secondary_" + secondarySkinIndex);

					if (!secondarySkins.ContainsKey(secondarySkin))
					{
						Material secondarySkinMaterial = loadRequiredAsset<Material>(p.bundle, "Skin_Secondary_" + secondarySkin);
						secondarySkins.Add(secondarySkin, secondarySkinMaterial);
					}
				}

				_attachmentSkin = p.bundle.load<Material>("Skin_Attachment");
				_tertiarySkin = p.bundle.load<Material>("Skin_Tertiary");

				if (attachmentSkin != null && tertiarySkin == null)
				{
					Assets.ReportError(this, "has Skin_Attachment material without a Skin_Tertiary material");
				}

				ushort overrideMeshesCount = p.data.ParseUInt16("Override_Meshes");
				overrideMeshes = new List<Mesh>(overrideMeshesCount);
				for (ushort overrideMeshIndex = 0; overrideMeshIndex < overrideMeshesCount; overrideMeshIndex++)
				{
					GameObject meshGameObject = p.bundle.load<GameObject>("Override_Mesh_" + overrideMeshIndex);
					if (meshGameObject != null)
					{
						MeshFilter meshFilter = meshGameObject.GetComponent<MeshFilter>();
						if (meshFilter != null)
						{
							if (meshFilter.sharedMesh != null)
							{
								overrideMeshes.Add(meshFilter.sharedMesh);
							}
							else
							{
								Assets.reportError("missing MeshFilter sharedMesh on " + meshGameObject.name);
							}
						}
						else
						{
							Assets.reportError("missing MeshFilter on " + meshGameObject.name);
						}

						Transform statTracker = meshGameObject.transform.Find("Stat_Tracker");
						if (statTracker != null)
						{
							hasStatTrackerTransformOverride = true;
							statTrackerPosition = statTracker.localPosition;
							statTrackerRotation = statTracker.localRotation;
						}

						Transform icon = meshGameObject.transform.Find("Icon");
						if (icon != null)
						{
							hasIconTransformOverride = true;
							iconPosition = icon.localPosition;
							iconRotation = icon.localRotation;
						}
					}
					else
					{
						Assets.reportError("missing Override_Mesh_" + overrideMeshIndex);
					}
				}

				if (p.data.ParseBool("Is_Legacy_Canned_Beans"))
				{
					GameObject animationsPrefab = p.bundle.load<GameObject>("Animations");
					Animation animation = animationsPrefab.GetComponent<Animation>();

					List<AnimationClip> tempAnimations = new List<AnimationClip>();

					bool hasNullClips = false;
					foreach (AnimationState state in animation)
					{
						AnimationClip clip = state.clip;
						if (clip == null)
						{
							hasNullClips = true;
							continue;
						}

						tempAnimations.Add(clip);
					}

					if (hasNullClips)
					{
						Assets.ReportError(this, "has invalid animation clip references");
					}

					if (tempAnimations.Count > 0)
					{
						overrideAnimations = tempAnimations.ToArray();
					}
				}
			}
		}
	}
}
