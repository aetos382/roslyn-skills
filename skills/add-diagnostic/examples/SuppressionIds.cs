namespace Contoso.Analyzers;

/// <summary>
/// Suppression IDs used by the Contoso diagnostic suppressors.
/// </summary>
/// <remarks>
/// Prefix: CTS + 'S'. The sequence is independent from <see cref="DiagnosticIds"/>.
/// Names describe what is allowed (the reason the suppressed diagnostic does not apply).
/// </remarks>
public static class SuppressionIds
{
    public const string TestClassesMayBePublic = "CTSS0001";
    public const string EventHandlersMayBeUnused = "CTSS0002";
}
