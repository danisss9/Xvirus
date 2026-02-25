import { useState, useEffect, useRef } from 'preact/hooks';
import { HistoryEntry } from '../model/HistoryEntry';
import { Rule } from '../model/Rule';
import { QuarantineEntry } from '../model/QuarantineEntry';
import { fetchHistory, clearHistory as apiClearHistory } from '../api/historyApi';
import { fetchRules, addAllowRule, addBlockRule, removeRule } from '../api/rulesApi';
import { fetchQuarantine, deleteQuarantine as apiDeleteQuarantine, restoreQuarantine as apiRestoreQuarantine } from '../api/quarantineApi';
import { getFilePath } from '../services/neutralino';

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

const IconChevron = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="12" height="12" viewBox="0 0 24 24"
    fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
    <polyline points="6 9 12 15 18 9" />
  </svg>
);

const IconWarning = () => (
  <svg xmlns="http://www.w3.org/2000/svg" width="28" height="28" viewBox="0 0 24 24"
    fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
    <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z" />
    <line x1="12" y1="9" x2="12" y2="13" />
    <line x1="12" y1="17" x2="12.01" y2="17" />
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
    title: string;
    message: string;
    confirmLabel: string;
    variant: 'danger' | 'teal';
    onConfirm: () => void;
  } | null>(null);

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
          fetchHistory(),
          fetchRules(),
          fetchQuarantine(),
        ]);
        if (histRes.status === 'fulfilled') setEntries(histRes.value || []);
        if (rulesRes.status === 'fulfilled') {
          setRules(rulesRes.value);
        }
        if (quarRes.status === 'fulfilled') setQuarantine(quarRes.value || []);
      } catch (e) {
        console.error('Failed to fetch data:', e);
      } finally {
        setLoading(false);
      }
    };
    fetchAll();
  }, []);

  const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));

  const goToPage = (index: number) => {
    setPage(clamp(index, 0, 2));
    setSearch('');
  };

  const dragThreshold = 8;

  const onPointerDown = (e: any) => {
    const target = e.target as HTMLElement;
    if (target.closest('button, .confirm-modal')) {
      return;
    }
    startX.current = e.touches ? e.touches[0].clientX : e.clientX;
    isDragging.current = false;
  };

  const onPointerMove = (e: any) => {
    if (startX.current == null) return;
    const clientX = e.touches ? e.touches[0].clientX : e.clientX;
    const dx = clientX - startX.current;

    if (!isDragging.current) {
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
      await apiClearHistory();
      setEntries([]);
    } catch (e) { console.error(e); }
  };

  const addRule = async (type: 'allow' | 'block' = 'allow') => {
    const path = await getFilePath();
    if (!path) return;
    try {
      const newRule = type === 'allow'
        ? await addAllowRule(path)
        : await addBlockRule(path);
      const ruleWithType = { ...newRule, type };
      setRules(prev => [...prev, ruleWithType]);
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
      await removeRule(id);
      setRules(prev => prev.filter(r => r.id !== id));
    } catch (e) { console.error(e); }
  };

  const restoreFile = async (id: string) => {
    try {
      await apiRestoreQuarantine(id);
      setQuarantine(prev => prev.filter(q => q.id !== id));
    } catch (e) { console.error(e); }
  };

  const deleteQuarantine = async (id: string) => {
    try {
      await apiDeleteQuarantine(id);
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
    r.path.toLowerCase().includes(q) ||
    (r.type ?? '').toLowerCase().includes(q)
  );
  const filteredQuarantine = quarantine.filter(f =>
    f.originalFileName.toLowerCase().includes(q) ||
    f.originalFilePath.toLowerCase().includes(q)
  );

  const pageTitles = ['History', 'Rules', 'Quarantine'];

  return (
    <div class="view-container">
      <div class="card history-card">
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
            <button
              onPointerDown={e => e.stopPropagation()}
              onClick={() => setConfirmInfo({
                kind: 'clear',
                title: 'Clear all history',
                message: 'This will permanently remove all scan history entries.',
                confirmLabel: 'Clear all',
                variant: 'danger',
                onConfirm: clearHistory,
              })}
              title="Clear all history"
              class="action-btn btn-danger"
            >
              <IconClearAll />
            </button>
          )}

          {page === 1 && (
            <div style="position:relative;" ref={ruleMenuRef}>
              <button
                onPointerDown={e => e.stopPropagation()}
                onClick={() => setRuleMenuOpen(o => !o)}
                title="Add rule"
                class="action-btn btn-bg-teal add-rule-btn"
              >
                <IconPlus />
                <IconChevron />
              </button>
              {ruleMenuOpen && (
                <div class="rule-dropdown">
                  <button
                    class="rule-dropdown-item"
                    onClick={() => { addRule('allow'); setRuleMenuOpen(false); }}
                  >
                    <span class="rule-dropdown-badge badge-allow">allow</span>
                    Add allow rule
                  </button>
                  <button
                    class="rule-dropdown-item"
                    onClick={() => { addRule('block'); setRuleMenuOpen(false); }}
                  >
                    <span class="rule-dropdown-badge badge-block">block</span>
                    Add block rule
                  </button>
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
              {loading ? (
                <p class="loading-text">Loading...</p>
              ) : filteredEntries.length === 0 ? (
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
                </div>
              ))}
            </section>

            {/* Page 2 – Rules */}
            <section class="settings-page slider-page">
              {loading ? (
                <p class="loading-text">Loading...</p>
              ) : filteredRules.length === 0 ? (
                <p class="no-history">No rules</p>
              ) : filteredRules.map((rule) => (
                <div key={rule.id} class="list-item row-center-gap">
                  <div class="flex-1">
                    {rule.type && (
                      <span class={`rule-type badge badge-${rule.type}`}>{rule.type}</span>
                    )}
                    <p class="item-path">{rule.path}</p>
                  </div>
                  <button
                    onClick={() => setConfirmInfo({
                      kind: 'deleteRule',
                      id: rule.id,
                      title: 'Remove rule',
                      message: `Remove rule for:\n${rule.path}`,
                      confirmLabel: 'Remove',
                      variant: 'danger',
                      onConfirm: () => deleteRule(rule.id),
                    })}
                    title="Remove rule"
                    class="icon-btn btn-danger"
                  >
                    <IconTrash />
                  </button>
                </div>
              ))}
            </section>

            {/* Page 3 – Quarantine */}
            <section class="settings-page slider-page">
              {loading ? (
                <p class="loading-text">Loading...</p>
              ) : filteredQuarantine.length === 0 ? (
                <p class="no-history">No quarantined files</p>
              ) : filteredQuarantine.map((file) => (
                <div key={file.id} class="list-item row-center-gap">
                  <div class="flex-1">
                    <p class="item-title">
                      {file.originalFileName}
                    </p>
                    <p class="item-path">
                      {file.originalFilePath}
                    </p>
                  </div>
                  <div class="actions-row">
                    <button
                      onClick={() => setConfirmInfo({
                        kind: 'restoreFile',
                        id: file.id,
                        title: 'Restore file',
                        message: `Restore "${file.originalFileName}" to its original location?`,
                        confirmLabel: 'Restore',
                        variant: 'teal',
                        onConfirm: () => restoreFile(file.id),
                      })}
                      title="Restore file"
                      class="icon-btn btn-teal"
                    >
                      <IconRestore />
                    </button>
                    <button
                      onClick={() => setConfirmInfo({
                        kind: 'deleteQuarantine',
                        id: file.id,
                        title: 'Delete permanently',
                        message: `"${file.originalFileName}" will be permanently deleted and cannot be recovered.`,
                        confirmLabel: 'Delete',
                        variant: 'danger',
                        onConfirm: () => deleteQuarantine(file.id),
                      })}
                      title="Delete permanently"
                      class="icon-btn btn-danger"
                    >
                      <IconTrash />
                    </button>
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

      {/* Confirm modal */}
      {confirmInfo && (
        <div class="confirm-modal-backdrop" onClick={() => setConfirmInfo(null)}>
          <div class="confirm-modal" onClick={(e: any) => e.stopPropagation()}>
            <div class={`confirm-modal-icon confirm-modal-icon-${confirmInfo.variant}`}>
              {confirmInfo.variant === 'teal' ? <IconRestore /> : <IconWarning />}
            </div>
            <p class="confirm-modal-title">{confirmInfo.title}</p>
            <p class="confirm-modal-message">{confirmInfo.message}</p>
            <div class="confirm-modal-actions">
              <button class="confirm-modal-btn confirm-modal-btn-cancel" onClick={() => setConfirmInfo(null)}>
                Cancel
              </button>
              <button
                class={`confirm-modal-btn confirm-modal-btn-${confirmInfo.variant}`}
                onClick={handleConfirm}
              >
                {confirmInfo.confirmLabel}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
