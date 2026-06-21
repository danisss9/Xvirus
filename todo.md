# TODO — Anti-Malware & Firewall Feature Implementation

Phased plan covering both products, userland-only (ETW/netsh/IPHelper, no drivers), no cloud
file upload. Phase 1 ships low-risk polish; Phases 2 & 3 add the major detection and firewall
features; Phase 4 hardens and exposes everything.

## Phase 1 — Quick Wins & Polish (parallelizable)

- [x] 1. **Wire up `OnlyScanExecutables`** in `Scanner.ScanFile` — currently declared in
     `AppSettingsDTO` but unused. Short-circuit non-executables when the setting is on.
     _parallel with 2–4_
- [-] 2. **Configurable scheduled-scan path** — replace hardcoded `C:\` in
  `ScheduledScanService.MaybeRunAsync` with a new `AppSettingsDTO.ScheduledScanPath`
  (default `C:\`); surface in `SettingsApi` and Settings UI. _parallel_
- [x] 3. **Stale firewall-rule reconciliation** — extend `Rules.SyncEnforcement` to enumerate
     `Xvirus_Block_*` netsh rules, drop orphans, re-add missing; add `Firewall.ListBlockedRules`
     using `netsh advfirewall firewall show rule name=all` parsing. _parallel_
- [ ] 4. **Refactor `Aho.cs`** — resolve the two `// todo` markers (copy-paste dedup, perf
     check). _parallel_
- [ ] 5. **Flip `BehaviorProtection` default to `true`** in `AppSettingsDTO` after verifying
     the Office→script false-positive rate. _depends on Phase 4 verification_

## Phase 2 — Anti-Malware Major Features (userland)

- [x] 6. **Archive unpacking** — new `ArchiveExtractor` module (zip via
     `System.IO.Compression`; rar/7z via SharpCompress pending license check), integrated into
     `Scanner.ScanFile` behind `SettingsDTO.EnableArchiveScan` with depth/size guards.
     _can start parallel with Phase 1_
- [-] 7. **AI for non-PE files** — generalize `AI.ScanFile`/`GetFileImage` to scripts, Office
  docs, PDFs; update the `isExecutable` branch in `Scanner.ScanFile`. _parallel with 6_
- [ ] 8. **Expanded behavior protection** — add ransomware (mass rename/encrypt,
     `vssadmin`/`wbadmin`/`bcdedit`) and LOLBins (`rundll32`/`regsvr32`/`mshta`/`certutil`)
     patterns to `RealTimeProtection`. _depends on 5_
- [-] 9. **YARA rule import** — new `YaraEngine` module loaded from the database folder, called
  after heuristics, behind `SettingsDTO.EnableYara`. _parallel with 6–8_
- [-] 10. **Weighted heuristics scoring** — replace the flat `score < (5 - HeuristicsLevel)`
  threshold with per-pattern severity weights. _depends on 6_

## Phase 3 — Firewall Major Features (userland)

- [-] 11. **IP/port/domain rule model** — extend `Rule` with a `RuleKind` enum + fields; add
  `Firewall.BlockIp`/`BlockPort`/`BlockDomain`; dispatch in `Rules.ApplyEnforcement`.
  _parallel with 12–13_
- [x] 12. **Near-real-time monitor via IPHelper** — replace the 3s `netstat` poll in
      `NetworkRealTimeProtection` and `NetworkService` with P/Invoke
      `GetExtendedTcpTable`/`GetExtendedUdpTable` on a 500ms–1s timer plus ETW
      `Microsoft-Windows-Kernel-Network`. _parallel_
- [-] 13. **NetworkApi direct-blocking endpoints** — `POST /network/block-pid/{pid}`,
  `POST /network/block-ip`, `POST /network/kill-connection`; new `NetworkBlockService`.
  _depends on 11_
- [-] 14. **Remote-endpoint allow/block + DNS monitoring** — ETW `Microsoft-Windows-DNS-Client`
  per-process domain logging; per-IP/domain allow/deny in the rules store. _depends on 11, 12_
- [-] 15. **Rule profiles (Home/Public/Work)** — `AppSettingsDTO.FirewallProfile`, netsh rules
  scoped to active profile. _depends on 11_

## Phase 4 — Cross-Cutting & Verification

- [x] 16. **Self-defense hardening (userland)** — extend `SelfDefenseService` with `icacls`
      ACLs on binary/config/quarantine and restricted service-stop perms. _parallel with 2/3_
- [-] 17. **UI surfaces** — Settings toggles for `EnableArchiveScan`/`EnableYara`/
  `ScheduledScanPath`/`FirewallProfile` and IP/domain block controls in
  `NetworkMonitorView.tsx`/`SettingsView.tsx`. _depends on backend steps_
- [-] 18. **SDK/CLI exposure** — new settings and block-by-IP/port in `CSharpSDK`/`NativeSDK`/
  `NodeSDK`/`XvirusCLI`. _depends on backend steps_

## Relevant files

- `BaseLibrary/Modules/Scanner.cs` — `ScanFile` (OnlyScanExecutables, archive, AI-non-PE, YARA, weighted scoring)
- `BaseLibrary/Modules/AI.cs` — `ScanFile`, `GetFileImage` (generalize beyond PE)
- `BaseLibrary/Modules/Rules.cs` — `ApplyEnforcement`, `SyncEnforcement` (stale reconciliation, new rule kinds)
- `BaseLibrary/Modules/Firewall.cs` — add `BlockIp`/`BlockPort`/`BlockDomain`, `ListBlockedRules`
- `BaseLibrary/Modules/Aho.cs` — TODOs, perf
- `BaseLibrary/Model/Rule.cs` — `RuleKind` + fields
- `BaseLibrary/Model/SettingsDTO.cs` / `AppSettingsDTO.cs` — new flags, `ScheduledScanPath`, `FirewallProfile`, `BehaviorProtection` default
- `XvirusService/services/RealTimeProtection.cs` — expanded behavior patterns
- `XvirusService/services/NetworkRealTimeProtection.cs` / `NetworkService.cs` — IPHelper/ETW monitor
- `XvirusService/services/ScheduledScanService.cs` / `SelfDefenseService.cs`
- `XvirusService/api/NetworkApi.cs` / `RulesApi.cs` — new endpoints
- `XvirusUI/src/views/NetworkMonitorView.tsx` / `SettingsView.tsx`
- `CSharpSDK`/`NativeSDK`/`NodeSDK` `XvirusSDK.cs`, `XvirusCLI/Main.cs`

## Verification

1. Create a `BaseLibrary.Tests` xUnit project (none exists) — cover each new `Scanner` branch,
   `OnlyScanExecutables` short-circuit, archive depth/size guards, weighted-score thresholds.
2. Firewall rule round-trip — add/remove IP/port/domain rules via API; assert via
   `netsh show rule name=all` that `Xvirus_Block_*` rules appear/disappear; assert stale-rule
   reconciliation removes orphans.
3. Network monitor latency benchmark — `GetExtendedTcpTable` vs `netstat`; verify a 1s-lived
   connection is caught by the new monitor and missed by the old.
4. Behavior protection — simulate Office→powershell and a mass-file-rename; assert
   `ThreatAlertService` fires and (firewall product) a block rule is created.
5. Manual UI smoke — toggle each new Settings control, run a scheduled scan on a custom path,
   block/unblock an IP from Network Monitor, confirm SSE events.
6. SDK/CLI smoke — call each new function from the example projects and CLI; confirm JSON shape
   matches existing `ScanResult`/`Rule` contracts.
7. `dotnet build Xvirus.sln` clean; AOT-publish `XvirusService` and `NativeSDK` with no trim
   warnings (new code must be AOT-safe — avoid reflection).

## Decisions

- Both products in scope, equally.
- Userland only — no minifilter, no WFP driver.
- Cloud file-upload out of scope; `CloudReputation` stays hash-only/fail-open.
- Geo-IP blocking deferred (needs a geo-IP DB dependency).

## Further Considerations

1. **YARA dependency** — native yara bindings (P/Invoke, verify AOT-publishes) vs extending the
   existing Aho-Corasick pattern format. Recommend native bindings; fallback is extending
   Aho-Corasick.
2. **Domain blocking mechanism** — netsh can't block by domain. Recommend ETW DNS-Client +
   per-process block on resolution (option C) over hosts-file redirect (global effect, needs
   elevation) or IP pinning (stale).
3. **Archive extraction security** — enforce max-depth, max-total-extracted-size, and path
   canonicalization to defeat zip-slip / zip bombs before any extraction.
