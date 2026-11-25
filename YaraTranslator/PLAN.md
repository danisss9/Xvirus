# YaraTranslator - Design and Implementation Plan

**Status:** Implemented (v1). The translator, CLI, validator, merge support and tests are
in place. This document remains the design reference and records the decisions taken.
**Target:** A .NET 8 command-line tool in `YaraTranslator/`, added to `Xvirus.sln`.
**Purpose:** Convert YARA 4.x rules into entries for the Xvirus heuristics database
(`heurlist.db` for PE files, `heurlist2.db` for scripts), while reporting exactly which
YARA features were translated faithfully and which were approximated or rejected.

---

## 1. Executive summary

A YARA rule translator is feasible, but it cannot be a blind text-to-text converter.
The Xvirus heuristic engine is a literal byte-string matcher built on Aho-Corasick. It
has no regex, wildcards, offsets, string counts, file-size checks or PE module support.
It also has an unusual scoring model that must be understood before emitting signatures.

The recommended design is:

1. Write a self-contained YARA lexer/parser (no native YARA dependency).
2. Lower YARA string declarations to literal byte patterns, expanding only features that
   can be represented exactly (ASCII/wide text, hex alternatives/nibble wildcards,
   optional case and XOR expansion).
3. Normalize the YARA condition into DNF (OR of AND terms) and emit one heuristic line
   per term: a positive pattern as the anchor, the other positive patterns as
   dependencies, and negated patterns as `!` dependencies.
4. Use the existing `HEX,Name,Deps` line format and a self-dependency trick
   (`HEX,Name,HEX`) for single-pattern terms so they detect immediately instead of
   relying on the heuristic score threshold.
5. Report every approximation. Default to rejecting a rule if a faithful translation is
   impossible; support an explicit `--allow-approximations` mode for best-effort output.
6. Apply the two small, backward-compatible engine fixes described in section 3.6
   (hard dependencies, ignoring unnamed support patterns) plus stripping `!` from
   negative dependencies when building the Aho tree. These make boolean YARA conditions
   exact at every `HeuristicsLevel`.

The engine changes are included in this repository. The translator's output format is
still the existing `HEX,Name,Deps` format, and existing database entries (which have no
dependencies) are unaffected.

### 1.1 Implementation status

Implemented in v1:

- `YaraTranslator` console project added to `Xvirus.sln`
- lexer, parser, AST and diagnostic spans for YARA 4.x syntax
- include resolver with root/depth/cycle guards
- text/hex/regex lowering with expansion caps
- condition normalization to DNF, including `all`/`any`/`none`/`N of`, rule references,
  private/global rules and PE target folding
- unique-anchor assignment, line planning and status reporting
- database writer, validator, merge, prune and manifest/report output
- CLI commands: `translate`, `validate`, `explain`, `version`
- `BaseLibrary` hard-dependency/support-pattern fixes and negative-dependency loading
- xUnit tests for parsing, lowering, DNF planning, merging, validation, BaseLibrary
  compatibility and real scanner detection

Not implemented in v1: `base64`/`base64wide`, exact regex, exact `fullword`, `for`
quantifiers, module runtimes, offsets and count thresholds beyond the documented
approximations.

---

## 2. Scope

### In scope

- Parse YARA rule files, including `include` directives and comments.
- Translate the supported YARA subset into `heurlist.db` / `heurlist2.db` fragments.
- Support multiple source files, namespaces and target selection (PE, script or auto).
- Merge generated entries into an existing database without corrupting it.
- Produce a machine-readable report and a manifest for repeatable updates/pruning.
- Validate generated database files with the same parsing rules used by `DB.cs`.
- Add unit, golden, property and integration tests.

### Out of scope for v1

- Implementing a full YARA runtime or replacing the Xvirus test harness.
- Executing module functions (`pe`, `elf`, `math`, `hash`, `dotnet`, `cuckoo`, ...).
- Exact translation of regex, `fullword`, offsets, `#` counts, `filesize` or module
  conditions. These are rejected or explicitly approximated.
- Automatically publishing generated rules to the update server.
- A GUI. The CLI is enough; Portal integration can come later.

---

## 3. What the Xvirus heuristics engine actually supports

The following facts come from `BaseLibrary/Modules/DB.cs`, `BaseLibrary/Modules/Scanner.cs`
and `BaseLibrary/Modules/Aho.cs`. They drive the whole design.

### 3.1 Database files

| File          | Used for                    | Loaded by                        |
| ------------- | --------------------------- | -------------------------------- |
| `heurlist.db` | PE executables              | `DB.Load`, `Scanner.ScanFileInternal` |
| `heurlist2.db`| Non-executables / scripts   | `DB.Load`, `Scanner.ScanFileInternal` |

The current `heurlist.db` has 457 patterns and no dependency lines. The current
`heurlist2.db` has 6 patterns. Both use CRLF line endings, no header, no comments and no
blank lines.

### 3.2 Line format

```text
ANCHOR_HEX,DETECTION_NAME[,DEP1&DEP2&!DEP3]
```

Rules that the emitter must respect:

- `ANCHOR_HEX` is uppercase hexadecimal, optionally separated by `-`. `DB.LoadDictionary`
  strips `-` from the dictionary key; `DB.LoadAhoCorasick` strips `-` from every pattern.
  **Lowercase hex will never match** because the Aho matcher produces uppercase hex.
- `DETECTION_NAME` is the verdict shown by `ScanResult`. It must not contain `,`, CR or LF.
  Avoid `&` for safety.
- `DEP1&DEP2` are other pattern keys. **Dependencies are not dash-stripped** by
  `DB.LoadDictionary`, so they must be written as undashed uppercase hex.
- A leading `!` on a dependency means "this pattern must be absent".
- Every line must have at least two fields. A blank line, or a line with fewer than two
  fields, throws in `DB.LoadDictionary` because it accesses `splitLine[1]`.
- `DB.LoadAhoCorasick` also adds every dependency to the Aho tree. Negative dependencies
  are written as `!HEX`; the loader now strips the leading `!` before adding the pattern,
  so the scanner can tell whether the negated bytes are present. Without that step a
  negative dependency would always look absent.
- Duplicate anchor keys are allowed but the **first line wins** in the `heurList`
  dictionary: both the name and the dependency list from later duplicate lines are
  ignored by `DB.LoadDictionary`, while `DB.LoadAhoCorasick` still adds the duplicate
  patterns to the tree. Emitting the same literal twice with different names or
  dependencies silently changes behaviour. This is a collision the translator must
  detect and reject.
- An empty `heurlist.db` is dangerous: `new AhoCorasickTree(Array.Empty<string>())` throws
  `ArgumentException`. The translator must never create an empty database file.
- The matcher walks the file as uppercase hex, so valid patterns are byte-aligned. A
  pattern with an odd number of hex characters can match at a nibble boundary and is
  invalid for a byte signature; a non-hex character will never match. The validator must
  reject both.

### 3.3 Matching and scoring

`Scanner` first collects all matches in file order, then creates `matchesKeys`, a set of
all matched patterns. For each match it evaluates dependencies, then scoring:

```csharp
foreach (var match in matches)
{
    if (!heurList.TryGetValue(match, out var name))
        continue;

    if (deps.TryGetValue(match, out var matchDeps))
    {
        if (!matchDeps.All(dep => dep[0] == '!' ? !matchesKeys.Contains(dep.Substring(1))
                                                : matchesKeys.Contains(dep)))
            continue;

        return new ScanResult(1, name, filePath);
    }

    if (score < (5 - settings.HeuristicsLevel))
    {
        score += match.StartsWith("Suspicious:") ? 1 : 2;
        continue;
    }

    return new ScanResult(1, name, filePath);
}
```

Consequences:

1. **A single distinct pattern does not detect at the default level.**
   `HeuristicsLevel` defaults to 4, so the threshold is 1. The first match only adds 2 to
   the score; a second named match is needed to return a verdict. Approximate matches
   required for a single standalone pattern:

   | HeuristicsLevel | Threshold | Distinct matches needed |
   | --------------- | --------- | ----------------------- |
   | 5               | 0         | 1                       |
   | 4 (default)     | 1         | 2                       |
   | 3               | 2         | 2                       |
   | 2               | 3         | 3                       |
   | 1               | 4         | 3                       |

   Duplicate occurrences of the same pattern also count, because `matches` is a list of
   occurrences, not a set. This is why the emitter uses a self-dependency for
   single-pattern terms: a line with dependencies returns immediately when those
   dependencies are satisfied, bypassing the score threshold.

2. **Dependencies are hard requirements in the implemented engine.** If a pattern has
   dependencies and they are not satisfied, the scanner continues without adding score.
   The original implementation fell through to the scoring branch; section 3.6 records the
   fix.

3. **Dependency patterns are unnamed support patterns in the recommended output.**
   If a dependency is not emitted as its own anchor line, it has no entry in `heurList`.
   It still participates in `matchesKeys` but is ignored for scoring because the scanner
   only scores named signatures.

4. **`HeuristicsLevel` no longer bypasses dependencies.** Because the dependency check
   happens before the score check and unsatisfied dependencies `continue`, AND/NOT rules
   behave the same at every level. Standalone patterns still use the normal score
   threshold.

5. **There is no wildcard, regex, offset, count or file-metadata support.** The only
   expressible condition is a Boolean combination of "literal bytes present / absent
   anywhere in the file", plus the global score behaviour.

### 3.4 PE versus script databases

The scanner chooses `heurlist.db` when the file is a PE and `heurlist2.db` otherwise. YARA
rules do not carry this distinction, so the translator needs a target policy:

- `--target pe` - write to the PE fragment (default).
- `--target script` - write to the script fragment.
- `--target auto` - PE if the rule uses PE-specific constructs (`pe.`, `uint16(0)`,
  `MZ`/`0x5A4D`), otherwise PE by default; script if `meta.target = "script"` or the rule
  is in a file/directory marked as script.
- `--target both` - emit both fragments with the same names. Use sparingly; it doubles
  false-positive surface.

### 3.5 Distribution

`Updater.cs` replaces `heurlist.db` and `heurlist2.db` from the cloud server. Appending
rules to an installed copy only will be lost on the next update. Generated rules must be
reviewed and merged into the source databases under `Cloud/public/database/` and
`Cloud/sdk/Database/`, then the server-side `HeurDB` / `HeurDB2` version must be bumped.
The translator should write fragments and a report, never publish automatically.

### 3.6 Engine patch (implemented)

These changes make the DNF encoding exact and are backward compatible (the current
database has no dependency lines). They are implemented in `BaseLibrary`:

1. **Hard dependencies.** If a line has dependencies and they are not satisfied, skip the
   match instead of falling through to scoring.
2. **Ignore unnamed support patterns.** If `heurList` has no name for a match, skip it
   instead of adding score. This prevents dependencies from inflating the global score.

Sketch, applied to both the PE and script branches (ideally via one shared helper):

```csharp
foreach (var match in matches)
{
    if (!database.heurList.TryGetValue(match, out var name))
        continue; // support-only pattern, never a verdict

    if (database.heurListDeps.TryGetValue(match, out var matchDeps))
    {
        bool satisfied = matchDeps.All(dep =>
            dep.Length > 0 &&
            (dep[0] == '!' ? !matchesKeys.Contains(dep[1..])
                           : matchesKeys.Contains(dep)));

        if (!satisfied)
            continue; // hard dependency

        return new ScanResult(1, name, filePath);
    }

    // Existing scoring path for standalone patterns.
    ...
}
```

The loader also strips the leading `!` from negative dependencies before adding them to
the Aho tree. Deduplicating patterns before constructing the tree remains an optional
performance clean-up; self-dependencies add the same pattern twice but do not change the
verdict.

---

## 4. Translation fidelity model

Every rule gets a status:

| Status     | Meaning                                                                 |
| ---------- | ----------------------------------------------------------------------- |
| `exact`    | The generated lines detect the same files as YARA for the supported literal subset, under the patched engine. |
| `conditional` | Correct under the current engine for typical inputs, but vulnerable to score fall-through, level 5, or shared-anchor score pollution. |
| `partial`  | Some patterns/conditions were approximated; false positives or negatives are possible. Always accompanied by warnings. |
| `skipped`  | No safe translation. Reason is reported.                                 |

The translator never silently drops part of a condition. If a condition cannot be
represented, the rule is `partial` (best-effort mode) or `skipped` (default mode).

---

## 5. Feature coverage matrix

| YARA feature                         | v1 support | Mapping / notes |
| ------------------------------------ | ---------- | --------------- |
| ASCII text string                    | Exact      | Raw bytes; YARA default encoding. |
| `wide` text string                   | Exact      | UTF-16LE bytes as a separate pattern variant. |
| `ascii wide`                         | Exact      | OR of the ASCII and wide variants; one line per variant. |
| `nocase`                             | Partial    | Enumerate ASCII case variants when the variant count is below the cap; otherwise reject/approximate. |
| `fullword`                           | Partial    | No boundary assertions in the engine. Reject by default; best-effort may emit the bare literal with a warning. |
| Plain hex string `{ 66 6F 6F }`      | Exact      | Literal bytes. |
| Hex nibble wildcard `?F`, `F?`       | Exact      | Expand to 16 variants when the total expansion fits the cap. |
| Hex byte wildcard `??`               | Exact      | Expand to 256 variants per wildcard; the total product must fit the cap. |
| Hex jump `[n]`, `[n-m]`              | Partial    | A jump means arbitrary bytes. Fully enumerate every byte sequence only when the total fits the cap; otherwise reject. |
| Hex negated byte `~00`               | Exact      | Expand to 255 variants when the total fits the cap. |
| Hex alternatives `(61|62)`           | Exact      | Cartesian product expansion when the total fits the cap. |
| Regex `/.../`                        | Partial    | Reject by default. Best-effort can extract literal runs of at least `MinPatternBytes`, which may over-match. |
| `xor`, `xor(a-b)`                    | Partial    | Enumerate XOR keys when under the cap. |
| `base64`, `base64wide`               | Later      | Complex alphabet rules. Not in v1. |
| Boolean `and`, `or`, `not`           | Exact*     | DNF + dependencies + `!`. *Exact with the engine patch; conditional without it. |
| `all of them`, `any of them`         | Exact*     | All = one term; any = one term per string. |
| `none of them`                       | Conditional | Conjunction of negations; only representable when conjoined with a separate positive atom that can act as the anchor. |
| `N of them`, `N of ($a*)`            | Exact*     | Expand to all N-combinations; cap the term count. |
| `#a >= 1`, `#a > 0`                  | Exact*     | Maps to `$a` presence. |
| `#a == 0`, `#a < 1`                  | Conditional | Maps to `not $a`; still needs a positive atom in the same term to act as the anchor. |
| `#a >= N` for `N > 1`, `#a == N`     | Partial    | Presence only. Rejected by default; best-effort approximates as `$a` presence. |
| `$a at offset`, `$a in (a..b)`       | Partial    | Offset is not representable. Reject by default; best-effort ignores the constraint. |
| `@a[i]`, `!a[i]`                     | No         | Match offsets/lengths are not exposed. Reject. |
| `filesize`                           | No         | No file metadata in the DB. Reject. |
| `uint16(0) == 0x5A4D`, `pe.is_pe`    | Exact      | Fold to "PE target" and route to `heurlist.db`; do not emit a separate signature. |
| Other `pe.*`, `elf.*`, `math.*`, ... | No         | Modules are parsed but treated as unsupported. Reject. |
| Rule references (`other_rule`)       | Exact*     | Inline the referenced condition with cycle detection. Private rules are inlined. |
| Global rules                         | Partial    | Conjoin the global condition into every rule in the namespace; if not expressible, fail those rules. |
| External variables                   | Partial    | Substitute `--define KEY=VALUE` and constant-fold; otherwise reject. |
| String operators (`contains`, `startswith`, `iequals`, `matches`, ...) | No | Literal matching only. Reject unless the operands are constant and fold to true/false. |
| `for any/all of ...` quantifiers     | No         | No runtime loop support. Reject. |
| `defined`, `entrypoint`              | No         | No expression/variable runtime. Reject. |
| `include "..."`                      | Exact      | Resolve relative to the including file, then include directories; cycle and depth guards. |
| `import "..."`                       | N/A        | Parsed and recorded; use of the module is unsupported unless folded to PE target. |

`Exact*` means "exact for the supported literal subset, using the engine patch included
in this repository". The report always states which engine profile was assumed.

---

## 6. Project layout

Keep the tool self-contained so it can be built and tested without loading LLamaSharp,
ONNX Runtime or the rest of `BaseLibrary`:

```text
YaraTranslator/
  YaraTranslator.csproj        net8.0, nullable, Exe, no external packages
  README.md                    short overview and usage
  PLAN.md                      this document
  Program.cs                   CLI entry point
  Cli/
    CommandLine.cs             argument parsing and validation
    TranslateCommand.cs
    ValidateCommand.cs
    ExplainCommand.cs
  Parsing/
    YaraToken.cs
    YaraLexer.cs
    YaraAst.cs
    YaraParser.cs
    Diagnostic.cs
  Semantics/
    IncludeResolver.cs
    RuleResolver.cs            imports, rule refs, global/private rules
    ConstantFolder.cs
    StringLowering.cs          text/hex/regex/encoding expansion
    ConditionNormalizer.cs     AST -> Boolean IR -> DNF
    PatternExpander.cs         nocase/xor/wildcard variant expansion
    SignaturePlanner.cs        DNF term -> database lines
  Emit/
    HeuristicDbWriter.cs
    HeuristicDbMerger.cs
    ManifestStore.cs
    TranslationReport.cs
  Validation/
    HeuristicDbValidator.cs
  samples/
    README.md
    basic.yar
    conditions.yar
    encodings.yar
    unsupported.yar
```

Add the project to `Xvirus.sln`. Tests can live in the existing `Xvirus.Tests` project
with a `ProjectReference` to `YaraTranslator`, or in a new `YaraTranslator.Tests` project
if the dependency on BaseLibrary's native packages becomes inconvenient. The existing
test project already has access to `DB` internals via `InternalsVisibleTo`, which is useful
for compatibility tests.

Use a handwritten lexer/parser rather than a NuGet YARA wrapper: the tool needs a stable
AST, precise diagnostics and source spans, and native YARA bindings do not expose the AST.
No external NuGet dependency also keeps the tool AOT-friendly and easy to build in CI.

---

## 7. Translation pipeline

### 7.1 Ingest

- Accept files and directories; expand `*.yar` / `*.yara` recursively.
- Cap input file size (default 16 MB) and include depth (default 32).
- Resolve `include` directives relative to the including file, then each `--include-dir`.
- Reject include paths that escape the allowed roots; detect cycles.
- Record a content hash of all inputs for the manifest and report.

### 7.2 Lex and parse

Lexer tokens:

- identifiers and keywords (`rule`, `meta`, `strings`, `condition`, `private`, `global`,
  `import`, `include`, `and`, `or`, `not`, `all`, `any`, `none`, `of`, `them`, `at`, `in`,
  `for`, `true`, `false`, `defined`, `contains`, `startswith`, ...)
- string identifiers `$a`, `$`, `#a`, `@a`, `!a`
- decimal/hex integers, floats, `KB`/`MB` suffixes
- text literals with YARA escapes (`\n`, `\t`, `\\`, `\"`, `\xNN`)
- hex string bodies including `??`, `?A`, `A?`, `[n]`, `[n-m]`, `( ... | ... )`, `~`
- regex literals with flags (`i`, `s`)
- punctuation and comments (`//`, `/* ... */`)

Parser:

- Recursive descent for rules, meta entries, string declarations and the condition.
- Pratt/precedence parser for expressions: `or`, `and`, `not`, comparisons, string
  operators, bitwise/shift/arithmetic, unary operators, postfix indexing, `of`, `at`,
  `in`, `for` quantifiers and function calls.
- Preserve spans for every AST node so diagnostics can point to the exact YARA line.
- Parse unsupported constructs into explicit `UnsupportedExpr`/`UnsupportedString` nodes
  instead of failing the whole file. A rule with an unsupported construct can still be
  partially analyzed and reported.

### 7.3 Resolve

- Resolve `include` results into one rule set.
- Record `import` declarations. A module import alone is harmless; a module call is
  unsupported unless it folds to a PE target check.
- Resolve rule references by name. Inline referenced conditions with a recursion guard.
- Apply `private`: private rules are not emitted as verdicts but are inlined when
  referenced.
- Apply `global`: conjoin the global condition into every non-private rule in the same
  namespace. If the global condition is not representable, mark those rules `skipped`.
- Substitute external variables from `--define`; fold constants.

### 7.4 Lower string declarations

For each YARA string, produce zero or more **pattern variants** (literal byte arrays):

- Text:
  - `ascii` (default): bytes from the parsed escapes.
  - `wide`: UTF-16LE encoding.
  - `ascii wide`: both variants; the condition sees an OR of variants.
  - `nocase`: enumerate ASCII case variants if `2^letters` is under the cap. Only ASCII
    letters are case-folded, matching YARA.
- Hex:
  - Parse bytes, nibble wildcards, jumps and alternatives into a small expansion tree.
  - Expand exactly when the number of variants is under the cap.
  - Reject patterns with unbounded jumps or expansion above the cap.
- Regex:
  - Not executable by the engine. Default: unsupported.
  - Best-effort: extract literal runs of at least `MinPatternBytes`; if a finite
    alternation is detectable, expand it. Mark the rule `partial`.
- `xor` / `xor(a-b)`: generate `byte ^ key` for each key in the range, under the cap.
- `fullword`: no boundary support. Default: unsupported; best-effort emits the bare
  literal and warns.
- Reject variants shorter than `MinPatternBytes` (default 4; existing DB minimum is 5).
  Warn when a variant is shorter than 6 bytes because false positives are more likely.
  If a *required* variant is rejected for length, the rule cannot be exact; mark it
  `partial` or `skipped` rather than silently dropping the variant.
- Reject a variant longer than `MaxPatternBytes` (default 4096). Split only if the split
  is semantically safe; otherwise report the rule as skipped.
- Canonicalize every variant to uppercase, undashed, even-length hex.

Store variants with a stable ID (`$a#0`, `$a#1`, ...) and keep a mapping back to the
source string identifier.

### 7.5 Normalize the condition

Build a small Boolean IR over **atoms**:

```csharp
abstract record BoolExpr;
record Atom(string StringId, string VariantId) : BoolExpr; // literal presence
record And(IReadOnlyList<BoolExpr> Items) : BoolExpr;
record Or(IReadOnlyList<BoolExpr> Items) : BoolExpr;
record Not(BoolExpr Item) : BoolExpr;
record True : BoolExpr;
record False : BoolExpr;
record Unsupported(string Reason) : BoolExpr;
```

Handling:

- `$a` -> OR of that string's variants.
- `not $a` -> NOT of that OR.
- `all of them` -> AND of all string atoms.
- `any of them` -> OR of all string atoms.
- `none of them` -> AND of NOTs.
- `N of them` / `N of ($a*)` -> OR over every N-combination of ANDs.
- Rule references -> substitute the resolved condition.
- `filesize`, `#a`, `@a`, offsets, module calls, `for` quantifiers -> `Unsupported`
  unless a safe constant-fold or best-effort approximation applies.
- Constant comparisons and boolean literals -> `True` / `False`.
- `uint16(0) == 0x5A4D`, `pe.is_pe` -> `True` when the rule targets PE; route to
  `heurlist.db`. If the rule targets scripts, the condition is false and the rule is
  skipped.

Then convert to DNF:

1. Push `not` inward with De Morgan.
2. Flatten nested `and` / `or`.
3. Remove double negations.
4. Drop `False` terms and `True` conjuncts.
5. Detect contradictions (`A` and `not A`) and drop the term.
6. Deduplicate atoms and terms.
7. Cap the number of terms (`MaxTermsPerRule`, default 1024). If exceeded, mark the rule
   `skipped` or `partial` with a precise message. Never silently truncate.

A DNF term is a conjunction of positive and negative atoms. It is representable only if
it contains at least one positive atom (that atom becomes the anchor). Terms that are
purely negative (`not $a and not $b`) are rejected because there is no pattern that can
serve as the anchor. Such a rule would match almost every file anyway and is a poor
heuristic candidate.

### 7.6 Plan database lines

For each DNF term:

1. Expand positive atoms to their variants; the term is an OR over all variant
   combinations (because each atom is an OR of variants).
2. Expand negative atoms to all their variants; all must be absent, so they become a
   conjunction of `!variant` dependencies (no cross-product).
3. Cap the Cartesian product (`MaxVariantsPerTerm`, default 256). If exceeded, skip the
   rule or mark it partial.
4. For each combination, choose an anchor. Prefer the longest positive variant to reduce
   false positives; all other positive variants become dependencies.
5. If the resulting dependency list is empty, use the anchor itself as a self-dependency:
   `HEX,Name,HEX`. This makes the line report immediately when the anchor matches,
   independent of `HeuristicsLevel`.
6. Emit a line:
   - anchor = canonical uppercase undashed hex
   - name = configured detection name
   - dependencies = canonical uppercase undashed hex, each negative one prefixed `!`
7. Deduplicate lines globally. If the same anchor appears with the same name and deps,
   keep one. If the same anchor appears with a different name or different deps, apply
   the collision policy. This is mandatory, not cosmetic: `DB.LoadDictionary` keeps only
   the first line's name and dependency list for a duplicate key.
8. Enforce a global `MaxLinesPerRule` (default 4096) in addition to the per-term caps.
9. Assign anchors with a bipartite matching across all terms in a target. Terms can share
   candidate literals (for example `($a and $b) or ($a and $c)`), so a naive "pick the
   longest positive string" can reuse an anchor and silently lose a term. Each term gets
   one unique anchor; if no matching covers every term, the affected rule is skipped and
   the conflict is reported.

Why the self-dependency works: `DB.LoadAhoCorasick` adds both the anchor and the
dependency to the tree, so the anchor is in `matchesKeys` when it matches. `Scanner`
evaluates dependencies before scoring and returns immediately when they are satisfied.
No engine change is needed for single-pattern rules.

### 7.7 Naming

- Default template: `YARA.{rule}`. Example: `YARA.Example`.
- `--name-template` supports `{namespace}`, `{rule}`, `{file}` and `{tag}`.
- Sanitize the result: replace `,`, CR, LF and control characters; collapse whitespace;
  cap length (for example 120 characters).
- Do not use `Suspicious:` by default. That prefix changes the score increment to 1 and
  is intended for low-confidence heuristics, not translated YARA signatures.
- Keep names stable across runs so manifests and diffs are deterministic.
- Optional future enhancement: map `meta.severity` or tags to a category prefix, but do
  not make it the default because it changes verdict names.

### 7.8 Emission and merge

- Output one fragment per target: `heurlist.yara.db` and/or `heurlist2.yara.db`, plus
  `yara-manifest.json` and `report.json`.
- Use UTF-8 (no BOM), CRLF, and a final CRLF. Never emit blank lines, comments or a
  header.
- Never write an empty file; if nothing translates, report it and leave any existing
  database untouched.
- With `--merge`, parse the existing DB with the same rules as `DB.cs`, detect collisions,
  preserve existing lines and append new entries in deterministic order (sorted by
  detection name, then anchor, then dependencies). Existing lines are preserved
  byte-for-byte, including pre-existing malformed anchors, so a merge never silently drops
  unrelated data; newly generated lines are canonical.
- Use an atomic write (temp file + replace) and refuse to overwrite a file that is
  currently locked.
- Collision policies:
  - `error` (default): stop and report the conflicting anchors.
  - `skip`: keep the existing entry.
  - `keep`: keep the existing entry, even if the new one differs.
  - `overwrite`: replace the existing entry.
- Preserve ownership information in the manifest so `--prune` can remove only lines that
  this tool previously generated.

---

## 8. CLI design

```text
YaraTranslator translate <paths...> [options]
  --out <dir|file>             Output directory or fragment file.
  --target auto|pe|script|both Target database. Default: auto.
  --engine legacy|strict       Assumed scanner behavior for reporting. Default: strict.
  --allow-approximations       Emit best-effort rules; otherwise skip them. Default: off.
  --merge <db>                 Merge into an existing database.
  --collision error|skip|keep|overwrite
  --name-template <template>   Default: "YARA.{rule}".
  --min-pattern-bytes <n>      Default: 4. Reject shorter variants.
  --max-pattern-bytes <n>      Default: 4096. Reject longer variants.
  --max-terms-per-rule <n>     Default: 1024.
  --max-variants <n>           Default: 256. Per-string expansion cap.
  --max-lines-per-rule <n>     Default: 4096.
  --max-input-bytes <n>        Default: 16777216.
  --include-dir <path>         Repeatable.
  --define KEY=VALUE           Repeatable external variable.
  --manifest <file>            Default: <out>/yara-manifest.json.
  --report <file>              Default: <out>/report.json.
  --dry-run                    Parse, translate and report without writing.
  --fail-on <none|partial|warning>  Exit-code policy. Default: partial.

YaraTranslator validate <db...> [--engine legacy|strict]
  Checks format, hex validity, duplicate anchors, dependency references, empty files,
  contradictory dependencies and optional Aho construction.

YaraTranslator explain <rule> [--json]
  Prints the DNF terms and planned lines for one rule without writing files.

YaraTranslator version
```

Exit codes:

| Code | Meaning |
| ---- | ------- |
| 0    | Success; every selected rule was translated exactly. |
| 1    | Usage, I/O, parse or output error. |
| 2    | Completed but some rules were partial or skipped (unless downgraded by policy). |

Examples:

```powershell
# Translate one rule file into a PE fragment and a report.
dotnet run --project YaraTranslator -- translate rules\example.yar --target pe --out out

# Translate a directory and merge into the source database without writing yet.
dotnet run --project YaraTranslator -- translate rules\ --target auto `
  --merge Cloud\public\database\heurlist.db --dry-run

# Validate a generated fragment.
dotnet run --project YaraTranslator -- validate out\heurlist.yara.db --engine strict

# Inspect how one rule will be lowered.
dotnet run --project YaraTranslator -- explain rules\example.yar --json
```

---

## 9. Report and manifest

`report.json` is for humans and CI. It should include:

```json
{
  "toolVersion": "0.1.0",
  "generatedAt": "2026-11-25T12:00:00Z",
  "inputHash": "sha256:...",
  "engineProfile": "strict",
  "options": { "target": "auto", "allowApproximations": false },
  "summary": {
    "rules": 120,
    "exact": 82,
    "conditional": 10,
    "partial": 18,
    "skipped": 10,
    "lines": 540
  },
  "rules": [
    {
      "name": "Example",
      "file": "rules/example.yar",
      "line": 12,
      "target": "pe",
      "status": "exact",
      "lines": 1,
      "features": ["ascii", "all-of"],
      "diagnostics": []
    }
  ]
}
```

`yara-manifest.json` records the generated entries and the ownership hash so reruns are
idempotent and `--prune` is safe:

```json
{
  "schema": 1,
  "sourceHash": "sha256:...",
  "entries": [
    {
      "rule": "Example",
      "anchor": "666F6F626172",
      "name": "YARA.Example",
      "dependencies": ["62617A717578"],
      "target": "pe"
    }
  ]
}
```

---

## 10. Validation and test plan

### 10.1 Unit tests

- Lexer: comments, escaped text, hex tokens, regex flags, CRLF/LF, malformed input.
- Parser: every supported construct and every explicitly unsupported construct; ensure
  unsupported nodes do not crash the parse.
- String lowering: ASCII, wide, ascii+wide, escapes, nocase expansion, hex nibble
  wildcards, jumps, alternatives, XOR ranges, minimum length, expansion caps.
- Condition normalization: De Morgan, DNF deduplication, contradictions, `all/any/none/N
  of`, rule references, external variables, PE folding, unsupported propagation.
- Planner: anchor selection, self-dependency, negative dependencies, variant
  cross-product, caps, naming, sanitization.
- Emitter: exact CRLF output, no blank lines, uppercase/undashed hex, final newline,
  empty-output refusal, deterministic ordering.
- Merger: idempotent re-run, collision policies, existing-line preservation, atomic write,
  malformed existing DB.
- Validator: odd-length hex, lowercase hex, blank line, missing name, unknown dependency,
  contradictory dependency, empty file.

### 10.2 Golden tests

Add a small corpus under `YaraTranslator/samples/`:

- `basic.yar`: one string, two strings, all-of.
- `conditions.yar`: any/all/none/N-of, not, rule references.
- `encodings.yar`: ascii, wide, ascii+wide, nocase.
- `hex.yar`: wildcards, jumps, alternatives, XOR.
- `unsupported.yar`: regex, fullword, offsets, `#` counts, `filesize`, PE modules.

Each sample has an expected `.db` fragment and expected `report.json` summary. Provide a
`--update-snapshots` test-only switch so intentional changes are easy to review in a diff.

### 10.3 Engine compatibility tests

Reference `BaseLibrary` from the test project and:

1. Copy a generated fragment to a temp folder as `heurlist.db` or `heurlist2.db`.
2. Instantiate `DB` with `EnableHeuristics = true`.
3. Assert `heurList`, `heurListDeps` and the Aho tree contain the expected entries.
4. Write synthetic files to disk and call `AhoCorasickTree.Search` to confirm matches.
5. Optionally instantiate `Scanner` with AI disabled and a minimal `Rules` object, then
   assert the verdict name for positive and negative samples.

This catches format mistakes that a pure string comparison would miss (case, dashes,
dependency normalization, duplicate anchors).

### 10.4 Semantic tests

The v1 test suite uses the real `BaseLibrary` scanner for semantic checks:

- Translate a rule, write the fragment as `heurlist2.db`, load it through `DB`, and scan
  synthetic files with `Scanner` at the default `HeuristicsLevel`.
- Assert positive samples detect with the expected rule name and negative samples do not.
- Cover single-pattern self-dependencies, positive dependencies, and negative
  dependencies.

This exercises the actual `DB`, `AhoCorasickTree` and `Scanner` code paths, including the
dependency hardening and negative-dependency loading. A separate reference evaluator can
be added later for broader combinatorial coverage.

If a native `libyara` is available in a developer environment, add an optional manual
harness that compares against real YARA for the supported subset. Keep it out of CI so the
build has no native dependency.

### 10.5 Fuzzing and robustness

- Feed random mutations of valid rules to the parser; assert no unhandled exception and no
  hang.
- Feed adversarial include paths (`..\..\Windows\...`) and verify rejection.
- Feed huge strings, huge `N of` combinations and huge hex expansions; verify caps trigger
  clean diagnostics.
- Feed oversized input files and deeply nested includes.

### 10.6 Performance tests

- Generate 10,000 synthetic rules / 50,000 lines and measure:
  - translation time,
  - output size,
  - `AhoCorasickTree` construction time and memory,
  - scan time on a 10 MB synthetic file.
- Establish caps if the existing Aho implementation becomes the bottleneck. The current
  tree uses per-node resizing arrays, so very large pattern sets may need a warning or a
  future engine optimization.

---

## 11. Implementation phases

Phases 0-4 and the core of Phase 5 are implemented. Phase 6 (reviewing a real corpus and
publishing merged rules) remains an operational step rather than code.

### Phase 0 - Decisions and engine RFC

- Confirm the accepted fidelity level and whether the optional `Scanner.cs` patch is
  approved.
- Confirm target database locations and naming convention.
- Document the supported YARA subset in the README.

**Deliverable:** approved plan and engine patch decision.

### Phase 1 - Scaffold and parser

- Create `YaraTranslator.csproj`, add it to `Xvirus.sln`.
- Implement lexer, AST, parser, diagnostics and include resolver.
- Add parser/lexer unit tests and the sample corpus skeleton.
- `explain` can already print the AST for debugging.

**Acceptance:** all sample YARA files parse; unsupported constructs produce diagnostics,
not crashes.

### Phase 2 - Literal lowering and emitter

- Implement text/hex lowering, canonical hex, minimum lengths and expansion caps.
- Implement the `HEX,Name,Deps` writer and the `validate` command.
- Implement single-pattern self-dependencies and OR-of-strings translation.
- Add DB compatibility tests against `BaseLibrary`.

**Acceptance:** a YARA rule with one literal or an `any of them` condition produces a valid
fragment that detects synthetic positive files and not negative files.

### Phase 3 - Boolean conditions and DNF

- Implement the Boolean IR, normalization, `all/any/none/N of`, `not`, rule references and
  target folding.
- Implement the planner, variant cross-product and caps.
- Add semantic tests comparing the reference evaluator with generated lines.
- Engine patch implemented and covered by strict-engine scanner tests.

**Acceptance:** `and`, `or`, `not`, `all`, `any` and `N of` rules translate with the
documented status; no silent condition truncation.

### Phase 4 - Merge, manifest and reports

- Implement `--merge`, collision policies, atomic writes and `--prune`.
- Implement `report.json` and `yara-manifest.json`.
- Add idempotency and malformed-database tests.

**Acceptance:** re-running the same translation produces byte-identical output; merging
never corrupts an existing database.

### Phase 5 - Best-effort features

- `nocase`, `fullword`, regex literal extraction, `xor`, finite hex expansions.
- Benign-corpus false-positive check and optional pattern filtering.
- Expand the sample corpus and add a real-world ruleset review.

**Acceptance:** every best-effort rule is marked `partial` with a specific warning, and the
report lists the unsupported feature.

### Phase 6 - Documentation and rollout

- Write `YaraTranslator/README.md` with usage, supported subset and limitations.
- Add the tool to developer docs / CHANGELOG.
- Review generated rules against a benign corpus.
- Merge accepted rules into both cloud database copies and bump the server-side
  `HeurDB` / `HeurDB2` version.

**Acceptance:** a documented, repeatable workflow exists from YARA source to published
heuristic database.

---

## 12. Risks and mitigations

| Risk | Impact | Mitigation |
| ---- | ------ | ---------- |
| Short or common strings create false positives | High | `MinPatternBytes`, warn under 6 bytes, benign-corpus scan, manual review, optional frequency filter. |
| Score fall-through defeats AND/NOT conditions | Medium | Recommend the two-line `Scanner` patch; report `conditional` status without it; warn about level 5. |
| Shared literal used by multiple rules | Medium | Detect duplicate anchors; `error` by default; merge only when name and deps match. |
| DNF/N-of explosion | Medium | Cap terms and variants; skip with a precise diagnostic; never truncate silently. |
| Aho tree size/performance | Medium | Cap generated lines/patterns; benchmark; warn on large output. |
| Empty or malformed DB output | High | Validator, empty-output refusal, CRLF/no-blank-line writer, atomic replace. |
| Updater overwrites local rules | High | Merge upstream into `Cloud/public/database` and `Cloud/sdk/Database`; bump server version. |
| Unsupported YARA semantics silently lost | High | Status per rule, diagnostics, default reject, `--allow-approximations` opt-in. |
| Malicious/untrusted rule files | Medium | Size/depth caps, include-root enforcement, no code execution, no regex execution, bounded parser. |
| YARA language drift | Low | State the supported YARA 4.x subset; add fixtures when new syntax appears. |
| Third-party rule licensing | Low | Do not bundle third-party rules; the user supplies the rules. |

---

## 13. Open decisions

Decisions taken for v1:

1. **`Scanner.cs` patch:** approved and implemented. Boolean translation is exact at every
   `HeuristicsLevel`.
2. **Default output location:** fragments under `out/` unless `--out` is supplied. Cloud
   databases are updated only by an explicit merge/write.
3. **YARA version target:** YARA 4.x syntax; hex wildcards, jumps, alternatives and `~`
   are supported when they can be fully expanded.
4. **Naming convention:** `YARA.{rule}` by default, configurable with
   `--name-template`.
5. **Best-effort features:** available only with `--allow-approximations`; default mode
   rejects them.
6. **Test placement:** `Xvirus.Tests`, with a `ProjectReference` to `YaraTranslator`.

Still operational:

7. **False-positive policy:** generated patterns should be reviewed against a benign
   corpus before they are accepted into the cloud database. The tool reports the
   information needed for that review but does not automate the corpus decision.
8. **Publication:** merge accepted fragments into both cloud database copies and bump the
   server-side `HeurDB` / `HeurDB2` version.

---

## 14. Worked examples

The hex below is illustrative; real output is not shortened with `...`.

Input:

```yara
rule Example
{
    strings:
        $a = "foobar"
        $b = "bazqux"
    condition:
        all of them
}
```

Output (`foobar` = `666F6F626172`, `bazqux` = `62617A717578`):

```text
666F6F626172,YARA.Example,62617A717578
```

The anchor is `$a`; `$b` is a support dependency.

Single string:

```yara
rule Single
{
    strings:
        $a = "foobar"
    condition:
        $a
}
```

Output — the self-dependency makes the match report immediately:

```text
666F6F626172,YARA.Single,666F6F626172
```

OR:

```yara
condition:
    $a or $b
```

Output — one line per branch, each with a self-dependency:

```text
666F6F626172,YARA.Example,666F6F626172
62617A717578,YARA.Example,62617A717578
```

NOT:

```yara
condition:
    $a and not $b
```

Output:

```text
666F6F626172,YARA.Example,!62617A717578
```

2 of 3 (`foobar`, `bazqux`, `quux` = `71757578`):

```text
666F6F626172,YARA.Example,62617A717578
666F6F626172,YARA.Example,71757578
62617A717578,YARA.Example,71757578
```

Wide + ASCII:

```yara
$a = "foobar" ascii wide
```

Output — `foobar` = `666F6F626172`, wide =
`66006F006F006200610072`; both variants are emitted and each self-depends:

```text
666F6F626172,YARA.Example,666F6F626172
66006F006F006200610072,YARA.Example,66006F006F006200610072
```

Unsupported:

```yara
condition:
    filesize < 1MB and #a > 2 and $a at 0
```

Report:

```json
{
  "name": "Unsupported",
  "status": "skipped",
  "diagnostics": [
    { "code": "YARA2001", "message": "filesize is not representable by the heuristic engine" },
    { "code": "YARA2002", "message": "string count #a is not representable; only presence is supported" },
    { "code": "YARA2003", "message": "match offset constraint is not representable" }
  ]
}
```

With `--allow-approximations`, the same rule could be emitted as a presence-only line for
`$a`, with a `partial` status and all three warnings retained.

---

## 15. Definition of done for v1

- `dotnet build Xvirus.sln` succeeds with the new project.
- All unit, golden and compatibility tests pass.
- The parser handles the sample corpus without unhandled exceptions.
- `translate` produces valid fragments for the supported subset and a report that explains
  every skipped or partial rule.
- `validate` rejects malformed databases and accepts the generated fragments.
- Re-running a translation is idempotent.
- The README documents the supported subset, the engine patch decision, the
  `HeuristicsLevel` caveats and the publishing workflow.
- A reviewed set of rules has been merged into both cloud database copies, and the
  corresponding database version bump has been recorded.
