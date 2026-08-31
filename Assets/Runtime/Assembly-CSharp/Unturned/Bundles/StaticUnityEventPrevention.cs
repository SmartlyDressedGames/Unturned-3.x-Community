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

					object persistentCallGroup = m_PersistentCalls.GetValue(unityEvent);
					Debug.Assert(persistentCallGroup != null);

					for (int index = 0; index < unityEvent.GetPersistentEventCount(); ++index)
					{
						if (!ValidateUnityEvent(unityEvent, persistentCallGroup, index, out string reason))
						{
							// We can only log a helpful message if we have some context of which object this is.
							if (!string.IsNullOrEmpty(reason) && Assets.shouldValidateAssets && (Assets.currentAsset != null || Assets.currentMasterBundle != null))
							{
								UnturnedLog.warn($"Deactivating UnityEvent {component.GetSceneHierarchyPath()} {componentType} {field.Name} Reason: {reason} (Asset: {Assets.currentAsset?.FriendlyNameWithFriendlyType} Bundle: {Assets.currentMasterBundle?.assetBundleName})");
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

				object persistentCallGroup = m_PersistentCalls.GetValue(unityEvent);
				Debug.Assert(persistentCallGroup != null);

				for (int index = 0; index < unityEvent.GetPersistentEventCount(); ++index)
				{
					if (!ValidateUnityEvent(unityEvent, persistentCallGroup, index, out string reason))
					{
						// We can only log a helpful message if we have some context of which object this is.
						if (!string.IsNullOrEmpty(reason) && Assets.shouldValidateAssets && (Assets.currentAsset != null || Assets.currentMasterBundle != null))
						{
							UnturnedLog.warn($"Deactivating UnityEvent {eventTrigger.GetSceneHierarchyPath()} EventTrigger {entry.eventID} Reason: {reason} (Asset: {Assets.currentAsset?.FriendlyNameWithFriendlyType} Bundle: {Assets.currentMasterBundle?.assetBundleName})");
						}

						unityEvent.SetPersistentListenerState(index, UnityEventCallState.Off);
						result = false;
					}
				}
			}
			return result;
		}

		private static bool IsTypeAllowed(System.Type type)
		{
			return typeof(Component).IsAssignableFrom(type)
				|| type == typeof(Transform)
				|| type == typeof(GameObject)
				|| type == typeof(Material);
		}

		private static bool ValidateUnityEvent(UnityEventBase unityEvent, object persistentCallGroup, int index, out string reason)
		{
			try
			{
				Object target = unityEvent.GetPersistentTarget(index);
				if (target == null)
				{
					reason = "null target object";
					return false;
				}

				string methodName = unityEvent.GetPersistentMethodName(index);
				if (string.IsNullOrEmpty(methodName))
				{
					reason = "empty method name";
					return false;
				}

				System.Type targetActualType = target.GetType();
				if (!IsTypeAllowed(targetActualType))
				{
					// Avoid somehow PersistentCall.targetAssemblyTypeName resolving unexpected type.
					reason = $"target type {targetActualType} is not allowed (if valid, please open an issue)";
					return false;
				}

				oneArgument[0] = index;
				object persistentCall = GetListener.Invoke(persistentCallGroup, oneArgument);
				if (persistentCall == null)
				{
					reason = "null persistent call (shouldn't happen?)";
					return false;
				}

				// targetTypeName CAN be empty if target object is assigned
				string targetTypeName = m_TargetAssemblyTypeName.GetValue(persistentCall) as string;
				if (!string.IsNullOrEmpty(targetTypeName))
				{
					System.Type serializedTargetType = System.Type.GetType(targetTypeName, /*throwOnError*/ false);
					if (serializedTargetType == null)
					{
						reason = $"unable to resolve target type \"{targetTypeName}\"";
						return false;
					}

					if (!IsTypeAllowed(serializedTargetType))
					{
						reason = $"serialized target type {serializedTargetType} is not allowed (if valid, please open an issue)";
						return false;
					}
				}

				oneArgument[0] = persistentCall;
				MethodInfo targetMethod = FindMethod.Invoke(unityEvent, oneArgument) as MethodInfo;
				if (targetMethod == null)
				{
					reason = $"unable to find target method \"{methodName}\"";
					return false;
				}

				if (targetMethod.IsStatic)
				{
					reason = $"target method is static ({targetMethod})";
					return false;
				}

				reason = null;
				return true;
			}
			catch
			{
				reason = "threw an exception";
				return false;
			}
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
		private static FieldInfo m_PersistentCalls;
		private static MethodInfo GetListener;
		private static MethodInfo FindMethod;
		private static object[] oneArgument;
		private static FieldInfo m_TargetAssemblyTypeName;

		static StaticUnityEventPrevention()
		{
			m_PersistentCalls = typeof(UnityEventBase).GetField("m_PersistentCalls", BindingFlags.Instance | BindingFlags.NonPublic);
			Debug.Assert(m_PersistentCalls != null, "found m_PersistentCalls");
			System.Type persistentCallGroupType = System.Type.GetType("UnityEngine.Events.PersistentCallGroup, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null", /*throwOnError*/ true);
			Debug.Assert(persistentCallGroupType != null, "found persistentCallGroupType");
			GetListener = persistentCallGroupType.GetMethod("GetListener", new System.Type[] { typeof(int) });
			Debug.Assert(GetListener != null, "found GetListener");
			oneArgument = new object[1];
			System.Type persistentCallType = System.Type.GetType("UnityEngine.Events.PersistentCall, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null");
			Debug.Assert(persistentCallType != null, "found persistentCallType");
			m_TargetAssemblyTypeName = persistentCallType.GetField("m_TargetAssemblyTypeName", BindingFlags.Instance | BindingFlags.NonPublic);
			Debug.Assert(m_TargetAssemblyTypeName != null, "found m_TargetAssemblyTypeName");
			FindMethod = typeof(UnityEventBase).GetMethod("FindMethod", BindingFlags.Instance | BindingFlags.NonPublic, /*binder*/ null, new System.Type[] { persistentCallType }, /*modifiers*/ null);
			Debug.Assert(FindMethod != null, "found FindMethod");
		}
	}
}
