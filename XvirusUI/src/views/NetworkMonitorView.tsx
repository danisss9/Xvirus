import { useState, useEffect, useCallback, useMemo } from 'preact/hooks';
import { fetchNetworkConnections, NetworkConnection } from '../api/networkApi';
import { fetchRules, addBlockRule, removeRule } from '../api/rulesApi';
import { Rule } from '../model/Rule';
import { isFirewall } from '../services/env';

const IconRefresh = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
    fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <path d="M21 12a9 9 0 1 1-3-6.7" />
    <polyline points="21 3 21 9 15 9" />
  </svg>
);

const IconExpand = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
    fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <polyline points="6 9 12 15 18 9" />
  </svg>
);

const IconCollapse = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
    fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <polyline points="18 15 12 9 6 15" />
  </svg>
);

function ScoreBadge({ score }: { score: number }) {
  const pct = Math.round(score * 100);
  let cls = 'score-badge score-clean';
  if (score >= 0.7) cls = 'score-badge score-danger';
  else if (score >= 0.4) cls = 'score-badge score-warn';
  return <span class={cls}>{pct}%</span>;
}

function BlockToggle({ blocked, onClick }: { blocked: boolean; onClick: (e: any) => void }) {
  return (
    <button
      class={`nm-block-btn ${blocked ? 'is-blocked' : ''}`}
      title={blocked ? 'Remove firewall block' : 'Block network access'}
      onClick={(e: any) => { e.stopPropagation(); onClick(e); }}
    >
      {blocked ? 'Unblock' : 'Block'}
    </button>
  );
}

function ConnectionRow({ conn, expanded, blocked, onToggleBlock }: {
  conn: NetworkConnection;
  expanded: boolean;
  blocked: boolean;
  onToggleBlock?: (conn: NetworkConnection, blocked: boolean) => void;
}) {
  const canBlock = !!onToggleBlock && !!conn.filePath;

  if (!expanded) {
    return (
      <div class="nm-row nm-row-collapsed">
        <span class="nm-filename" title={conn.filePath}>
          {conn.fileName || <span class="nm-unknown">Unknown</span>}
        </span>
        <span class="nm-remote" title={conn.remoteAddress}>{conn.remoteAddress}</span>
        {blocked && <span class="nm-blocked-badge">Blocked</span>}
        <ScoreBadge score={conn.score} />
        {canBlock && <BlockToggle blocked={blocked} onClick={() => onToggleBlock!(conn, blocked)} />}
      </div>
    );
  }

  return (
    <div class="nm-row nm-row-expanded">
      <div class="nm-row-header">
        <span class="nm-filename" title={conn.filePath}>
          {conn.fileName || <span class="nm-unknown">Unknown</span>}
        </span>
        {blocked && <span class="nm-blocked-badge">Blocked</span>}
        <ScoreBadge score={conn.score} />
        {canBlock && <BlockToggle blocked={blocked} onClick={() => onToggleBlock!(conn, blocked)} />}
      </div>
      <div class="nm-fields">
        <div class="nm-field">
          <span class="nm-field-label">Protocol</span>
          <span class={`nm-proto nm-proto-${conn.protocol.toLowerCase()}`}>{conn.protocol}</span>
        </div>
        <div class="nm-field">
          <span class="nm-field-label">State</span>
          <span class="nm-field-value">{conn.state || '—'}</span>
        </div>
        <div class="nm-field">
          <span class="nm-field-label">PID</span>
          <span class="nm-field-value">{conn.pid}</span>
        </div>
        <div class="nm-field nm-field-full">
          <span class="nm-field-label">Local</span>
          <span class="nm-field-value nm-mono">{conn.localAddress}</span>
        </div>
        <div class="nm-field nm-field-full">
          <span class="nm-field-label">Remote</span>
          <span class="nm-field-value nm-mono">{conn.remoteAddress}</span>
        </div>
        <div class="nm-field nm-field-full">
          <span class="nm-field-label">Path</span>
          <span class="nm-field-value nm-path nm-mono">{conn.filePath || '—'}</span>
        </div>
      </div>
    </div>
  );
}

export default function NetworkMonitorView() {
  const [connections, setConnections] = useState<NetworkConnection[]>([]);
  const [rules, setRules] = useState<Rule[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [allExpanded, setAllExpanded] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const [conns, ruleList] = await Promise.all([
        fetchNetworkConnections(),
        isFirewall ? fetchRules() : Promise.resolve([] as Rule[]),
      ]);
      setConnections(conns);
      setRules(ruleList);
    } catch {
      setConnections([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, []);

  // Map of blocked program path (lowercased) → rule, for badge + unblock.
  const blockedMap = useMemo(() => {
    const m = new Map<string, Rule>();
    for (const r of rules) {
      if (r.type === 'block' && r.path) m.set(r.path.toLowerCase(), r);
    }
    return m;
  }, [rules]);

  const isBlocked = useCallback(
    (conn: NetworkConnection) => !!conn.filePath && blockedMap.has(conn.filePath.toLowerCase()),
    [blockedMap]
  );

  const toggleBlock = useCallback(async (conn: NetworkConnection, blocked: boolean) => {
    try {
      if (blocked) {
        const rule = blockedMap.get(conn.filePath.toLowerCase());
        if (rule) await removeRule(rule.id);
      } else {
        await addBlockRule(conn.filePath);
      }
      setRules(await fetchRules());
    } catch (error) {
      console.error('Failed to toggle firewall block:', error);
    }
  }, [blockedMap]);

  const q = search.toLowerCase();
  const filtered = connections.filter(c =>
    !q ||
    c.fileName.toLowerCase().includes(q) ||
    c.filePath.toLowerCase().includes(q) ||
    c.remoteAddress.toLowerCase().includes(q) ||
    c.localAddress.toLowerCase().includes(q) ||
    c.protocol.toLowerCase().includes(q) ||
    c.state.toLowerCase().includes(q) ||
    String(c.pid).includes(q)
  );

  return (
    <div class="view-container">
      <div class="card nm-card">
        <h2 class="title">Network Monitor</h2>

        <div class="search-bar">
          <div class="search-wrapper">
            <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24"
              fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"
              class="search-icon">
              <circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" />
            </svg>
            <input
              type="text"
              placeholder="Filter connections…"
              value={search}
              onInput={(e: any) => setSearch(e.currentTarget.value)}
              class="search-input"
            />
          </div>
          <button
            class="action-btn"
            title={allExpanded ? 'Collapse all' : 'Expand all'}
            onClick={() => setAllExpanded(v => !v)}
          >
            {allExpanded ? <IconCollapse /> : <IconExpand />}
          </button>
          <button
            class="action-btn"
            title="Reload"
            onClick={load}
            disabled={loading}
          >
            <IconRefresh />
          </button>
        </div>

        <div class="nm-list">
          {loading ? (
            <p class="loading-text">Loading connections…</p>
          ) : filtered.length === 0 ? (
            <p class="no-history">No connections found</p>
          ) : filtered.map((conn, i) => (
            <ConnectionRow
              key={`${conn.pid}-${conn.localAddress}-${conn.remoteAddress}-${i}`}
              conn={conn}
              expanded={allExpanded}
              blocked={isBlocked(conn)}
              onToggleBlock={isFirewall ? toggleBlock : undefined}
            />
          ))}
        </div>
      </div>
    </div>
  );
}
