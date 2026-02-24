import { useState, useEffect } from 'preact/hooks';
import { fetchLastUpdateCheck, checkUpdates } from '../api/updateApi';
import { showNotification } from '../services/bunRpc';
import { fetchSettings, saveSettings } from '../api/settingsApi';
import { isFirewall } from '../services/env';

function isWithin7Days(dateStr: string): boolean {
  const diffMs = Date.now() - new Date(dateStr).getTime();
  return diffMs <= 7 * 24 * 60 * 60 * 1000;
}

export default function HomeView({ onScanStart, onOpenNetworkMonitor }: {
  onScanStart: () => void;
  onOpenNetworkMonitor?: () => void;
}) {
  const [realtimeProtection, setRealtimeProtection] = useState(true);
  const [checkingUpdates, setCheckingUpdates] = useState(false);
  const [lastChecked, setLastChecked] = useState<string | null>(null);

  const handleCheckUpdates = async () => {
    setCheckingUpdates(true);
    try {
      const res = await checkUpdates();
      setLastChecked(res.lastUpdateCheck || null);
      await showNotification('Update Check', res.message);
    } catch (e) {
      console.error(e);
      await showNotification('Update Check', 'Update check failed');
    } finally {
      setCheckingUpdates(false);
    }
  };

  const handleEnableProtection = async () => {
    try {
      const s = await fetchSettings();
      s.appSettings.realTimeProtection = true;
      await saveSettings(s);
      setRealtimeProtection(true);
    } catch (e) {
      console.error(e);
    }
  };

  useEffect(() => {
    const loadData = async () => {
      try {
        const [updateRes, settingsRes] = await Promise.all([
          fetchLastUpdateCheck(),
          fetchSettings(),
        ]);
        setLastChecked(updateRes.lastUpdateCheck || null);
        setRealtimeProtection(settingsRes.appSettings.realTimeProtection);
      } catch (e) {
        console.error(e);
         setRealtimeProtection(false);
      }
    };
    loadData();
  }, []);

  const updatedRecently = lastChecked !== null && isWithin7Days(lastChecked);
  const isProtected = realtimeProtection && (lastChecked !== null && updatedRecently);
  const needsUpdate = !updatedRecently;

  let primaryLabel: string;
  let primaryAction: () => void;
  if (!realtimeProtection) {
    primaryLabel = 'Enable Protection';
    primaryAction = handleEnableProtection;
  } else if (needsUpdate) {
    primaryLabel = 'Check for Updates';
    primaryAction = handleCheckUpdates;
  } else if (isFirewall) {
    primaryLabel = 'Open Network Monitor';
    primaryAction = onOpenNetworkMonitor ?? (() => {});
  } else {
    primaryLabel = 'Scan Now';
    primaryAction = onScanStart;
  }

  return (
    <div class="view-container home-view-container">
      <div class="card">
        <div class={`shield-icon ${isProtected ? 'shield-protected' : 'shield-unprotected'}`}>
          {isProtected ? (
            <svg viewBox="0 0 100 100" class="shield-svg">
              <path d="M50 10 L80 25 L80 50 Q80 75 50 85 Q20 75 20 50 L20 25 Z" fill="none" stroke="currentColor" stroke-width="4"/>
              <path d="M35 50 L45 60 L65 40" fill="none" stroke="currentColor" stroke-width="4" stroke-linecap="round" stroke-linejoin="round"/>
            </svg>
          ) : (
            <svg viewBox="0 0 100 100" class="shield-svg">
              <path d="M50 10 L80 25 L80 50 Q80 75 50 85 Q20 75 20 50 L20 25 Z" fill="none" stroke="currentColor" stroke-width="4"/>
              <path d="M40 38 L60 62 M60 38 L40 62" fill="none" stroke="currentColor" stroke-width="4" stroke-linecap="round"/>
            </svg>
          )}
        </div>
        <p class={`protection-text ${isProtected ? '' : 'protection-text-danger'}`}>
          {isProtected ? 'System is Protected' : 'System is not protected'}
        </p>
        <button class="btn-primary" onClick={primaryAction} disabled={checkingUpdates}>
          {checkingUpdates && primaryLabel === 'Check for Updates' ? 'Checking...' : primaryLabel}
        </button>
      </div>

      <div class="card update-card" onClick={handleCheckUpdates}>
        <div class={`update-content ${checkingUpdates ? 'updating' : ''}`}>
          <div class="update-info">
            <p class="update-title">
              {  (updatedRecently ? 'Up-To-Date' : 'Out-of-date')}
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
            onChange={async (e: any) => {
              const checked = e.currentTarget.checked;
              setRealtimeProtection(checked);
              try {
                const s = await fetchSettings();
                s.appSettings.realTimeProtection = checked;
                await saveSettings(s);
              } catch (err) {
                console.error(err);
                setRealtimeProtection(!checked);
              }
            }}
            aria-label="Real-time protection"
          />
        </div>
      </div>
    </div>
  );
}
