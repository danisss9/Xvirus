import { useState, useEffect, useCallback } from 'preact/hooks';
import { fetchNetworkConnections, NetworkConnection } from '../api/networkApi';

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

function ConnectionRow({ conn, expanded }: { conn: NetworkConnection; expanded: boolean }) {
  if (!expanded) {
    return (
      <div class="nm-row nm-row-collapsed">
        <span class="nm-filename" title={conn.filePath}>
          {conn.fileName || <span class="nm-unknown">Unknown</span>}
        </span>
        <span class="nm-remote" title={conn.remoteAddress}>{conn.remoteAddress}</span>
        <ScoreBadge score={conn.score} />
      </div>
    );
  }

  return (
    <div class="nm-row nm-row-expanded">
      <div class="nm-row-header">
        <span class="nm-filename" title={conn.filePath}>
          {conn.fileName || <span class="nm-unknown">Unknown</span>}
        </span>
        <ScoreBadge score={conn.score} />
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
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [allExpanded, setAllExpanded] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const data = await fetchNetworkConnections();
      setConnections(data);
    } catch {
      setConnections([]);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); }, []);

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
            <ConnectionRow key={`${conn.pid}-${conn.localAddress}-${conn.remoteAddress}-${i}`} conn={conn} expanded={allExpanded} />
          ))}
        </div>
      </div>
    </div>
  );
}
