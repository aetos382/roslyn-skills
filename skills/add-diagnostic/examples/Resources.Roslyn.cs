using Microsoft.CodeAnalysis;

namespace Contoso.Analyzers;

/// <summary>
/// Hand-written partial of the generated resource class. Exposes every diagnostic string as a
/// <see cref="LocalizableResourceString"/> through the nested <see cref="Localizable"/> class, so that
/// analyzers never spell resx names themselves and cannot bypass the properties (the helper is private).
/// </summary>
/// <remarks>
/// The generated partial (Microsoft.CodeAnalysis.ResxSourceGenerator or Resources.Designer.cs) declares
/// <c>string</c> properties with the resx names (<c>DisposableFieldShouldBeDisposedTitle</c>), so the
/// localizable versions live in a nested class to keep the same names without clashing.
/// Descriptors use them as <c>title: Resources.Localizable.DisposableFieldShouldBeDisposedTitle</c>.
/// </remarks>
internal partial class Resources
{
    private static LocalizableResourceString GetLocalizableResourceString(string name)
    {
        return new LocalizableResourceString(name, ResourceManager, typeof(Resources));
    }

    /// <summary>Localizable views of the resx entries, named exactly like the entries.</summary>
    public static class Localizable
    {
        // DisposableFieldShouldBeDisposed (CTS1001)
        public static LocalizableResourceString DisposableFieldShouldBeDisposedTitle { get; } =
            GetLocalizableResourceString(nameof(DisposableFieldShouldBeDisposedTitle));

        public static LocalizableResourceString DisposableFieldShouldBeDisposedMessage { get; } =
            GetLocalizableResourceString(nameof(DisposableFieldShouldBeDisposedMessage));

        public static LocalizableResourceString DisposableFieldShouldBeDisposedDescription { get; } =
            GetLocalizableResourceString(nameof(DisposableFieldShouldBeDisposedDescription));

        // TestClassesMayBePublic (CTSS0001)
        public static LocalizableResourceString TestClassesMayBePublicJustification { get; } =
            GetLocalizableResourceString(nameof(TestClassesMayBePublicJustification));
    }
}
