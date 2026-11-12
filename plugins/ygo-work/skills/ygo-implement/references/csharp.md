# C# and Razor C# style

Apply to C# and `@code`; preserve markup/layout during readability work.

- Brace ordinary control flow; place opening braces on declaration/control lines. Keep normal `!condition` spacing.
- Use one blank line between logical blocks/methods, after fields and before terminal flow when helpful. Keep related declarations compact; avoid multiple blank lines.
- Use explicit local/loop types; reserve `var` for anonymous or impractical-to-name types. Use target-typed `new()` with explicit declarations.
- Only private instance fields use `_camelCase`.
- Name dense conditions with meaningful booleans; prefer switch expressions or local steps to nested ternaries.
- Line length is advisory. Short readable declarations, calls, expression-bodied members and incidental guard/storage/log calls may stay compact.
- When declarations need wrapping, including constructors/primary constructors/local functions, use one parameter per line and a separate `) {` line aligned with the declaration. Significant wrapped calls use one argument per line and a separate `)` line; avoid partial wrapping.
- Use descriptive lambda names and readable fluent stages; keep obvious short expressions and conventional numeric indices compact.
- Modern syntax must improve clarity and preserve defensive checks, collection order, exception meaning, serialization and test expectations.
- Respect each project's nullable configuration; `Nullable=annotations` preserves annotations while disabling flow warnings, not all diagnostics.
- Use human judgment for cleanup. Do not automatically run CleanupCode, `dotnet format` or analyzer quick fixes. Targeted non-mutating diagnostics are allowed.
- Before analyzer-driven `static`, accessibility, nullability or defensive-state changes, inspect call sites and framework/serialization/DI/build contracts. Do not remove defensive validation to satisfy analysis.
