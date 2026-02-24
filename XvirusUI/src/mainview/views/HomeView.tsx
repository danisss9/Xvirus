import { useState, useEffect } from 'preact/hooks';
import { fetchLastUpdateCheck, checkUpdates } from '../api/updateApi';
import { showNotification } from '../api/bunRpc';

export default function HomeView({ onScanStart }: {
  onScanStart: () => void;
}) {
  const [realtimeProtection, setRealtimeProtection] = useState(true);
  const [checkingUpdates, setCheckingUpdates] = useState(false);
  const [lastChecked, setLastChecked] = useState<string | null>(null);
  const [updateMessage, setUpdateMessage] = useState<string | null>(null);

  const handleCheckUpdates = async () => {
    setCheckingUpdates(true);
    try {
      const res = await checkUpdates();
      setUpdateMessage(res.message);
      setLastChecked(res.lastUpdateCheck || null);
      await showNotification('Update Check', res.message);
    } catch (e) {
      console.error(e);
      await showNotification('Update Check', 'Update check failed');
    } finally {
      setCheckingUpdates(false);
    }
  };

  useEffect(() => {
    const loadLast = async () => {
      try {
        const res = await fetchLastUpdateCheck();
        setLastChecked(res.lastUpdateCheck || null);
      } catch (e) {
        console.error(e);
      }
    };
    loadLast();
  }, []);

  return (
    <div class="view-container home-view-container">
      <div class="card">
        <div class="shield-icon">
          <svg viewBox="0 0 100 100" class="shield-svg">
            <path d="M50 10 L80 25 L80 50 Q80 75 50 85 Q20 75 20 50 L20 25 Z" fill="none" stroke="currentColor" stroke-width="2"/>
            <path d="M35 50 L45 60 L65 40" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"/>
          </svg>
        </div>
        <p class="protection-text">System is protected</p>
        <button class="btn-primary" onClick={onScanStart}>Scan Now</button>
      </div>

      <div class="card update-card" onClick={handleCheckUpdates}>
        <div class={`update-content ${checkingUpdates ? 'updating' : ''}`}>
          <div class="update-info">
            <p class="update-title">
              { updateMessage || 'Up-To-Date'}
            </p>
            <p class="update-subtitle">
              {lastChecked ? `Checked ${new Date(lastChecked).toLocaleString()}` : 'Never checked'}
            </p>
          </div>
          <div class="update-hover" style={{ opacity: checkingUpdates ? 1 : undefined }}>
            <svg
              class="loop-icon"
              viewBox="0 0 24 24"
              width="36"
              height="36"
              aria-hidden="true"
              style={{ animation: checkingUpdates ? 'spin 2s linear infinite' : 'none' }}
            >
              <path d="M21 12a9 9 0 1 1-3-6.7" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
              <polyline points="21 3 21 9 15 9" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
            <p class="update-hover-text">
              {checkingUpdates ? 'Checking for updates...' : 'Check for Updates'}
            </p>
          </div>
        </div>
      </div>

      <div class="card protection-card">
        <div class="protection-content">
          <div class="protection-info">
            <p class="protection-info-title">Real-Time Protection</p>
            <p class="protection-info-subtitle">{realtimeProtection ? 'Enabled' : 'Disabled'}</p>
          </div>
          <input
            type="checkbox"
            class="toggle-switch"
            checked={realtimeProtection}
            onChange={(e: any) => setRealtimeProtection(e.currentTarget.checked)}
            aria-label="Real-time protection"
          />
        </div>
      </div>
    </div>
  );
}
