# YaraTranslator

**Status: v1 implemented.**

A .NET 8 command-line tool that translates YARA 4.x rules into entries for the Xvirus
heuristics engine:

- `heurlist.db` for PE executables
- `heurlist2.db` for scripts and other non-executables

The design analysis and rationale are in [PLAN.md](PLAN.md). This README documents the
implemented tool.

## Build and test

```powershell
dotnet build YaraTranslator\YaraTranslator.csproj
dotnet test Xvirus.Tests\Xvirus.Tests.csproj --filter "FullyQualifiedName~YaraTranslatorTests"
```

The project is part of `Xvirus.sln`.

## Usage

```powershell
# Translate a rule file into a PE fragment.
dotnet run --project YaraTranslator -- translate rules\example.yar --target pe --out out

# Translate a directory, inspect the result, and do not write files.
dotnet run --project YaraTranslator -- translate rules\ --target auto --dry-run

# Merge into an existing database and write the merged result elsewhere.
dotnet run --project YaraTranslator -- translate rules\ --target pe `
  --merge Cloud\public\database\heurlist.db `
  --out out\heurlist.db --engine strict

# Validate a database fragment.
dotnet run --project YaraTranslator -- validate out\heurlist.yara.db --engine strict

# Inspect how a rule is lowered.
dotnet run --project YaraTranslator -- explain rules\example.yar --target pe --json
```

Exit codes:

- `0` success
- `1` usage, I/O, parse or write error
- `2` partial/skipped rules (or warnings with `--fail-on warning`)

`--target auto` writes to the PE database by default. A rule can opt into the script
database with `meta: target = "script"`; use `--target script` or `--target both` to
override this.

## Output layout

When `--out` is a directory (the default is `out`), fragments are written as:

```text
out/
  heurlist.yara.db      PE fragment
  heurlist2.yara.db     script fragment
  report.json           per-rule status and diagnostics
  yara-manifest.json    ownership information for repeatable merge/prune
```

When only one target is produced, `--out` may name a file instead:

```powershell
... --out out\heurlist.db
```

Generated fragments are valid `HEX,Name[,Deps]` database files with CRLF line endings,
uppercase undashed hex, and no blank lines. The tool never creates an empty database
file.

## Supported YARA subset

Exact translations:

- ASCII text strings, `wide`, and `ascii wide`
- Hex literals, nibble/byte wildcards, alternatives, negated bytes, and finite jumps
  when the complete expansion fits the variant cap
- `nocase` when the case expansion fits the cap
- `xor` / `xor(a-b)` when the key expansion fits the cap
- Boolean `and`, `or`, `not`
- `all of them`, `any of them`, `none of them`, `N of them`, `N of ($a*)`
- Rule references, private rules, and global rules
- `include "..."` and `import "..."` (module calls are otherwise unsupported)
- `pe.is_pe`, `uint16(0) == 0x5A4D`, `uint16be(0) == 0x4D5A` folded to target selection

Conservative approximations, only with `--allow-approximations`:

- Regular expressions: longest literal run is used
- `fullword`: boundary checks are ignored
- `#a` comparisons other than presence/absence: approximated
- `$a at ...` / `$a in ...`: offset/range is ignored
- `filesize`: ignored

Always rejected (the rule is reported as `skipped`):

- `base64` / `base64wide`
- Other PE/ELF/hash/math module calls
- `@a`, `!a`, and string offsets/lengths
- `for` quantifiers
- Conditions with no positive string that can act as an anchor

## How conditions become database lines

The condition is normalized to DNF (OR of AND terms). Each term becomes one line:

- one positive pattern is the anchor
- the other positive patterns are dependencies
- negated patterns are `!` dependencies
- a term with no dependency uses a self-dependency: `HEX,Name,HEX`

A single anchor key cannot be used by two lines because `DB.LoadDictionary` keeps only
the first line's name and dependency list. The planner uses a bipartite matching to give
each term a unique anchor; if no matching exists, the rule is skipped with a diagnostic.

## Engine behavior

The translator relies on two small, backward-compatible `BaseLibrary` changes:

1. `Scanner` treats dependencies as hard requirements and ignores unnamed support
   patterns for scoring.
2. `DB.LoadAhoCorasick` strips the `!` prefix from negative dependencies before adding
   them to the Aho tree, so the scanner can detect whether a negated pattern is present.

Without those changes, translated conditions that use dependencies would be
best-effort. They are included in this repository, so the emitted database has the
intended semantics at every `HeuristicsLevel`. If you are producing a fragment for an
older, unpatched engine, pass `--engine legacy`; dependency-based rules are then reported
as `conditional` so the risk is visible.

## Publishing

`Updater.cs` replaces `heurlist.db` and `heurlist2.db` wholesale. Do not append generated
rules only to an installed database. Review the fragments, merge them into both
`Cloud/public/database/` and `Cloud/sdk/Database/`, and bump the server-side `HeurDB` /
`HeurDB2` version.

## Known limitations

- The engine has no regex, wildcard matching, offsets, counts, file-size checks or module
  runtime. Anything outside the supported subset is rejected or explicitly approximated.
- Pattern and line caps are enforced to keep the Aho tree buildable. Lower
  `--max-variants` / `--max-lines-per-rule` for stricter output, or raise them
  deliberately after measuring scan performance.
- All loaded files are treated as one rule namespace. YARA compiler namespaces are not
  modeled; duplicate rule names across files are reported as errors.
- YARA rule licensing is the user's responsibility; no third-party rule sets are bundled.
