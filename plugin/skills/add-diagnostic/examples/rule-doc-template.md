# {ID}: {Title}

| Property | Value |
|----------|-------|
| **Rule ID** | {ID} |
| **Category** | {Category} |
| **Severity** | {Severity} |
| **Enabled by default** | {Yes/No} |

## Cause

{One or two sentences describing the code pattern that triggers the diagnostic.}

## Rule description

{Why the pattern is a problem and what the rule enforces. Mention the message arguments if useful.}

## How to fix violations

{Concrete steps or the transformation the user should apply. Mention the code fix if one exists.}

## When to suppress warnings

{Situations where the diagnostic is a false positive or intentionally ignored. Show the pragma:}

```csharp
#pragma warning disable {ID}
// offending code
#pragma warning restore {ID}
```

Or in `.editorconfig`:

```ini
dotnet_diagnostic.{ID}.severity = none
```

## Example

### Violates

```csharp
{Minimal code that triggers the diagnostic.}
```

### Fixed

```csharp
{The same code after applying the fix.}
```

## Related rules

- {Links to related rule docs, if any.}
