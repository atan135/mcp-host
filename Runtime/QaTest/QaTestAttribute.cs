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

        public QaTestAttribute(string name, bool allowParallelExecution)
        {
            Name = name;
            AllowParallelExecution = allowParallelExecution;
        }

        public QaTestAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public QaTestAttribute(string name, string description, bool allowParallelExecution)
        {
            Name = name;
            Description = description;
            AllowParallelExecution = allowParallelExecution;
        }

        public string Name { get; }
        public string Description { get; }
        public bool AllowParallelExecution { get; set; }
    }
}
