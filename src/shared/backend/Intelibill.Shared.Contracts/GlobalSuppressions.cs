using System.Diagnostics.CodeAnalysis;

// "Shared" is a Visual Basic keyword. Every consumer of this library is C#, and the namespace
// follows the repository's src/shared/backend/Intelibill.Shared.Contracts layout.
[assembly: SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "C#-only consumers; namespace mirrors the shared project path.",
    Scope = "namespace",
    Target = "~N:Intelibill.Shared.Contracts.V1")]
