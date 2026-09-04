# add-diagnostic settings

Repository conventions for the add-diagnostic skill.
Commit this file at `.claude/roslyn-skills/add-diagnostic.md`.
The first fenced `json` block holds the settings; every key is optional, and detection from the repository fills in whatever is missing.
Comments and trailing commas are allowed.

```json
{
  "diagnosticPrefix": "CTS",
  "idDigits": 4,
  "diagnosticIdsFile": "src/Contoso.Analyzers/DiagnosticIds.cs",
  "suppressionIdsFile": "src/Contoso.Analyzers/SuppressionIds.cs",
  "categoriesFile": "src/Contoso.Analyzers/DiagnosticCategories.cs",
  // Category name -> band (leading digit of the number). Bands must match the comment headers in the IDs file.
  "categories": {
    "Design": 1,
    "Usage": 2,
    "Performance": 3,
  },
  "resxBaseName": "Resources",
  "docsDir": "docs/rules",
  "docsIndexFile": "README.md",
  // Placeholders: {owner} {repo} {branch} {path}. Defaults to the GitHub blob URL when origin is on github.com.
  "docUrlTemplate": "https://github.com/{owner}/{repo}/blob/{branch}/{path}",
  // AnalyzerProject | LinkedFile | SharedProject | SharedFile — where DiagnosticIds lives (normally detected, so usually omitted).
  "idSharing": "AnalyzerProject",
}
```

## Notes

Free-form notes for the skill.
For example: "Descriptors are created through the `DescriptorFactory.Create` helper in `Internal/DescriptorFactory.cs`; follow it."
