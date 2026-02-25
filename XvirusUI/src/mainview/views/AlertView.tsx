import { useState, useEffect } from 'preact/hooks';
import { ThreatPayload } from '../model/ThreatPayload';
import { submitAction } from '../api/actionsApi';

interface AlertViewProps {
  threats: ThreatPayload[];
  onDismiss: (id: string) => void;
}

export default function AlertView({ threats, onDismiss }: AlertViewProps) {
  const [index, setIndex] = useState(0);
  const [loading, setLoading] = useState(false);
  const [rememberDecision, setRememberDecision] = useState(false);

  const safeIndex = Math.min(index, threats.length - 1);
  const threat = threats[safeIndex];
  const total = threats.length;

  // Reset per-alert state when the visible threat changes
  useEffect(() => {
    setLoading(false);
    setRememberDecision(false);
  }, [threat?.id]);

  const handle = async (action: 'quarantine' | 'allow') => {
    setLoading(true);
    try {
      await submitAction(threat.id, action, rememberDecision);
    } catch { /* best-effort */ }
    onDismiss(threat.id);
  };

  const scorePercent = Math.round(threat.malwareScore * 100);

  return (
    <div class="view-container">
      <div class="card alert-view-card">

        {total > 1 && (
          <div class="alert-nav">
            <button
              class="alert-nav-btn"
              onClick={() => setIndex(i => Math.max(0, i - 1))}
              disabled={safeIndex === 0}
            >
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="15 18 9 12 15 6"/>
              </svg>
            </button>
            <span class="alert-nav-label">{safeIndex + 1} / {total} threats</span>
            <button
              class="alert-nav-btn"
              onClick={() => setIndex(i => Math.min(total - 1, i + 1))}
              disabled={safeIndex === total - 1}
            >
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round">
                <polyline points="9 18 15 12 9 6"/>
              </svg>
            </button>
          </div>
        )}

        <div class="alert-view-icon">
          <svg width="40" height="40" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M10.29 3.86L1.82 18a2 2 0 0 0 1.71 3h16.94a2 2 0 0 0 1.71-3L13.71 3.86a2 2 0 0 0-3.42 0z"/>
            <line x1="12" y1="9" x2="12" y2="13"/>
            <line x1="12" y1="17" x2="12.01" y2="17"/>
          </svg>
        </div>

        <h2 class="alert-view-title">Threat Detected</h2>
        <p class="alert-view-subtitle">A suspicious file requires your attention</p>

        <div class="alert-modal-details">
          <div class="alert-modal-row">
            <span class="alert-modal-label">File</span>
            <span class="alert-modal-value alert-modal-value-path" title={threat.filePath}>{threat.fileName}</span>
          </div>
          <div class="alert-modal-row">
            <span class="alert-modal-label">Process</span>
            <span class="alert-modal-value">{threat.processName} (PID {threat.processId})</span>
          </div>
          <div class="alert-modal-row">
            <span class="alert-modal-label">Threat</span>
            <span class="alert-modal-value">{threat.threatName || 'Unknown'}</span>
          </div>
          <div class="alert-modal-row">
            <span class="alert-modal-label">Score</span>
            <span class="alert-modal-value alert-modal-score">{scorePercent}%</span>
          </div>
          <div class="alert-modal-row">
            <span class="alert-modal-label">Status</span>
            <span class="alert-modal-value">{threat.action === 'suspended' ? 'Process suspended' : 'Quarantine failed — process suspended'}</span>
          </div>
        </div>

        <label class="alert-remember-label">
          <input
            type="checkbox"
            class="alert-remember-check"
            checked={rememberDecision}
            onChange={(e: any) => setRememberDecision(e.currentTarget.checked)}
          />
          Remember decision for this file
        </label>

        <div class="alert-view-actions">
          <button
            class="confirm-modal-btn confirm-modal-btn-teal"
            onClick={() => handle('allow')}
            disabled={loading}
          >
            Allow
          </button>
          <button
            class="confirm-modal-btn confirm-modal-btn-danger"
            onClick={() => handle('quarantine')}
            disabled={loading}
          >
            {loading ? 'Working…' : 'Quarantine'}
          </button>
        </div>

      </div>
    </div>
  );
}
