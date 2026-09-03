namespace Contoso.Analyzers;

/// <summary>
/// Category strings passed to the DiagnosticDescriptor constructor.
/// Each category owns one number band in DiagnosticIds. (Plain text on purpose: this file may live in a
/// shared project that does not reference Microsoft.CodeAnalysis, where a cref would raise CS1574.)
/// </summary>
public static class DiagnosticCategories
{
    public const string Design = "Design";          // CTS1xxx
    public const string Usage = "Usage";            // CTS2xxx
    public const string Performance = "Performance"; // CTS3xxx
}
