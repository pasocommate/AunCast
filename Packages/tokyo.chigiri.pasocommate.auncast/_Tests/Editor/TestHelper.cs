using System;
using System.Reflection;
using UnityEngine;

namespace PasocomMate.AunCast.Tests
{
    internal static class TestHelper
    {
        private const BindingFlags PRIVATE_INSTANCE =
            BindingFlags.NonPublic | BindingFlags.Instance;

        private const BindingFlags PRIVATE_STATIC =
            BindingFlags.NonPublic | BindingFlags.Static;

        public static void Set<T>(T instance, string fieldName, object value)
        {
            var field = typeof(T).GetField(fieldName, PRIVATE_INSTANCE)
                ?? typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                throw new ArgumentException($"Field '{fieldName}' not found on {typeof(T).Name}");
            field.SetValue(instance, value);
        }

        public static TResult Get<T, TResult>(T instance, string fieldName)
        {
            var field = typeof(T).GetField(fieldName, PRIVATE_INSTANCE)
                ?? typeof(T).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null)
                throw new ArgumentException($"Field '{fieldName}' not found on {typeof(T).Name}");
            return (TResult)field.GetValue(instance);
        }

        public static object Invoke<T>(T instance, string methodName, params object[] args)
        {
            var method = typeof(T).GetMethod(methodName, PRIVATE_INSTANCE)
                ?? typeof(T).GetMethod(methodName, BindingFlags.Public | BindingFlags.Instance);
            if (method == null)
                throw new ArgumentException($"Method '{methodName}' not found on {typeof(T).Name}");
            return method.Invoke(instance, args);
        }

        public static T CreateComponent<T>() where T : Component
        {
            var go = new GameObject($"Test_{typeof(T).Name}");
            return go.AddComponent<T>();
        }

        public static void Destroy(Component component)
        {
            if (component != null)
                UnityEngine.Object.DestroyImmediate(component.gameObject);
        }
    }
}
