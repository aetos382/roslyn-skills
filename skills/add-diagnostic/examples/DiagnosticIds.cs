namespace Contoso.Analyzers;

/// <summary>
/// Diagnostic IDs reported by the Contoso analyzers and source generators.
/// </summary>
/// <remarks>
/// Prefix: CTS. The leading digit of the number is the category band; each band is listed
/// under its own comment header and kept sorted by number. Never reuse a number that has shipped.
/// </remarks>
public static class DiagnosticIds
{
    // Design (CTS1xxx)
    public const string DisposableFieldShouldBeDisposed = "CTS1001";
    public const string AbstractTypeShouldNotHavePublicConstructor = "CTS1002";

    // Usage (CTS2xxx)
    public const string TaskShouldBeAwaited = "CTS2001";
    public const string CancellationTokenShouldBeForwarded = "CTS2002";

    // Performance (CTS3xxx)
    public const string StringConcatenationInLoopShouldUseStringBuilder = "CTS3001";
}
