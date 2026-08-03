////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using SDG.NetTransport;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	public class LightningWeatherComponent : MonoBehaviour
	{
		public NetId GetNetId()
		{
			return netId;
		}

		public WeatherComponentBase weatherComponent;

		private static ClientInstanceMethod<Vector3> SendLightningStrike = ClientInstanceMethod<Vector3>.Get(typeof(LightningWeatherComponent), nameof(ReceiveLightningStrike));
		[SteamCall(ESteamCallValidation.ONLY_FROM_SERVER)]
		public void ReceiveLightningStrike(Vector3 hitPosition)
		{
#if !DEDICATED_SERVER
			StartCoroutine(PlayEffect(hitPosition));

			AudioClip clipToPlay = farClip;
			if (MainCamera.instance != null)
			{
				float distance = MathfEx.HorizontalDistanceSquared(MainCamera.instance.transform.position, hitPosition);
				if (distance < 100.0f * 100.0f)
				{
					clipToPlay = nearClip;
				}
				else if (distance < 300.0f * 300.0f)
				{
					clipToPlay = mediumClip;
				}
			}

			if (clipToPlay != null)
			{
				OneShotAudioParameters audioParams = new OneShotAudioParameters(hitPosition, clipToPlay);
				audioParams.RandomizePitch(0.95f, 1.05f);
				audioParams.SetLinearRolloff(0.0f, 2048.0f);
				audioParams.Play();
			}
#endif // !DEDICATED_SERVER
		}

		private void Update()
		{
			if (!Provider.isServer || weatherComponent == null)
				return;

			if (!weatherComponent.isFullyTransitionedIn)
			{
				// No lightning strikes while fading in/out.
				return;
			}

			if (hasInitializedTimer)
			{
				timer -= Time.deltaTime;
				if (timer > 0.0f)
				{
					return;
				}
				hasInitializedTimer = false;
			}
			else
			{
				timer = Random.Range(weatherComponent.asset.minLightningInterval, weatherComponent.asset.maxLightningInterval);
				hasInitializedTimer = true;
				return;
			}

			playerPositions.Clear();
			foreach (Player player in weatherComponent.EnumerateMaskedPlayers())
			{
				if (player.life.IsAlive)
				{
					playerPositions.Add(player.transform.position);
				}
			}

			if (playerPositions.IsEmpty())
				return;

			int randomIndex = Random.Range(0, playerPositions.Count - 1);
			Vector3 playerPosition = playerPositions[randomIndex];
			Vector3 targetPosition = MathfEx.RandomPositionInCircleY(playerPosition, weatherComponent.asset.lightningTargetRadius);

			RaycastHit hit;
			bool hitAnything = Physics.Raycast(new Vector3(targetPosition.x, Level.HEIGHT, targetPosition.z), Vector3.down, out hit, Level.HEIGHT * 2.0f, RayMasks.LIGHTNING);
			Vector3 hitPosition = hitAnything ? hit.point : targetPosition;

			SendLightningStrike.Invoke(GetNetId(), ENetReliability.Reliable, Provider.GatherClientConnectionsWithinSphere(hitPosition, 600.0f), hitPosition);

			StartCoroutine(DoExplosionDamage(hitPosition));
		}

#if !DEDICATED_SERVER
		private IEnumerator AsyncLoadEffects()
		{
			MasterBundleConfig coreMasterBundle = Assets.findMasterBundleByName("core.masterbundle");
			if (coreMasterBundle == null)
			{
				UnturnedLog.warn("Lightning missing core asset bundle");
				yield break;
			}

			AssetBundleRequest request = coreMasterBundle.LoadAssetAsync<GameObject>("Effects/Weather/Lightning/LightningEffect.prefab");
			if (request != null)
			{
				yield return request;

				GameObject effectPrefab = request.asset as GameObject;
				if (effectPrefab != null)
				{
					StaticUnityEventPrevention.Validate(effectPrefab);

					effectInstance = Instantiate(effectPrefab);
					effectInstance.SetActive(false);
					lineRenderer = effectInstance.GetComponent<LineRenderer>();
				}
			}

			request = coreMasterBundle.LoadAssetAsync<AudioClip>("Effects/Weather/Lightning/thunder_lightning_strike_rumble_01.wav");
			yield return request;
			nearClip = request.asset as AudioClip;

			request = coreMasterBundle.LoadAssetAsync<AudioClip>("Effects/Weather/Lightning/thunder_lightning_strike_rumble_02.wav");
			yield return request;
			mediumClip = request.asset as AudioClip;

			request = coreMasterBundle.LoadAssetAsync<AudioClip>("Effects/Weather/Lightning/thunder_lightning_strike_rumble_04.wav");
			yield return request;
			farClip = request.asset as AudioClip;
		}

		private IEnumerator PlayEffect(Vector3 hitPosition)
		{
			if (effectInstance == null)
				yield break;

			// 1 second between audio clip start and the visual.
			yield return new WaitForSeconds(1.0f);

			Vector3 skyPosition = hitPosition;
			skyPosition.y = Level.HEIGHT;
			effectInstance.transform.position = skyPosition;

			float length = Level.HEIGHT - hitPosition.y;
			int disturbanceCount = Mathf.CeilToInt(length / 25.0f);

			// Line from hitPosition to a random point in the sky.
			Vector3[] linePositions = new Vector3[disturbanceCount + 1];
			linePositions[0] = hitPosition;
			for (int vertexIndex = 1; vertexIndex <= disturbanceCount; ++vertexIndex)
			{
				float normalizedMagnitude = vertexIndex / (float) disturbanceCount;
				Vector2 randomDisturbance = MathfEx.RandomPositionInCircle(50.0f * normalizedMagnitude);
				linePositions[vertexIndex] = hitPosition + new Vector3(randomDisturbance.x, normalizedMagnitude * length, randomDisturbance.y);
			}

			// Newer versions of Unity support recycling, but in the meantime we need to do it this way.
			lineRenderer.positionCount = linePositions.Length;
			lineRenderer.SetPositions(linePositions);

			EffectAsset lightningHit = Assets.find(LightningHitRef);
			if (lightningHit != null)
			{
				EffectManager.effect(lightningHit, hitPosition, Vector3.up);
			}

			effectInstance.SetActive(true);
			yield return new WaitForSeconds(0.1f);
			effectInstance.SetActive(false);
		}
#endif // !DEDICATED_SERVER

		private IEnumerator DoExplosionDamage(Vector3 hitPosition)
		{
			yield return new WaitForSeconds(1.0f);

			ExplosionParameters explosionParameters = new ExplosionParameters(hitPosition, 10.0f, EDeathCause.BURNING);
			explosionParameters.damageOrigin = EDamageOrigin.Lightning;
			explosionParameters.playImpactEffect = false;
			explosionParameters.playerDamage = 75.0f;
			explosionParameters.zombieDamage = 200.0f;
			explosionParameters.animalDamage = 200.0f;
			explosionParameters.barricadeDamage = 100.0f;
			explosionParameters.structureDamage = 100.0f;
			explosionParameters.vehicleDamage = 200.0f;
			explosionParameters.resourceDamage = 1000.0f;
			explosionParameters.objectDamage = 1000.0f;
			explosionParameters.launchSpeed = 50.0f;
			List<EPlayerKill> unusedKills;
			DamageTool.explode(explosionParameters, out unusedKills);
		}

		private void Start()
		{
#if !DEDICATED_SERVER
			StartCoroutine(AsyncLoadEffects());
#endif // !DEDICATED_SERVER
		}

		private void OnDestroy()
		{
#if !DEDICATED_SERVER
			if (effectInstance != null)
			{
				Destroy(effectInstance);
				effectInstance = null;
			}
#endif // !DEDICATED_SERVER

			if (!netId.IsNull())
			{
				NetIdRegistry.Release(netId);
				netId.Clear();
			}
		}

#if !DEDICATED_SERVER
		private GameObject effectInstance;
		private LineRenderer lineRenderer;
		private AudioClip nearClip;
		private AudioClip mediumClip;
		private AudioClip farClip;
#endif // !DEDICATED_SERVER

		private List<Vector3> playerPositions = new List<Vector3>();
		internal NetId netId;
		private float timer;
		private bool hasInitializedTimer;

		private static AssetReference<EffectAsset> LightningHitRef = new AssetReference<EffectAsset>("bed12ffc45694cd69217924d75e96fe9"); // (162)
	}
}
