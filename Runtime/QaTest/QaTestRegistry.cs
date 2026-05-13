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
        private readonly Dictionary<string, QaTestMethodEntry> methodsByFullId = new Dictionary<string, QaTestMethodEntry>();

        public IReadOnlyCollection<QaTestMethodEntry> Methods
        {
            get { return methods.Values; }
        }

        public void Refresh()
        {
            methods.Clear();
            methodsByFullId.Clear();
            List<QaTestMethodCandidate> candidates = new List<QaTestMethodCandidate>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (Type type in SafeGetTypes(assembly))
                {
                    CollectType(type, candidates);
                }
            }

            ValidateUniqueMethodNames(candidates);

            foreach (QaTestMethodCandidate candidate in candidates)
            {
                RegisterMethod(candidate.Method, candidate.Attribute);
            }
        }

        public bool TryGet(string methodId, out QaTestMethodEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(methodId) && methods.TryGetValue(methodId, out entry))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(methodId) && methodsByFullId.TryGetValue(methodId, out entry))
            {
                return true;
            }

            entry = methods.Values.FirstOrDefault(method =>
                method.Method.Name == methodId ||
                method.DisplayName == methodId ||
                method.Id == methodId ||
                method.FullId == methodId);
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

        private void CollectType(Type type, List<QaTestMethodCandidate> candidates)
        {
            if (type == null)
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

                candidates.Add(new QaTestMethodCandidate(method, attribute));
            }
        }

        private static void ValidateUniqueMethodNames(IEnumerable<QaTestMethodCandidate> candidates)
        {
            Dictionary<string, MethodInfo> methodsByName = new Dictionary<string, MethodInfo>();
            foreach (QaTestMethodCandidate candidate in candidates)
            {
                string methodName = candidate.Method.Name;
                if (methodsByName.TryGetValue(methodName, out MethodInfo existing))
                {
                    throw new InvalidOperationException(
                        "Duplicate QaTest method name: " + methodName +
                        ". QaTest method names must be globally unique. Existing=" + BuildDefinitionMethodId(existing) +
                        ", Duplicate=" + BuildDefinitionMethodId(candidate.Method));
                }

                methodsByName[methodName] = candidate.Method;
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
            if (targets.Length > 1)
            {
                throw new InvalidOperationException(
                    "Multiple QaTest targets found for method: " + BuildDefinitionMethodId(method) +
                    ". Instance QaTest methods must resolve to a single target when short method IDs are enabled.");
            }

            foreach (UnityEngine.Object target in targets)
            {
                AddMethod(method, target, attribute);
            }
        }

        private void AddMethod(MethodInfo method, object target, QaTestAttribute attribute)
        {
            string methodName = method.Name;
            QaTestMethodEntry duplicate = methods.Values.FirstOrDefault(entry => entry.Method.Name == methodName);
            if (duplicate != null)
            {
                throw new InvalidOperationException(
                    "Duplicate QaTest method name: " + methodName +
                    ". QaTest method names must be globally unique. Existing=" + duplicate.FullId +
                    ", Duplicate=" + BuildFullMethodId(method, target));
            }

            string id = BuildShortMethodId(method);
            string fullId = BuildFullMethodId(method, target);
            if (methods.ContainsKey(id))
            {
                throw new InvalidOperationException(
                    "Duplicate QaTest method signature: " + id +
                    ". QaTest short method signatures must be globally unique. Existing=" + methods[id].FullId +
                    ", Duplicate=" + fullId);
            }

            QaTestMethodEntry entry = new QaTestMethodEntry(id, fullId, method, target, attribute);
            methods[id] = entry;
            methodsByFullId[fullId] = entry;
        }

        private static string BuildShortMethodId(MethodInfo method)
        {
            string parameters = string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName));
            return method.Name + "(" + parameters + ")";
        }

        private static string BuildDefinitionMethodId(MethodInfo method)
        {
            string declaringTypeName = method.DeclaringType != null ? method.DeclaringType.FullName : "UnknownType";
            string parameters = string.Join(",", method.GetParameters().Select(parameter => parameter.ParameterType.FullName));
            return declaringTypeName + "." + method.Name + "(" + parameters + ")";
        }

        private static string BuildFullMethodId(MethodInfo method, object target)
        {
            string id = BuildDefinitionMethodId(method);

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

        private sealed class QaTestMethodCandidate
        {
            public QaTestMethodCandidate(MethodInfo method, QaTestAttribute attribute)
            {
                Method = method;
                Attribute = attribute;
            }

            public MethodInfo Method { get; }
            public QaTestAttribute Attribute { get; }
        }
    }
}
