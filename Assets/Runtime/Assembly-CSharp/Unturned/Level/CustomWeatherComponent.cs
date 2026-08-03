////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SDG.Unturned
{
	public class CustomWeatherComponent : WeatherComponentBase
	{
		public WeatherAsset customAsset;

		public override void InitializeWeather()
		{
			base.InitializeWeather();

			customAsset = asset as WeatherAsset;

			overrideFog = customAsset.overrideFog;
			overrideAtmosphericFog = customAsset.overrideAtmosphericFog;
			overrideCloudColors = customAsset.overrideCloudColors;

			shadowStrengthMultiplier = customAsset.shadowStrengthMultiplier;
			fogBlendExponent = customAsset.fogBlendExponent;
			cloudBlendExponent = customAsset.cloudBlendExponent;
			windMain = customAsset.windMain;

#if !DEDICATED_SERVER
			if (Dedicator.IsDedicatedServer)
				return;

			if (customAsset.effects != null)
			{
				effects = new List<EffectInstance>(customAsset.effects.Length);
				foreach (WeatherAsset.Effect effectSettings in customAsset.effects)
				{
					if (effectSettings.prefab.isValid)
					{
						StartCoroutine(AsyncLoadEffect(effectSettings));
					}
				}
			}
#endif // !DEDICATED_SERVER
		}

		public override void UpdateWeather()
		{
#if !DEDICATED_SERVER
			if (effects != null)
			{
				foreach (EffectInstance effect in effects)
				{
					if (effect == null)
						continue;

					ParticleSystem.EmissionModule emission = effect.particleSystem.emission;
					emission.rateOverTimeMultiplier = effect.rateOverTime * Mathf.Pow(EffectBlendAlpha, effect.asset.emissionExponent);

					if (effect.asset.rotateYawWithWind)
					{
						effect.particleSystem.transform.rotation = Quaternion.Slerp(effect.particleSystem.transform.rotation, Quaternion.Euler(effect.asset.pitch, LevelLighting.wind, 0), 0.5f * Time.deltaTime);
					}
				}
			}
#endif // !DEDICATED_SERVER
		}

		public override void UpdateLightingTime(int blendKey, int currentKey, float timeAlpha)
		{
			LightingInfo levelBlendTo = LevelLighting.times[currentKey];
			LightingInfo levelBlendFrom = blendKey == -1 ? levelBlendTo : LevelLighting.times[blendKey];

			WeatherAsset.TimeValues blendFrom;
			WeatherAsset.TimeValues blendTo;
			customAsset.getTimeValues(blendKey, currentKey, out blendFrom, out blendTo);

			Color fromFogColor = blendFrom.fogColor.Evaluate(levelBlendFrom);
			Color toFogColor = blendTo.fogColor.Evaluate(levelBlendTo);
			fogColor = Color.Lerp(fromFogColor, toFogColor, timeAlpha);
			fogDensity = Mathf.Lerp(blendFrom.fogDensity, blendTo.fogDensity, timeAlpha);

			Color fromCloudColor = blendFrom.cloudColor.Evaluate(levelBlendFrom);
			Color toCloudColor = blendTo.cloudColor.Evaluate(levelBlendTo);
			cloudColor = Color.Lerp(fromCloudColor, toCloudColor, timeAlpha);

			Color fromCloudRimColor = blendFrom.cloudRimColor.Evaluate(levelBlendFrom);
			Color toCloudRimColor = blendTo.cloudRimColor.Evaluate(levelBlendTo);
			cloudRimColor = Color.Lerp(fromCloudRimColor, toCloudRimColor, timeAlpha);

			brightnessMultiplier = Mathf.Lerp(blendFrom.brightnessMultiplier, blendTo.brightnessMultiplier, timeAlpha);
		}

		public override void PreDestroyWeather()
		{
			base.PreDestroyWeather();

#if !DEDICATED_SERVER
			if (effects != null)
			{
				foreach (EffectInstance effect in effects)
				{
					Destroy(effect.particleSystem.gameObject);
				}
				effects = null;
			}
#endif // !DEDICATED_SERVER
		}

		private void Update()
		{
			if (customAsset == null || !Provider.isServer)
				return;

			float deltaSeconds = Time.deltaTime * globalBlendAlpha;
			staminaBuffer += customAsset.staminaPerSecond * deltaSeconds;
			healthBuffer += customAsset.healthPerSecond * deltaSeconds;
			foodBuffer += customAsset.foodPerSecond * deltaSeconds;
			waterBuffer += customAsset.waterPerSecond * deltaSeconds;
			virusBuffer += customAsset.virusPerSecond * deltaSeconds;

			int staminaInt = MathfEx.TruncateToInt(staminaBuffer);
			if (staminaInt != 0)
			{
				staminaBuffer -= staminaInt;

				foreach (Player player in EnumerateMaskedPlayers())
				{
					player.life.serverModifyStamina(staminaInt);
				}
			}

			int healthInt = MathfEx.TruncateToInt(healthBuffer);
			if (healthInt != 0)
			{
				healthBuffer -= healthInt;

				foreach (Player player in EnumerateMaskedPlayers())
				{
					player.life.serverModifyHealth(healthInt);
				}
			}

			int foodInt = MathfEx.TruncateToInt(foodBuffer);
			if (foodInt != 0)
			{
				foodBuffer -= foodInt;

				foreach (Player player in EnumerateMaskedPlayers())
				{
					player.life.serverModifyFood(foodInt);
				}
			}

			int waterInt = MathfEx.TruncateToInt(waterBuffer);
			if (waterInt != 0)
			{
				waterBuffer -= waterInt;

				foreach (Player player in EnumerateMaskedPlayers())
				{
					player.life.serverModifyWater(waterInt);
				}
			}

			int virusInt = MathfEx.TruncateToInt(virusBuffer);
			if (virusInt != 0)
			{
				virusBuffer -= virusInt;

				foreach (Player player in EnumerateMaskedPlayers())
				{
					player.life.serverModifyVirus(virusInt);
				}
			}
		}

#if !DEDICATED_SERVER
		private IEnumerator AsyncLoadEffect(WeatherAsset.Effect effectSettings)
		{
			AssetBundleRequest request = effectSettings.prefab.LoadAssetAsync();
			if (request == null)
				yield break;

			yield return request;

			GameObject prefabTemplate = request.asset as GameObject;
			if (prefabTemplate == null)
				yield break;

			StaticUnityEventPrevention.Validate(prefabTemplate);

			GameObject prefabInstance = Instantiate(prefabTemplate, Vector3.zero, Quaternion.identity);
			prefabInstance.name = string.Format("{0}_Effect_{1}", asset.name, prefabTemplate.name);
			ParticleSystem particleSystem = prefabInstance.GetComponentInChildren<ParticleSystem>();
			if (particleSystem == null)
			{
				Assets.ReportError(asset, "effect {0} missing particle system", prefabTemplate.name);
				Destroy(prefabInstance);
				yield break;
			}

			if (effectSettings.translateWithView)
			{
				Transform effectTransform = prefabInstance.transform;
				effectTransform.parent = transform;
				effectTransform.localPosition = Vector3.zero;
				effectTransform.localRotation = Quaternion.identity;
			}

			EffectInstance effectInstance = effects.AddDefaulted();
			effectInstance.asset = effectSettings;
			effectInstance.particleSystem = particleSystem;

			ParticleSystem.EmissionModule emission = particleSystem.emission;
			effectInstance.rateOverTime = emission.rateOverTimeMultiplier;
			emission.rateOverTimeMultiplier = 0.0f;

			particleSystem.Play();
		}
#endif // !DEDICATED_SERVER

		private float staminaBuffer;
		private float healthBuffer;
		private float foodBuffer;
		private float waterBuffer;
		private float virusBuffer;

#if !DEDICATED_SERVER
		private class EffectInstance
		{
			public ParticleSystem particleSystem;
			public WeatherAsset.Effect asset;
			public float rateOverTime;
		}

		private List<EffectInstance> effects;
#endif // !DEDICATED_SERVER
	}
}
