namespace QaTestFramework
{
    public sealed class QaTestCoroutineReturn
    {
        public QaTestCoroutineReturn(object value)
        {
            Value = value;
        }

        public object Value { get; }

        public static QaTestCoroutineReturn From(object value)
        {
            return new QaTestCoroutineReturn(value);
        }
    }
}
