import { useState, useEffect, useRef } from 'preact/hooks';
import { HistoryEntry } from '../model/HistoryEntry';
import { Rule } from '../model/Rule';
import { QuarantineEntry } from '../model/QuarantineEntry';

const IconTrash = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
    fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <polyline points="3 6 5 6 21 6" />
    <path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6" />
    <path d="M10 11v6M14 11v6" />
    <path d="M9 6V4a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2" />
  </svg>
);

const IconRestore = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
    fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8" />
    <path d="M3 3v5h5" />
  </svg>
);

const IconPlus = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
    fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <line x1="12" y1="5" x2="12" y2="19" />
    <line x1="5" y1="12" x2="19" y2="12" />
  </svg>
);

const IconClearAll = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 24 24"
    fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
    <path d="M3 6h18" /><path d="M8 6V4h8v2" /><path d="M19 6l-1 14H6L5 6" />
    <line x1="10" y1="11" x2="10" y2="17" /><line x1="14" y1="11" x2="14" y2="17" />
  </svg>
);


export default function HistoryView() {
  const [entries, setEntries] = useState<HistoryEntry[]>([]);
  const [rules, setRules] = useState<Rule[]>([]);
  const [quarantine, setQuarantine] = useState<QuarantineEntry[]>([]);
  const [loading, setLoading] = useState(true);
  const [search, setSearch] = useState('');
  const [page, setPage] = useState(0);
  const [confirmInfo, setConfirmInfo] = useState<{
    kind: 'clear' | 'deleteRule' | 'restoreFile' | 'deleteQuarantine';
    id?: string;
    message: string;
    onConfirm: () => void;
  } | null>(null);
  const confirmRef = useRef<HTMLDivElement | null>(null);

  const [ruleMenuOpen, setRuleMenuOpen] = useState(false);
  const ruleMenuRef = useRef<HTMLDivElement | null>(null);

  const sliderRef = useRef<HTMLDivElement | null>(null);
  const viewportRef = useRef<HTMLDivElement | null>(null);
  const startX = useRef<number | null>(null);
  const currentTranslate = useRef(0);
  const isDragging = useRef(false);

  useEffect(() => {
    const fetchAll = async () => {
      try {
        const [histRes, rulesRes, quarRes] = await Promise.allSettled([
          fetch('http://localhost:5236/history').then(r => r.json()),
          fetch('http://localhost:5236/rules').then(r => r.json()),
          fetch('http://localhost:5236/quarantine').then(r => r.json()),
        ]);
        if (histRes.status === 'fulfilled') setEntries(histRes.value || []);
        if (rulesRes.status === 'fulfilled') setRules(rulesRes.value || []);
        if (quarRes.status === 'fulfilled') setQuarantine(quarRes.value || []);
      } catch (e) {
        console.error('Failed to fetch data:', e);
      } finally {
        setLoading(false);

           // provide example data so the UI isn't completely empty when the backend
        // can't be reached.  This makes it easier to see the layout while
        // developing or when offline.
        setEntries([{
          type: 'Error',
          timestamp: Date.now(),
          details: 'Unable to load scan history',
          filesScanned: 0,
          threatsFound: 0,
        }]);
        setRules([
          { id: 'example-rule-1', name: 'Example rule 1', path: 'C:\\path\\to\\exclude1' },
          { id: 'example-rule-2', name: 'Example rule 2', path: 'C:\\path\\to\\exclude2' },
          { id: 'example-rule-3', name: 'Example rule 3', path: 'C:\\path\\to\\exclude3' },
          { id: 'example-rule-4', name: 'Example rule 4', path: 'C:\\path\\to\\exclude4' },
          { id: 'example-rule-5', name: 'Example rule 5', path: 'C:\\path\\to\\exclude5' },
          { id: 'example-rule-6', name: 'Example rule 6', path: 'C:\\path\\to\\exclude6' },
          { id: 'example-rule-7', name: 'Example rule 7', path: 'C:\\path\\to\\exclude7' },
          { id: 'example-rule-8', name: 'Example rule 8', path: 'C:\\path\\to\\exclude8' },
          { id: 'example-rule-9', name: 'Example rule 9', path: 'C:\\path\\to\\exclude9' },
          { id: 'example-rule-10', name: 'Example rule 10', path: 'C:\\path\\to\\exclude10' },
        ]);
        setQuarantine([{
          id: 'example-file',
          name: 'infected.exe',
          path: 'C:\\path\\to\\infected.exe',
        }]);
      }
    };
    fetchAll();
  }, []);

  const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));

  const goToPage = (index: number) => {
    setPage(clamp(index, 0, 2));
    setSearch('');
  };

  const dragThreshold = 8; // pixels before we treat it as a drag

  const onPointerDown = (e: any) => {
    // ignore interactions originating from buttons or popovers so clicks
    // don't inadvertently start a slide gesture
    const target = e.target as HTMLElement;
    if (target.closest('button, .confirm-popover')) {
      return;
    }
    startX.current = e.touches ? e.touches[0].clientX : e.clientX;
    isDragging.current = false; // only become true once moved enough
  };

  const onPointerMove = (e: any) => {
    if (startX.current == null) return;
    const clientX = e.touches ? e.touches[0].clientX : e.clientX;
    const dx = clientX - startX.current;

    if (!isDragging.current) {
      // only start actual dragging when threshold exceeded
      if (Math.abs(dx) < dragThreshold) return;
      isDragging.current = true;
      if (sliderRef.current) sliderRef.current.style.transition = 'none';
    }

    const width = viewportRef.current?.clientWidth || 1;
    currentTranslate.current = -page * width + dx;
    if (sliderRef.current) sliderRef.current.style.transform = `translateX(${currentTranslate.current}px)`;
  };

  const onPointerUp = () => {
    if (!isDragging.current) {
      startX.current = null;
      return;
    }
    isDragging.current = false;
    const width = viewportRef.current?.clientWidth || 1;
    const dx = currentTranslate.current + page * width;
    const threshold = width * 0.18;
    if (dx < -threshold && page < 2) goToPage(page + 1);
    else if (dx > threshold && page > 0) goToPage(page - 1);
    else goToPage(page);
    if (sliderRef.current) sliderRef.current.style.transition = '';
    startX.current = null;
  };

  useEffect(() => {
    if (sliderRef.current && viewportRef.current) {
      const width = viewportRef.current.clientWidth;
      sliderRef.current.style.transition = 'transform 320ms cubic-bezier(.2,.9,.2,1)';
      sliderRef.current.style.transform = `translateX(${-page * width}px)`;
    }
  }, [page]);

  useEffect(() => {
    if (!confirmInfo) return;
    const handler = (e: MouseEvent) => {
      if (confirmRef.current && !confirmRef.current.contains(e.target as Node)) {
        setConfirmInfo(null);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [confirmInfo]);

  // close rule menu when clicking outside
  useEffect(() => {
    if (!ruleMenuOpen) return;
    const handler = (e: MouseEvent) => {
      if (ruleMenuRef.current && !ruleMenuRef.current.contains(e.target as Node)) {
        setRuleMenuOpen(false);
      }
    };
    document.addEventListener('mousedown', handler);
    return () => document.removeEventListener('mousedown', handler);
  }, [ruleMenuOpen]);


  const clearHistory = async () => {
    try {
      await fetch('http://localhost:5236/history', { method: 'DELETE' });
      setEntries([]);
    } catch (e) { console.error(e); }
  };

  // type can be 'allow' or 'block'; used for menu selection.
  const addRule = async (type: 'allow' | 'block' = 'allow') => {
    const path = prompt(`Enter file or folder path to ${type} rule:`);
    if (!path) return;
    const name = path.split(/[/\\]/).pop() || path;
    try {
      const res = await fetch('http://localhost:5236/rules', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name, path, type }),
      });
      const newRule = await res.json();
      setRules(prev => [...prev, newRule]);
    } catch (e) { console.error(e); }
  };

  const handleConfirm = () => {
    if (confirmInfo) {
      confirmInfo.onConfirm();
      setConfirmInfo(null);
    }
  };

  const deleteRule = async (id: string) => {
    try {
      await fetch(`http://localhost:5236/rules/${id}`, { method: 'DELETE' });
      setRules(prev => prev.filter(r => r.id !== id));
    } catch (e) { console.error(e); }
  };

  const restoreFile = async (id: string) => {
    try {
      await fetch(`http://localhost:5236/quarantine/${id}/restore`, { method: 'POST' });
      setQuarantine(prev => prev.filter(q => q.id !== id));
    } catch (e) { console.error(e); }
  };

  const deleteQuarantine = async (id: string) => {
    if (!confirm('Permanently delete this file?')) return;
    try {
      await fetch(`http://localhost:5236/quarantine/${id}`, { method: 'DELETE' });
      setQuarantine(prev => prev.filter(q => q.id !== id));
    } catch (e) { console.error(e); }
  };

  const formatDate = (timestamp: number) => {
    const date = new Date(timestamp);
    const today = new Date();
    const yesterday = new Date(today);
    yesterday.setDate(yesterday.getDate() - 1);
    if (date.toDateString() === today.toDateString())
      return `Today, ${date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
    if (date.toDateString() === yesterday.toDateString())
      return `Yesterday, ${date.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}`;
    return date.toLocaleDateString();
  };

  const q = search.toLowerCase();
  const filteredEntries = entries.filter(e =>
    e.type.toLowerCase().includes(q) || e.details.toLowerCase().includes(q)
  );
  const filteredRules = rules.filter(r =>
    r.name.toLowerCase().includes(q) || r.path.toLowerCase().includes(q)
  );
  const filteredQuarantine = quarantine.filter(f =>
    f.name.toLowerCase().includes(q) || f.path.toLowerCase().includes(q)
  );

  const pageTitles = ['History', 'Rules', 'Quarantine'];

  if (loading) {
    return (
      <div class="view-container">
        <div class="card history-card">
          <p class="loading-text">Loading...</p>
        </div>
      </div>
    );
  }

  return (
    <div class="view-container">
      <div
        class="card history-card"
      >
        <h2 class="title">{pageTitles[page]}</h2>

        {/* Search + action button */}
        <div class="search-bar">
          <div class="search-wrapper">
            <svg xmlns="http://www.w3.org/2000/svg" width="15" height="15" viewBox="0 0 24 24"
              fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"
              class="search-icon">
              <circle cx="11" cy="11" r="8" /><line x1="21" y1="21" x2="16.65" y2="16.65" />
            </svg>
            <input
              type="text"
              placeholder={`Search ${pageTitles[page].toLowerCase()}…`}
              value={search}
              onInput={(e: any) => setSearch(e.currentTarget.value)}
              class="search-input"
            />
          </div>
          {page === 0 && (
            <div style="position:relative;">
              <button
                onPointerDown={e => e.stopPropagation()}
                onClick={() => setConfirmInfo({
                  kind: 'clear',
                  message: 'Clear all history?',
                  onConfirm: clearHistory,
                })}
                title="Clear all history"
                class="action-btn btn-danger"
              >
                <IconClearAll />
              </button>
              {confirmInfo?.kind === 'clear' && (
                <div ref={confirmRef} class="confirm-popover">
                  <p>{confirmInfo.message}</p>
                  <div class="row-center-gap">
                    <button onClick={handleConfirm} class="action-btn btn-danger">Yes</button>
                    <button onClick={() => setConfirmInfo(null)} class="action-btn">No</button>
                  </div>
                </div>
              )}
            </div>
          )}
          {page === 1 && (
            <div style="position:relative;">
              <button
                onPointerDown={e => e.stopPropagation()}
                onClick={() => setRuleMenuOpen(o => !o)}
                title="Add rule"
                class="action-btn btn-bg-teal"
              >
                <IconPlus />
              </button>
              {ruleMenuOpen && (
                <div ref={ruleMenuRef} class="rule-menu">
                  <p>Add rule</p>
                  <div class="row-center-gap">
                    <button onClick={() => { addRule('allow'); setRuleMenuOpen(false); }} class="action-btn">Allow</button>
                    <button onClick={() => { addRule('block'); setRuleMenuOpen(false); }} class="action-btn">Block</button>
                  </div>
                </div>
              )}
            </div>
          )}
        </div>

        {/* Swipeable pages */}
        <div
          class="settings-slider-viewport"
          ref={viewportRef}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerCancel={onPointerUp}
          onTouchStart={onPointerDown}
          onTouchMove={onPointerMove}
          onTouchEnd={onPointerUp}
        >
          <div class="settings-slider" ref={sliderRef}>

            {/* Page 1 – History */}
            <section class="settings-page slider-page">
              {filteredEntries.length === 0 ? (
                <p class="no-history">No history found</p>
              ) : filteredEntries.map((entry, i) => (
                <div key={i} class="list-item">
                  <div class="history-header">
                    <span class="history-type">{entry.type}</span>
                    <span class="history-date">{formatDate(entry.timestamp)}</span>
                  </div>
                  <p class="history-details">
                    {entry.details}
                  </p>
                  {entry.filesScanned > 0 && (
                    <p class="history-stats">
                      {entry.filesScanned} files &bull; {entry.threatsFound} threats
                    </p>
                  )}
                </div>
              ))}
            </section>

            {/* Page 2 – Rules */}
            <section class="settings-page slider-page">
              {filteredRules.length === 0 ? (
                <p class="no-history">No rules</p>
              ) : filteredRules.map((rule) => (
                <div key={rule.id} class="list-item row-center-gap">
                  <div class="flex-1">
                    <p class="item-title">
                      {rule.name}
                    </p>
                    <p class="item-path">
                      {rule.path}
                    </p>
                  </div>
                  <div style="position:relative;">
                    <button
                      onClick={() => setConfirmInfo({
                        kind: 'deleteRule',
                        id: rule.id,
                        message: 'Delete this rule?',
                        onConfirm: () => deleteRule(rule.id),
                      })}
                      title="Remove rule"
                      class="icon-btn btn-danger"
                    >
                      <IconTrash />
                    </button>
                    {confirmInfo?.kind === 'deleteRule' && confirmInfo.id === rule.id && (
                      <div ref={confirmRef} class="confirm-popover">
                        <p>{confirmInfo.message}</p>
                        <div class="row-center-gap">
                          <button onClick={handleConfirm} class="action-btn btn-danger">Yes</button>
                          <button onClick={() => setConfirmInfo(null)} class="action-btn">No</button>
                        </div>
                      </div>
                    )}
                  </div>
                </div>
              ))}
            </section>

            {/* Page 3 – Quarantine */}
            <section class="settings-page slider-page">
              {filteredQuarantine.length === 0 ? (
                <p class="no-history">No quarantined files</p>
              ) : filteredQuarantine.map((file) => (
                <div key={file.id} class="list-item row-center-gap">
                  <div class="flex-1">
                    <p class="item-title">
                      {file.name}
                    </p>
                    <p class="item-path">
                      {file.path}
                    </p>
                  </div>
                  <div class="actions-row">
                    <div style="position:relative;">
                      <button
                        onClick={() => setConfirmInfo({
                          kind: 'restoreFile',
                          id: file.id,
                          message: 'Restore this file? ',
                          onConfirm: () => restoreFile(file.id),
                        })}
                        title="Restore file"
                        class="icon-btn btn-teal"
                      >
                        <IconRestore />
                      </button>
                      {confirmInfo?.kind === 'restoreFile' && confirmInfo.id === file.id && (
                        <div ref={confirmRef} class="confirm-popover">
                          <p>{confirmInfo.message}</p>
                          <div class="row-center-gap">
                            <button onClick={handleConfirm} class="action-btn btn-teal">Yes</button>
                            <button onClick={() => setConfirmInfo(null)} class="action-btn">No</button>
                          </div>
                        </div>
                      )}
                    </div>
                    <div style="position:relative;">
                      <button
                        onClick={() => setConfirmInfo({
                          kind: 'deleteQuarantine',
                          id: file.id,
                          message: 'Permanently delete this file?',
                          onConfirm: () => deleteQuarantine(file.id),
                        })}
                        title="Delete permanently"
                        class="icon-btn btn-danger"
                      >
                        <IconTrash />
                      </button>
                      {confirmInfo?.kind === 'deleteQuarantine' && confirmInfo.id === file.id && (
                        <div ref={confirmRef} class="confirm-popover">
                          <p>{confirmInfo.message}</p>
                          <div class="row-center-gap">
                            <button onClick={handleConfirm} class="action-btn btn-danger">Yes</button>
                            <button onClick={() => setConfirmInfo(null)} class="action-btn">No</button>
                          </div>
                        </div>
                      )}
                    </div>
                  </div>
                </div>
              ))}
            </section>

          </div>
        </div>

        {/* Dots */}
        <div class="settings-dots">
          {[0, 1, 2].map(i => (
            <button
              key={i}
              class={`dot ${i === page ? 'active' : ''}`}
              aria-label={`Go to ${pageTitles[i]}`}
              title={pageTitles[i]}
              onClick={() => goToPage(i)}
            />
          ))}
        </div>
      </div>
    </div>
  );
}
