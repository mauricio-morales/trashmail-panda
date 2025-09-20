namespace TrashMailPanda.Services;

/// <summary>
/// Startup steps enumeration
/// </summary>
public enum StartupStep
{
    Initializing,
    InitializingStorage,
    InitializingSecurity,
    InitializingEmailProvider,
    InitializingContactsProvider,
    InitializingLLMProvider,
    CheckingProviderHealth,
    Ready,
    Failed
}