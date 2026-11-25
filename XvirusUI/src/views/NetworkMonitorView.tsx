import { useState, useEffect, useCallback, useMemo } from 'preact/hooks';
import {
  fetchNetworkConnections,
  NetworkConnection,
  blockPid,
  blockIp,
  killConnection,
} from '../api/networkApi';
import { fetchRules, addBlockRule, removeRule } from '../api/rulesApi';
import { Rule } from '../model/Rule';
import { isFirewall } from '../services/env';

type FilterKind = 'all' | 'established' | 'blocked' | 'suspicious';

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

function extractHost(endpoint: string): string {
  if (!endpoint || endpoint === '*') return '';
  const idx = endpoint.lastIndexOf(':');
  if (idx <= 0) return endpoint;
  return endpoint.slice(0, idx).replace(/^\[|\]$/g, '');
}

function isBlockableIp(ip: string): boolean {
  if (!ip || ip === '*' || ip === '0.0.0.0' || ip === '::' || ip === '127.0.0.1' || ip === '::1')
    return false;
  return true;
}

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
      title={blocked ? 'Remove program firewall block' : 'Block program network access'}
      onClick={(e: any) => { e.stopPropagation(); onClick(e); }}
    >
      {blocked ? 'Unblock' : 'Block'}
    </button>
  );
}

function ConnectionRow({
  conn,
  expanded,
  blocked,
  ipBlocked,
  onToggleBlock,
  onToggleIpBlock,
  onKill,
}: {
  conn: NetworkConnection;
  expanded: boolean;
  blocked: boolean;
  ipBlocked: boolean;
  onToggleBlock?: (conn: NetworkConnection, blocked: boolean) => void;
  onToggleIpBlock?: (remoteIp: string, ipBlocked: boolean) => void;
  onKill?: (conn: NetworkConnection) => void;
}) {
  const canBlock = !!onToggleBlock && !!conn.filePath;
  const remoteIp = extractHost(conn.remoteAddress);
  const canBlockIp = !!onToggleIpBlock && isBlockableIp(remoteIp);
  const canKill = !!onKill && conn.protocol === 'TCP' && conn.state === 'ESTABLISHED' && isBlockableIp(remoteIp);
  const anyBlocked = blocked || ipBlocked;
  const remoteLabel = conn.remoteDomain ? `${conn.remoteDomain}` : conn.remoteAddress;

  if (!expanded) {
    return (
      <div class="nm-row nm-row-collapsed">
        <span class="nm-filename" title={conn.filePath}>
          {conn.fileName || <span class="nm-unknown">Unknown</span>}
        </span>
        <span class="nm-remote" title={conn.remoteDomain ? `${conn.remoteDomain} (${conn.remoteAddress})` : conn.remoteAddress}>
          {remoteLabel}
        </span>
        {anyBlocked && <span class="nm-blocked-badge">{ipBlocked && !blocked ? 'IP Blocked' : 'Blocked'}</span>}
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
        {anyBlocked && <span class="nm-blocked-badge">{ipBlocked && !blocked ? 'IP Blocked' : 'Blocked'}</span>}
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
        {conn.remoteDomain && (
          <div class="nm-field nm-field-full">
            <span class="nm-field-label">Domain</span>
            <span class="nm-field-value nm-domain">{conn.remoteDomain}</span>
          </div>
        )}
        <div class="nm-field nm-field-full">
          <span class="nm-field-label">Path</span>
          <span class="nm-field-value nm-path nm-mono">{conn.filePath || '—'}</span>
        </div>
      </div>
      {(canBlockIp || canKill) && (
        <div class="nm-row-actions">
          {canBlockIp && (
            <button
              class={`nm-block-btn ${ipBlocked ? 'is-blocked' : ''}`}
              onClick={() => onToggleIpBlock!(remoteIp, ipBlocked)}
              title={ipBlocked ? `Unblock IP ${remoteIp}` : `Block remote IP ${remoteIp}`}
            >
              {ipBlocked ? 'Unblock IP' : 'Block IP'}
            </button>
          )}
          {canKill && (
            <button
              class="nm-block-btn nm-kill-btn"
              onClick={() => onKill!(conn)}
              title="Terminate this TCP connection"
            >
              Kill Connection
            </button>
          )}
        </div>
      )}
    </div>
  );
}

export default function NetworkMonitorView() {
  const [connections, setConnections] = useState<NetworkConnection[]>([]);
  const [rules, setRules] = useState<Rule[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [filter, setFilter] = useState<FilterKind>('all');
  const [allExpanded, setAllExpanded] = useState(false);
  const [autoRefresh, setAutoRefresh] = useState(false);
  const [statusMsg, setStatusMsg] = useState<string | null>(null);

  const flashStatus = useCallback((msg: string) => {
    setStatusMsg(msg);
    setTimeout(() => setStatusMsg(prev => (prev === msg ? null : prev)), 3000);
  }, []);

  const load = useCallback(async (silent = false) => {
    if (!silent) setLoading(true);
    try {
      const [conns, ruleList] = await Promise.all([
        fetchNetworkConnections(),
        isFirewall ? fetchRules() : Promise.resolve([] as Rule[]),
      ]);
      setConnections(conns);
      setRules(ruleList);
    } catch {
      if (!silent) setConnections([]);
    } finally {
      if (!silent) setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, [load]);

  useEffect(() => {
    if (!autoRefresh) return;
    const id = setInterval(() => { load(true); }, 3000);
    return () => clearInterval(id);
  }, [autoRefresh, load]);

  const blockedProgramMap = useMemo(() => {
    const m = new Map<string, Rule>();
    for (const r of rules) {
      if (r.type === 'block' && (!r.kind || r.kind === 'program') && r.path) {
        m.set(r.path.toLowerCase(), r);
      }
    }
    return m;
  }, [rules]);

  const blockedIpMap = useMemo(() => {
    const m = new Map<string, Rule>();
    for (const r of rules) {
      if (r.type === 'block' && r.kind === 'ip' && r.path) {
        m.set(r.path.toLowerCase(), r);
      }
    }
    return m;
  }, [rules]);

  const isProgramBlocked = useCallback(
    (conn: NetworkConnection) => !!conn.filePath && blockedProgramMap.has(conn.filePath.toLowerCase()),
    [blockedProgramMap],
  );

  const isIpBlocked = useCallback(
    (conn: NetworkConnection) => {
      const host = extractHost(conn.remoteAddress).toLowerCase();
      return !!host && blockedIpMap.has(host);
    },
    [blockedIpMap],
  );

  const toggleProgramBlock = useCallback(async (conn: NetworkConnection, blocked: boolean) => {
    try {
      if (blocked) {
        const rule = blockedProgramMap.get(conn.filePath.toLowerCase());
        if (rule) await removeRule(rule.id);
        flashStatus(`Unblocked ${conn.fileName || conn.filePath}`);
      } else if (conn.pid > 4) {
        const res = await blockPid(conn.pid);
        flashStatus(res.message);
      } else {
        await addBlockRule(conn.filePath);
        flashStatus(`Blocked ${conn.fileName || conn.filePath}`);
      }
      await load(true);
    } catch (error) {
      console.error('Failed to toggle firewall block:', error);
      flashStatus('Failed to update program block rule');
    }
  }, [blockedProgramMap, flashStatus, load]);

  const toggleIpBlock = useCallback(async (remoteIp: string, currentlyBlocked: boolean) => {
    try {
      if (currentlyBlocked) {
        const rule = blockedIpMap.get(remoteIp.toLowerCase());
        if (rule) await removeRule(rule.id);
        flashStatus(`Unblocked IP ${remoteIp}`);
      } else {
        const res = await blockIp({ ip: remoteIp, killConnections: true });
        flashStatus(res.message);
      }
      await load(true);
    } catch (error) {
      console.error('Failed to toggle IP block:', error);
      flashStatus('Failed to update IP block rule');
    }
  }, [blockedIpMap, flashStatus, load]);

  const handleKill = useCallback(async (conn: NetworkConnection) => {
    try {
      const res = await killConnection({
        localAddress: conn.localAddress,
        remoteAddress: conn.remoteAddress,
      });
      flashStatus(res.message);
      await load(true);
    } catch (error) {
      console.error('Failed to kill connection:', error);
      flashStatus('Could not terminate connection');
    }
  }, [flashStatus, load]);

  const q = search.toLowerCase();
  const filtered = connections.filter(c => {
    if (filter === 'established' && c.state !== 'ESTABLISHED') return false;
    if (filter === 'blocked' && !isProgramBlocked(c) && !isIpBlocked(c)) return false;
    if (filter === 'suspicious' && c.score < 0.4) return false;
    if (!q) return true;
    return (
      c.fileName.toLowerCase().includes(q) ||
      c.filePath.toLowerCase().includes(q) ||
      c.remoteAddress.toLowerCase().includes(q) ||
      (c.remoteDomain ?? '').toLowerCase().includes(q) ||
      c.localAddress.toLowerCase().includes(q) ||
      c.protocol.toLowerCase().includes(q) ||
      c.state.toLowerCase().includes(q) ||
      String(c.pid).includes(q)
    );
  });

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
              placeholder="Filter process, IP, domain…"
              value={search}
              onInput={(e: any) => setSearch(e.currentTarget.value)}
              class="search-input"
            />
          </div>
          <button
            class={`action-btn nm-auto-btn ${autoRefresh ? 'active' : ''}`}
            title={autoRefresh ? 'Disable auto-refresh (3s)' : 'Enable auto-refresh (3s)'}
            onClick={() => setAutoRefresh(v => !v)}
          >
            {autoRefresh ? 'Live' : 'Auto'}
          </button>
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
            onClick={() => load(false)}
            disabled={loading}
          >
            <IconRefresh />
          </button>
        </div>

        <div class="nm-filter-bar">
          {(['all', 'established', 'blocked', 'suspicious'] as FilterKind[]).map(kind => (
            <button
              key={kind}
              class={`nm-filter-pill ${filter === kind ? 'active' : ''}`}
              onClick={() => setFilter(kind)}
            >
              {kind === 'all' ? `All (${connections.length})` : kind[0].toUpperCase() + kind.slice(1)}
            </button>
          ))}
        </div>

        {statusMsg && <div class="nm-toast">{statusMsg}</div>}

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
              blocked={isProgramBlocked(conn)}
              ipBlocked={isIpBlocked(conn)}
              onToggleBlock={isFirewall ? toggleProgramBlock : undefined}
              onToggleIpBlock={isFirewall ? toggleIpBlock : undefined}
              onKill={isFirewall ? handleKill : undefined}
            />
          ))}
        </div>
      </div>
    </div>
  );
}
