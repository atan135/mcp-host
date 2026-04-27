using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace QaTestFramework
{
    internal sealed class QaTestRegistry
    {
        private readonly Dictionary<string, QaTestMethodEntry> methods = new Dictionary<string, QaTestMethodEntry>();

        public IReadOnlyCollection<QaTestMethodEntry> Methods
        {
            get { return methods.Values; }
        }

        public void Refresh()
        {
            methods.Clear();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in SafeGetTypes(assembly))
                {
                    RegisterType(type);
                }
            }
        }

        public bool TryGet(string methodId, out QaTestMethodEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(methodId) && methods.TryGetValue(methodId, out entry))
            {
                return true;
            }

            entry = methods.Values.FirstOrDefault(method =>
                method.Method.Name == methodId ||
                method.DisplayName == methodId ||
                method.Id == methodId);
            return entry != null;
        }

        public QaTestMethodDto[] ToDtos()
        {
            return methods.Values
                .OrderBy(method => method.Method.DeclaringType != null ? method.Method.DeclaringType.FullName : string.Empty)
                .ThenBy(method => method.Method.Name)
                .Select(method => method.ToDto())
                .ToArray();
        }

        private void RegisterType(Type type)
        {
            if (type == null || type.IsAbstract && type.IsSealed)
            {
                return;
            }

            BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            foreach (MethodInfo method in type.GetMethods(flags))
            {
                QaTestAttribute attribute = method.GetCustomAttribute<QaTestAttribute>(true);
                if (attribute == null || method.ContainsGenericParameters)
                {
                    continue;
                }

                RegisterMethod(method, attribute);
            }
        }

        private void RegisterMethod(MethodInfo method, QaTestAttribute attribute)
        {
            if (method.IsStatic)
            {
                AddMethod(method, null, attribute);
                return;
            }

            Type declaringType = method.DeclaringType;
            if (declaringType == null || !typeof(MonoBehaviour).IsAssignableFrom(declaringType))
            {
                return;
            }

            UnityEngine.Object[] targets = UnityEngine.Object.FindObjectsOfType(declaringType, true);
            foreach (UnityEngine.Object target in targets)
            {
                AddMethod(method, target, attribute);
            }
        }

        private void AddMethod(MethodInfo method, object target, QaTestAttribute attribute)
        {
            string id = BuildMethodId(method, target);
            methods[id] = new QaTestMethodEntry(id, method, target, attribute);
        }

        private static string BuildMethodId(MethodInfo method, object target)
        {
            string declaringTypeName = method.DeclaringType != null ? method.DeclaringType.FullName : "UnknownType";
            string parameters = string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName));
            string id = declaringTypeName + "." + method.Name + "(" + parameters + ")";

            UnityEngine.Object unityTarget = target as UnityEngine.Object;
            if (unityTarget != null)
            {
                id += "@" + unityTarget.GetInstanceID();
            }

            return id;
        }

        private static IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException exception)
            {
                return exception.Types.Where(type => type != null);
            }
            catch
            {
                return Array.Empty<Type>();
            }
        }
    }
}
