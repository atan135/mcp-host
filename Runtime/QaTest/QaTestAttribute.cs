using System;

namespace QaTestFramework
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public sealed class QaTestAttribute : Attribute
    {
        public QaTestAttribute()
        {
        }

        public QaTestAttribute(string name)
        {
            Name = name;
        }

        public QaTestAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }
    }
}
