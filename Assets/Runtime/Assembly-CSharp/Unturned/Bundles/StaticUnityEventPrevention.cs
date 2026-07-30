////////////////////////////////////////////////////////////////////////////////////////
// This file is part of the U3 SDK: https://github.com/smartlydressedgames/u3-sdk/    //
// Please refer to the included LICENSE.txt for copyright notice and license details. //
////////////////////////////////////////////////////////////////////////////////////////
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;

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
				System.Type componentType = component.GetType();
				TypeInfo componentInfo = GetTypeInfo(componentType);

				if (componentInfo.unityEventFields == null)
					continue;
				
				bool badComponent = false;

				foreach (FieldInfo field in componentInfo.unityEventFields)
				{
					UnityEventBase unityEvent = field.GetValue(component) as UnityEventBase;
					if (unityEvent == null)
						continue;

					for (int index = 0; index < unityEvent.GetPersistentEventCount(); ++index)
					{
						Object target = unityEvent.GetPersistentTarget(index);
						if (target == null)
						{
							badComponent = true;
							UnturnedLog.warn($"Found call to static method in {component.GetSceneHierarchyPath()} {componentType} {field.Name}, deleting component");
							goto AfterLoop;
						}
					}
				}

				AfterLoop:

				if (badComponent)
				{
					Object.DestroyImmediate(component, /*allowDestroyingAssets*/ true);
					result = false;
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

			return info;
		}

		private static List<FieldInfo> tempFields = new List<FieldInfo>();
	}
}
