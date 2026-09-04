using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Aetos.RoslynSkills.Tools.Internal;

/// <summary>A `const string Name = "value";` declaration, with the line it is written on.</summary>
internal sealed record ConstString(string Name, string Value, int Line);

/// <summary>A `LocalizableResourceString` member, with the classes it is nested in, outermost first.</summary>
internal sealed record LocalizableStringMember(string Name, string Accessibility, List<string> ContainingClasses);

/// <summary>
/// A parsed C# file. Everything the tool reads out of source goes through here, so a declaration written
/// inside a string, a comment or a disabled #if region is never mistaken for a real one.
/// </summary>
internal sealed class CSharpSource
{
    /// <summary>
    /// Preview accepts syntax newer than the language this tool is built with, so a repository on a later
    /// compiler still parses. No preprocessor symbols are defined, which means only the code an ordinary
    /// build would compile is reported: a constant under `#if DEBUG` is inside disabled text, not a
    /// declaration.
    /// </summary>
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.Preview, DocumentationMode.None);

    /// <summary>
    /// Callers read a file once and pass that one string to several readers, so keying the parse on the
    /// string keeps a repository-wide scan to one syntax tree per file. Weak, because the tree is only
    /// worth keeping while the text it came from is still in use.
    /// </summary>
    private static readonly ConditionalWeakTable<string, CSharpSource> Parsed = [];

    private static readonly SyntaxKind[] AccessibilityKeywords =
        [SyntaxKind.PublicKeyword, SyntaxKind.InternalKeyword, SyntaxKind.ProtectedKeyword, SyntaxKind.PrivateKeyword];

    private readonly SyntaxTree _tree;
    private readonly CompilationUnitSyntax _root;

    private CSharpSource(string text)
    {
        this._tree = CSharpSyntaxTree.ParseText(text, ParseOptions);
        this._root = (CompilationUnitSyntax)this._tree.GetRoot();
    }

    public static CSharpSource Parse([StringSyntax("c#")] string text)
    {
        return Parsed.GetValue(text, static t => new CSharpSource(t));
    }

    /// <summary>Every `const string`, in document order, whatever its accessibility.</summary>
    public IEnumerable<ConstString> ConstStrings()
    {
        foreach (var field in this._root.DescendantNodes().OfType<FieldDeclarationSyntax>())
        {
            if (!field.Modifiers.Any(SyntaxKind.ConstKeyword))
            {
                continue;
            }

            if (!IsString(field.Declaration.Type))
            {
                continue;
            }

            foreach (var variable in field.Declaration.Variables)
            {
                // Only a literal: an interpolated or computed value is not an ID a caller could read.
                if (variable.Initializer?.Value is not LiteralExpressionSyntax { Token.Value: string value })
                {
                    continue;
                }

                yield return new ConstString(variable.Identifier.ValueText, value, this.LineOf(variable));
            }
        }
    }

    /// <summary>
    /// The first static class, with the accessibility it declares. An omitted modifier is reported as
    /// internal, the default a top-level class gets.
    /// </summary>
    public (string? Name, string Visibility) StaticClass()
    {
        var declaration = this._root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Modifiers.Any(SyntaxKind.StaticKeyword));
        if (declaration is null)
        {
            return (null, "internal");
        }

        var visibility = DeclaredAccessibility(declaration.Modifiers);
        return (declaration.Identifier.ValueText, visibility is "public" or "internal" ? visibility : "internal");
    }

    /// <summary>The first class of any kind, which for a generated Designer file is the resource class.</summary>
    public string? FirstClassName()
    {
        return this._root.DescendantNodes().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText;
    }

    /// <summary>The `//` comments, in document order.</summary>
    public IEnumerable<string> SingleLineComments()
    {
        return this._root.DescendantTrivia().Where(t => t.IsKind(SyntaxKind.SingleLineCommentTrivia))
            .Select(t => t.ToString());
    }

    /// <summary>
    /// Each class that declares base types, with those types as their simple names. Only a real base list
    /// counts, so a file that merely mentions DiagnosticAnalyzer is not reported as declaring one.
    /// </summary>
    public IEnumerable<(string Name, List<string> BaseTypes)> ClassesWithBaseTypes()
    {
        foreach (var declaration in this._root.DescendantNodes().OfType<ClassDeclarationSyntax>())
        {
            if (declaration.BaseList is null)
            {
                continue;
            }

            yield return (declaration.Identifier.ValueText,
                declaration.BaseList.Types.Select(t => SimpleName(t.Type)).OfType<string>().ToList());
        }
    }

    /// <summary>
    /// The first string argument of an assembly-level attribute, as in
    /// `[assembly: NeutralResourcesLanguage("ja")]`. The `Attribute` suffix is optional, as in source.
    /// </summary>
    public string? AssemblyAttributeArgument(string attributeName)
    {
        foreach (var list in this._root.AttributeLists)
        {
            if (list.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) != true)
            {
                continue;
            }

            foreach (var attribute in list.Attributes)
            {
                var name = SimpleName(attribute.Name);
                if (name != attributeName && name != attributeName + "Attribute")
                {
                    continue;
                }

                if (attribute.ArgumentList?.Arguments is [{ Expression: LiteralExpressionSyntax { Token.Value: string value } }, ..])
                {
                    return value;
                }
            }
        }
        return null;
    }

    /// <summary>
    /// A hand-written resource helper: `static LocalizableResourceString Get(string name)`. Accessibility
    /// defaults to private, the default for a class member, because a private helper means the intended
    /// entry points are the members below.
    /// </summary>
    public LocalizableStringMember? LocalizableStringHelper()
    {
        foreach (var method in this._root.DescendantNodes().OfType<MethodDeclarationSyntax>())
        {
            if (!method.Modifiers.Any(SyntaxKind.StaticKeyword) || !IsLocalizableString(method.ReturnType))
            {
                continue;
            }

            if (method.ParameterList.Parameters is not [{ Type: { } parameter }] || !IsString(parameter))
            {
                continue;
            }

            return new LocalizableStringMember(
                method.Identifier.ValueText, DeclaredAccessibility(method.Modifiers) ?? "private", ContainingClasses(method));
        }
        return null;
    }

    /// <summary>
    /// The `LocalizableResourceString` properties and fields descriptors read their strings from, in
    /// document order. Restricted to public and internal statics: a private one is an implementation
    /// detail of the helper above, not something a descriptor can name.
    /// </summary>
    public List<LocalizableStringMember> LocalizableStringMembers()
    {
        var members = new List<LocalizableStringMember>();
        foreach (var member in this._root.DescendantNodes().OfType<MemberDeclarationSyntax>())
        {
            switch (member)
            {
                case PropertyDeclarationSyntax property when Qualifies(property.Modifiers, property.Type):
                    Add(property.Identifier, property.Modifiers, property);
                    break;

                case FieldDeclarationSyntax field when Qualifies(field.Modifiers, field.Declaration.Type):
                    foreach (var variable in field.Declaration.Variables)
                    {
                        Add(variable.Identifier, field.Modifiers, field);
                    }

                    break;
            }
        }

        return members;

        static bool Qualifies(SyntaxTokenList modifiers, TypeSyntax type)
        {
            return
                modifiers.Any(SyntaxKind.StaticKeyword) &&
                IsLocalizableString(type) &&
                DeclaredAccessibility(modifiers) is "public" or "internal";
        }

        void Add(SyntaxToken identifier, SyntaxTokenList modifiers, SyntaxNode declaration)
        {
            members.Add(new LocalizableStringMember(
                identifier.ValueText,
                DeclaredAccessibility(modifiers)!,
                ContainingClasses(declaration)));
        }
    }

    /// <summary>The classes a declaration is nested in, outermost first (e.g. ["Resources", "Localizable"]).</summary>
    private static List<string> ContainingClasses(SyntaxNode declaration)
    {
        return declaration
            .Ancestors()
            .OfType<TypeDeclarationSyntax>()
            .Select(t => t.Identifier.ValueText)
            .Reverse()
            .ToList();
    }

    /// <summary>
    /// The classes whose bodies contain <paramref name="position"/>, outermost first. A position in a
    /// class's header is not inside its body, so it reports the classes around it and not the class itself.
    /// </summary>
    public List<string> ContainingClasses(int position)
    {
        if (this._root.FindToken(position).Parent is not { } node)
        {
            return [];
        }

        return node
            .AncestorsAndSelf()
            .OfType<TypeDeclarationSyntax>()
            .Where(t =>
                !t.OpenBraceToken.IsMissing &&
                position > t.OpenBraceToken.SpanStart &&
                position < t.CloseBraceToken.SpanStart)
            .Select(t => t.Identifier.ValueText)
            .Reverse()
            .ToList();
    }

    private int LineOf(SyntaxNode node)
    {
        return this._tree.GetLineSpan(node.Span).StartLinePosition.Line + 1;
    }

    private static bool IsString(TypeSyntax? type)
    {
        return SimpleName(type) is "string" or "String";
    }

    private static bool IsLocalizableString(TypeSyntax? type)
    {
        return SimpleName(type) is "LocalizableString" or "LocalizableResourceString";
    }

    /// <summary>The name a type is written with, without its namespace or type arguments.</summary>
    private static string? SimpleName(TypeSyntax? type)
    {
        return type switch
        {
            PredefinedTypeSyntax predefined => predefined.Keyword.ValueText,
            IdentifierNameSyntax identifier => identifier.Identifier.ValueText,
            GenericNameSyntax generic => generic.Identifier.ValueText,
            QualifiedNameSyntax qualified => SimpleName(qualified.Right),
            AliasQualifiedNameSyntax alias => SimpleName(alias.Name),
            NullableTypeSyntax nullable => SimpleName(nullable.ElementType),
            _ => null,
        };
    }

    /// <summary>The accessibility keyword as written, or null when the declaration omits it.</summary>
    private static string? DeclaredAccessibility(SyntaxTokenList modifiers)
    {
        foreach (var modifier in modifiers)
        {
            if (AccessibilityKeywords.Contains(modifier.Kind()))
            {
                return modifier.ValueText;
            }
        }

        return null;
    }
}
