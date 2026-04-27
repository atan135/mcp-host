using System;
using System.Collections;

namespace QaTestFramework
{
    public sealed class QaTestCoroutineResult
    {
        private readonly Func<object> resultFactory;

        public QaTestCoroutineResult(IEnumerator routine)
            : this(routine, null)
        {
        }

        public QaTestCoroutineResult(IEnumerator routine, Func<object> resultFactory)
        {
            Routine = routine ?? throw new ArgumentNullException(nameof(routine));
            this.resultFactory = resultFactory;
        }

        public IEnumerator Routine { get; }

        public bool HasResultFactory
        {
            get { return resultFactory != null; }
        }

        public object GetResult()
        {
            return resultFactory != null ? resultFactory() : null;
        }
    }
}
