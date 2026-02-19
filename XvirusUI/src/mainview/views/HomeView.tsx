import { useState } from 'preact/hooks';

export default function HomeView({ onScanStart }: {
  onScanStart: () => void;
}) {
  const [realtimeProtection, setRealtimeProtection] = useState(true);
  const [checkingUpdates, setCheckingUpdates] = useState(false);

  const handleCheckUpdates = async () => {
    setCheckingUpdates(true);
    // Simulate checking for updates
    setTimeout(() => {
      setCheckingUpdates(false);
      alert('No updates available');
    }, 2000);
  };

  const handleToggleProtection = () => {
    setRealtimeProtection(!realtimeProtection);
  };

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
        <div class="update-content">
          <div class="update-info">
            <p class="update-title">Up-To-Date</p>
            <p class="update-subtitle">Checked 5 days ago</p>
          </div>
          <div class="update-hover">
            <svg class="loop-icon" viewBox="0 0 24 24" width="36" height="36" aria-hidden="true">
              <path d="M21 12a9 9 0 1 1-3-6.7" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
              <polyline points="21 3 21 9 15 9" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" />
            </svg>
            <p class="update-hover-text">Check for Updates</p>
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
