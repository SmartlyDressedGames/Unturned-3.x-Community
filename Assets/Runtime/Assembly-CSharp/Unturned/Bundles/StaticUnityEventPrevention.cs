////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace SDG.Unturned
{
	public static class StaticUnityEventPrevention
	{
		/// <summary>
		/// Check gameObject (and children) components for any unity events calling static methods (exploitable).
		/// </summary>
		/// <returns>True if nothing was found.</returns>
		public static bool Validate(GameObject gameObject)
		{
			bool result = true;
			components.Clear();
			gameObject.GetComponentsInChildren(/*includeInactive*/ true, components);

			foreach (MonoBehaviour component in components)
			{
				if (component == null)
				{
					// Can be null if component's script is missing.
					continue;
				}

				System.Type componentType = component.GetType();
				if (typeof(EventTrigger).IsAssignableFrom(componentType))
				{
					EventTrigger eventTrigger = component as EventTrigger;
					if (eventTrigger != null)
					{
						bool wasSafe = ValidateEventTrigger(eventTrigger);
						result &= wasSafe;
						continue;
					}
				}

				TypeInfo componentInfo = GetTypeInfo(componentType);

				if (componentInfo.unityEventFields == null)
					continue;
				
				foreach (FieldInfo field in componentInfo.unityEventFields)
				{
					UnityEventBase unityEvent = field.GetValue(component) as UnityEventBase;
					if (unityEvent == null)
						continue;

					for (int index = 0; index < unityEvent.GetPersistentEventCount(); ++index)
					{
						Object target = unityEvent.GetPersistentTarget(index);
						string method = unityEvent.GetPersistentMethodName(index);
						if (target == null && !string.IsNullOrEmpty(method))
						{
							// We can only log a helpful message if we have some context of which object this is.
							if (Assets.shouldValidateAssets && (Assets.currentAsset != null || Assets.currentMasterBundle != null))
							{
								UnturnedLog.warn($"Found call to method \"{method}\" without target in {component.GetSceneHierarchyPath()} {componentType} {field.Name} (Asset: {Assets.currentAsset?.FriendlyNameWithFriendlyType} Bundle: {Assets.currentMasterBundle?.assetBundleName}), deactivating (may be attempting to call a static method, or target is unassigned)");
							}
							unityEvent.SetPersistentListenerState(index, UnityEventCallState.Off);
							result = false;
						}
					}
				}
			}

			return result;
		}

		private static bool ValidateEventTrigger(EventTrigger eventTrigger)
		{
			bool result = true;
			foreach (EventTrigger.Entry entry in eventTrigger.triggers)
			{
				UnityEventBase unityEvent = entry.callback;
				if (unityEvent == null)
					continue;

				for (int index = 0; index < unityEvent.GetPersistentEventCount(); ++index)
				{
					Object target = unityEvent.GetPersistentTarget(index);
					string method = unityEvent.GetPersistentMethodName(index);
					if (target == null && !string.IsNullOrEmpty(method))
					{
						// We can only log a helpful message if we have some context of which object this is.
						if (Assets.shouldValidateAssets && (Assets.currentAsset != null || Assets.currentMasterBundle != null))
						{
							UnturnedLog.warn($"Found call to method \"{method}\" without target in {eventTrigger.GetSceneHierarchyPath()} EventTrigger {entry.eventID} (Asset: {Assets.currentAsset?.FriendlyNameWithFriendlyType} Bundle: {Assets.currentMasterBundle?.assetBundleName}), deactivating (may be attempting to call a static method, or target is unassigned)");
						}
						unityEvent.SetPersistentListenerState(index, UnityEventCallState.Off);
						result = false;
					}
				}
			}
			return result;
		}

		private static List<MonoBehaviour> components = new List<MonoBehaviour>();

		class TypeInfo
		{
			public FieldInfo[] unityEventFields;
		}
		private static Dictionary<System.Type, TypeInfo> cachedTypeInfo = new Dictionary<System.Type, TypeInfo>();

		private static TypeInfo GetTypeInfo(System.Type type)
		{
			TypeInfo info;
			if (cachedTypeInfo.TryGetValue(type, out info)) {
				return info;
			}

			info = new TypeInfo();
			tempFields.Clear();
			foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
			{
				// Some classes (e.g., uGUI Button) subclass UnityEvent.
				if (typeof(UnityEventBase).IsAssignableFrom(field.FieldType))
				{
					tempFields.Add(field);
				}
			}

			if (tempFields.Count > 0)
			{
				info.unityEventFields = tempFields.ToArray();
			}

			cachedTypeInfo.Add(type, info);
			return info;
		}

		private static List<FieldInfo> tempFields = new List<FieldInfo>();
	}
}
