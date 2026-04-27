namespace QaTestFramework
{
    public interface IQaTestClientName
    {
        string CustomClientName { get; }
        string ResolvedClientName { get; }
        void SetClientName(string newClientName, bool persist = false, bool resendRegister = true);
        void ClearClientName(bool persist = false, bool resendRegister = true);
        void RefreshRegistration();
    }
}
