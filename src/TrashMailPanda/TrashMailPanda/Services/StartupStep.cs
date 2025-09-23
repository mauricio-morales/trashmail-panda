namespace TrashMailPanda.Services;

/// <summary>
/// Startup steps enumeration
/// </summary>
public enum StartupStep
{
    Initializing,
    InitializingStorage,
    InitializingSecurity,
    InitializingGoogleServices,
    InitializingEmailProvider,
    InitializingContactsProvider,
    InitializingLLMProvider,
    CheckingProviderHealth,
    Ready,
    Failed
}