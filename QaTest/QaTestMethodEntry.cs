using System;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace QaTestFramework
{
    internal sealed class QaTestMethodEntry
    {
        public QaTestMethodEntry(string id, MethodInfo method, object target, QaTestAttribute attribute)
        {
            Id = id;
            Method = method;
            Target = target;
            Attribute = attribute;
            Parameters = method.GetParameters();
        }

        public string Id { get; }
        public MethodInfo Method { get; }
        public object Target { get; }
        public QaTestAttribute Attribute { get; }
        public ParameterInfo[] Parameters { get; }

        public string DisplayName
        {
            get
            {
                return string.IsNullOrWhiteSpace(Attribute.Name) ? Method.Name : Attribute.Name;
            }
        }

        public QaTestMethodDto ToDto()
        {
            QaTestParameterDto[] parameterDtos = new QaTestParameterDto[Parameters.Length];
            for (int i = 0; i < Parameters.Length; i++)
            {
                ParameterInfo parameter = Parameters[i];
                parameterDtos[i] = new QaTestParameterDto
                {
                    name = parameter.Name,
                    type = GetFriendlyTypeName(parameter.ParameterType),
                    isOptional = parameter.IsOptional,
                    defaultValue = parameter.HasDefaultValue && parameter.DefaultValue != null
                        ? Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture)
                        : string.Empty,
                };
            }

            return new QaTestMethodDto
            {
                id = Id,
                name = DisplayName,
                declaringType = Method.DeclaringType != null ? Method.DeclaringType.FullName : string.Empty,
                description = Attribute.Description ?? string.Empty,
                returnType = GetFriendlyTypeName(Method.ReturnType),
                isStatic = Method.IsStatic,
                parameters = parameterDtos,
            };
        }

        public object Invoke(string[] rawArguments)
        {
            object[] convertedArguments = ConvertArguments(rawArguments ?? Array.Empty<string>());
            return Method.Invoke(Target, convertedArguments);
        }

        private object[] ConvertArguments(string[] rawArguments)
        {
            object[] convertedArguments = new object[Parameters.Length];
            for (int i = 0; i < Parameters.Length; i++)
            {
                ParameterInfo parameter = Parameters[i];
                string rawValue = i < rawArguments.Length ? rawArguments[i] : null;

                if (string.IsNullOrEmpty(rawValue))
                {
                    convertedArguments[i] = GetDefaultArgument(parameter);
                    continue;
                }

                convertedArguments[i] = ConvertArgument(rawValue, parameter.ParameterType);
            }

            return convertedArguments;
        }

        private static object GetDefaultArgument(ParameterInfo parameter)
        {
            if (parameter.HasDefaultValue)
            {
                return parameter.DefaultValue;
            }

            Type type = parameter.ParameterType;
            if (type == typeof(string))
            {
                return string.Empty;
            }

            return type.IsValueType ? Activator.CreateInstance(type) : null;
        }

        private static object ConvertArgument(string rawValue, Type targetType)
        {
            Type nullableType = Nullable.GetUnderlyingType(targetType);
            if (nullableType != null)
            {
                targetType = nullableType;
            }

            if (targetType == typeof(string))
            {
                return rawValue;
            }

            if (targetType == typeof(bool))
            {
                return rawValue == "1" || rawValue.Equals("true", StringComparison.OrdinalIgnoreCase);
            }

            if (targetType.IsEnum)
            {
                return Enum.Parse(targetType, rawValue, true);
            }

            if (targetType == typeof(int))
            {
                return int.Parse(rawValue, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(long))
            {
                return long.Parse(rawValue, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(float))
            {
                return float.Parse(rawValue, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(double))
            {
                return double.Parse(rawValue, CultureInfo.InvariantCulture);
            }

            if (targetType == typeof(Vector2))
            {
                string[] parts = rawValue.Split(',');
                return new Vector2(ParseFloat(parts, 0), ParseFloat(parts, 1));
            }

            if (targetType == typeof(Vector3))
            {
                string[] parts = rawValue.Split(',');
                return new Vector3(ParseFloat(parts, 0), ParseFloat(parts, 1), ParseFloat(parts, 2));
            }

            if (targetType == typeof(Vector4))
            {
                string[] parts = rawValue.Split(',');
                return new Vector4(ParseFloat(parts, 0), ParseFloat(parts, 1), ParseFloat(parts, 2), ParseFloat(parts, 3));
            }

            return JsonUtility.FromJson(rawValue, targetType);
        }

        private static float ParseFloat(string[] parts, int index)
        {
            if (index >= parts.Length)
            {
                return 0f;
            }

            return float.Parse(parts[index], CultureInfo.InvariantCulture);
        }

        private static string GetFriendlyTypeName(Type type)
        {
            Type nullableType = Nullable.GetUnderlyingType(type);
            if (nullableType != null)
            {
                return GetFriendlyTypeName(nullableType) + "?";
            }

            if (!type.IsGenericType)
            {
                return type.Name;
            }

            string typeName = type.Name;
            int tickIndex = typeName.IndexOf('`');
            if (tickIndex >= 0)
            {
                typeName = typeName.Substring(0, tickIndex);
            }

            Type[] genericArguments = type.GetGenericArguments();
            string[] genericNames = new string[genericArguments.Length];
            for (int i = 0; i < genericArguments.Length; i++)
            {
                genericNames[i] = GetFriendlyTypeName(genericArguments[i]);
            }

            return typeName + "<" + string.Join(", ", genericNames) + ">";
        }
    }
}
