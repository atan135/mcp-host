using System;

namespace QaTestFramework
{
    [AttributeUsage(AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
    public sealed class QaParamAttribute : Attribute
    {
        public QaParamAttribute(string description)
        {
            Description = description;
        }

        public string Description { get; }
    }
}
