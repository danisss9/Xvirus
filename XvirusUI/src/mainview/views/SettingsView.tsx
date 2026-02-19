import { useState, useEffect, useRef } from 'preact/hooks';

export default function SettingsView() {
  const [settings, setSettings] = useState({
    enableSignatures: true,
    enableHeuristics: true,
    enableAIScan: true,
    checkSDKUpdates: true,
    heuristicsLevel: 4,
    aiLevel: 10,
    enableContextMenu: true,
    passwordProtection: false,
    enableLogs: true,
    onlyScanExecutables: true,
    maxExeScanLength: 20971520,
    maxOtherScanLength: 10485760,
    autoQuarantine: true
  });
  


  const [localSettings, setLocalSettings] = useState({
    scheduledScan: localStorage.getItem('scheduledScan') === 'true',
    scanInterval: localStorage.getItem('scanInterval') || 'daily',
    language: localStorage.getItem('language') || 'en',
    darkMode: localStorage.getItem('darkMode') === 'true',
    startWithWindows: localStorage.getItem('startWithWindows') === 'true'
  });

  useEffect(() => {
    // Fetch settings from backend
    const fetchSettings = async () => {
      try {
        const response = await fetch('http://localhost:5236/settings');
        const data = await response.json();
        setSettings({
          enableSignatures: data.enableSignatures,
          enableHeuristics: data.enableHeuristics,
          enableAIScan: data.enableAIScan,
          checkSDKUpdates: data.checkSDKUpdates,
          heuristicsLevel: data.heuristicsLevel,
          aiLevel: data.aiLevel,
          enableContextMenu: data.enableContextMenu ?? true,
          passwordProtection: data.passwordProtection ?? false,
          enableLogs: data.enableLogs ?? true,
          onlyScanExecutables: data.onlyScanExecutables ?? true,
          maxExeScanLength: data.maxExeScanLength ?? 20971520,
          maxOtherScanLength: data.maxOtherScanLength ?? 10485760,
          autoQuarantine: data.autoQuarantine ?? true,
          // protection settings
          behaviorProtection: data.behaviorProtection ?? true,
          cloudScan: data.cloudScan ?? false,
          networkProtection: data.networkProtection ?? true,
          selfDefense: data.selfDefense ?? true,
          showNotifications: data.showNotifications ?? true,
          threatAction: data.threatAction ?? 'auto'
        } as any);
      } catch (error) {
        console.error('Failed to fetch settings:', error);
      }
    };

    fetchSettings();
  }, []);

  const handleSettingChange = async (field: string, value: boolean | string | number) => {
    const newSettings = { ...settings, [field]: value };
    setSettings(newSettings);

    // Save to backend
    try {
      await fetch('http://localhost:5236/settings', {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          enableSignatures: newSettings.enableSignatures,
          enableHeuristics: newSettings.enableHeuristics,
          enableAIScan: newSettings.enableAIScan,
          checkSDKUpdates: newSettings.checkSDKUpdates,
          heuristicsLevel: newSettings.heuristicsLevel,
          aiLevel: newSettings.aiLevel,
          enableContextMenu: newSettings.enableContextMenu,
          passwordProtection: newSettings.passwordProtection,
          enableLogs: newSettings.enableLogs,
          onlyScanExecutables: newSettings.onlyScanExecutables,
          maxExeScanLength: newSettings.maxExeScanLength,
          maxOtherScanLength: newSettings.maxOtherScanLength,
          autoQuarantine: newSettings.autoQuarantine,
          // protection settings
          behaviorProtection: (newSettings as any).behaviorProtection,
          cloudScan: (newSettings as any).cloudScan,
          networkProtection: (newSettings as any).networkProtection,
          selfDefense: (newSettings as any).selfDefense,
          showNotifications: (newSettings as any).showNotifications,
          threatAction: (newSettings as any).threatAction || 'auto',
          databaseFolder: 'Database',
          databaseVersion: { aiModel: 0, mainDB: 0, dailyDB: 0, whiteDB: 0, dailywlDB: 0, heurDB: 0, heurDB2: 0, malvendorDB: 0 },
          maxScanLength: null,
          maxHeuristicsPeScanLength: 20971520,
          maxHeuristicsOthersScanLength: 10485760,
          maxAIScanLength: 20971520
        })
      });
    } catch (error) {
      console.error('Failed to save settings:', error);
    }
  };

  const handleLocalSettingChange = (field: string, value: string | boolean) => {
    const newLocalSettings = { ...localSettings, [field]: value };
    setLocalSettings(newLocalSettings);
    localStorage.setItem(field, value.toString());
  };

  const isRealTimeProtectionEnabled = settings.enableSignatures && settings.enableHeuristics && settings.enableAIScan;

  // Slider state for three pages: 0 = General, 1 = Scan, 2 = Protection
  const [page, setPage] = useState(0);
  const sliderRef = useRef<HTMLDivElement | null>(null);
  const viewportRef = useRef<HTMLDivElement | null>(null);
  const startX = useRef<number | null>(null);
  const currentTranslate = useRef(0);
  const isDragging = useRef(false);

  const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));

  const goToPage = (index: number) => {
    const idx = clamp(index, 0, 2);
    setPage(idx);
  };

  const onPointerDown = (e: any) => {
    startX.current = e.touches ? e.touches[0].clientX : e.clientX;
    isDragging.current = true;
    if (sliderRef.current) sliderRef.current.style.transition = 'none';
  };

  const onPointerMove = (e: any) => {
    if (!isDragging.current || startX.current == null) return;
    const clientX = e.touches ? e.touches[0].clientX : e.clientX;
    const dx = clientX - startX.current;
    const width = viewportRef.current?.clientWidth || 1;
    const base = -page * width;
    currentTranslate.current = base + dx;
    if (sliderRef.current) sliderRef.current.style.transform = `translateX(${currentTranslate.current}px)`;
  };

  const onPointerUp = (e: any) => {
    if (!isDragging.current) return;
    isDragging.current = false;
    const width = viewportRef.current?.clientWidth || 1;
    const dx = currentTranslate.current + page * width; // offset from base
    const threshold = width * 0.18;
    if (dx < -threshold && page < 2) {
      goToPage(page + 1);
    } else if (dx > threshold && page > 0) {
      goToPage(page - 1);
    } else {
      goToPage(page);
    }
    if (sliderRef.current) sliderRef.current.style.transition = '';
    startX.current = null;
  };

  useEffect(() => {
    // ensure slider snaps to page when page changes
    if (sliderRef.current && viewportRef.current) {
      const width = viewportRef.current.clientWidth;
      sliderRef.current.style.transition = 'transform 320ms cubic-bezier(.2,.9,.2,1)';
      sliderRef.current.style.transform = `translateX(${-page * width}px)`;
    }
  }, [page]);

  const [isPaused, setIsPaused] = useState(false);

  // autoplay demo: advance pages every 4s unless paused
  useEffect(() => {
    if (isPaused) return;
    const id = setInterval(() => {
      setPage((p) => (p < 2 ? p + 1 : 0));
    }, 4000);
    return () => clearInterval(id);
  }, [isPaused]);
  const pageTitles = ['General Settings', 'Scan Settings', 'Protection Settings'];
  const pageTitle = pageTitles[page] || 'Settings';

  return (
    <div class="view-container">
      <div
        class="card settings-card"
        onMouseEnter={() => setIsPaused(true)}
        onMouseLeave={() => setIsPaused(false)}
      >
        <h2 class="title">{pageTitle}</h2>

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
            <section class="settings-page">
              <div class="setting-item">
                <label class="setting-label">Language</label>
                <select
                  class="dropdown"
                  value={localSettings.language}
                  onChange={(e: any) => handleLocalSettingChange('language', e.currentTarget.value)}
                >
                  <option value="en">English</option>
                </select>
              </div>
              <div class="setting-item">
                <label class="setting-label">Dark Mode</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={localSettings.darkMode}
                  onChange={(e: any) => handleLocalSettingChange('darkMode', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Start with Windows</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={localSettings.startWithWindows}
                  onChange={(e: any) => handleLocalSettingChange('startWithWindows', e.currentTarget.checked)}
                />
              </div>
              <div class="setting-item">
                <label class="setting-label">Automatic Updates</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={settings.checkSDKUpdates}
                  onChange={(e: any) => handleSettingChange('checkSDKUpdates', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Enable context-menu scan</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={settings.enableContextMenu}
                  onChange={(e: any) => handleSettingChange('enableContextMenu', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Password protection</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={settings.passwordProtection}
                  onChange={(e: any) => handleSettingChange('passwordProtection', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Enable logging</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={settings.enableLogs}
                  onChange={(e: any) => handleSettingChange('enableLogs', e.currentTarget.checked)}
                />
              </div>
            </section>

            <section class="settings-page">
              <div class="setting-item">
                <label class="setting-label">Heuristics Level</label>
                <select
                  class="dropdown"
                  value={String(settings.heuristicsLevel)}
                  onChange={(e: any) => handleSettingChange('heuristicsLevel', Number(e.currentTarget.value))}
                >
                  <option value="5">High</option>
                  <option value="3">Medium</option>
                  <option value="1">Low</option>
                  <option value="0">Off</option>
                </select>
              </div>

              <div class="setting-item">
                <label class="setting-label">AI Level</label>
                <select
                  class="dropdown"
                  value={String(settings.aiLevel)}
                  onChange={(e: any) => handleSettingChange('aiLevel', Number(e.currentTarget.value))}
                >
                  <option value="80">High</option>
                  <option value="50">Medium</option>
                  <option value="20">Low</option>
                  <option value="0">Off</option>
                </select>
              </div>

              <div class="setting-item">
                <label class="setting-label">Only scan executable files</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={settings.onlyScanExecutables}
                  onChange={(e: any) => handleSettingChange('onlyScanExecutables', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Max EXE scan length</label>
                <select
                  class="dropdown"
                  value={String(settings.maxExeScanLength)}
                  onChange={(e: any) => handleSettingChange('maxExeScanLength', Number(e.currentTarget.value))}
                >
                  <option value="0">Unlimited</option>
                  <option value="1048576">1 MB</option>
                  <option value="5242880">5 MB</option>
                  <option value="10485760">10 MB</option>
                  <option value="20971520">20 MB</option>
                  <option value="52428800">50 MB</option>
                </select>
              </div>

              <div class="setting-item">
                <label class="setting-label">Max Other files scan length</label>
                <select
                  class="dropdown"
                  value={String(settings.maxOtherScanLength)}
                  onChange={(e: any) => handleSettingChange('maxOtherScanLength', Number(e.currentTarget.value))}
                >
                  <option value="0">Unlimited</option>
                  <option value="1048576">1 MB</option>
                  <option value="5242880">5 MB</option>
                  <option value="10485760">10 MB</option>
                  <option value="20971520">20 MB</option>
                  <option value="52428800">50 MB</option>
                </select>
              </div>

              <div class="setting-item">
                <label class="setting-label">Automatically quarantine detected threats</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={settings.autoQuarantine}
                  onChange={(e: any) => handleSettingChange('autoQuarantine', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Scheduled scan</label>
                <select
                  class="dropdown"
                  value={localSettings.scanInterval}
                  onChange={(e: any) => {
                    const val = e.currentTarget.value;
                    handleLocalSettingChange('scanInterval', val);
                    handleLocalSettingChange('scheduledScan', val !== 'off');
                  }}
                >
                  <option value="off">Off</option>
                  <option value="daily">Daily</option>
                  <option value="weekly">Weekly</option>
                  <option value="monthly">Monthly</option>
                </select>
              </div>
            </section>

            <section class="settings-page">
              <div class="setting-item">
                <label class="setting-label">Real-time Protection</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={isRealTimeProtectionEnabled}
                  onChange={(e: any) => {
                    const enabled = e.currentTarget.checked;
                    handleSettingChange('enableSignatures', enabled);
                    handleSettingChange('enableHeuristics', enabled);
                    handleSettingChange('enableAIScan', enabled);
                  }}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">If a threat is found</label>
                <select
                  class="dropdown"
                  value={(settings as any).threatAction || 'auto'}
                  onChange={(e: any) => handleSettingChange('threatAction', e.currentTarget.value)}
                >
                  <option value="auto">Automatically quarantine</option>
                  <option value="ask">Ask me</option>
                </select>
              </div>

              <div class="setting-item">
                <label class="setting-label">Behavior protection</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={(settings as any).behaviorProtection ?? true}
                  onChange={(e: any) => handleSettingChange('behaviorProtection', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Scan files in the cloud</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={(settings as any).cloudScan ?? false}
                  onChange={(e: any) => handleSettingChange('cloudScan', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Network protection</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={(settings as any).networkProtection ?? true}
                  onChange={(e: any) => handleSettingChange('networkProtection', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Self-defense (protect app from tampering)</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={(settings as any).selfDefense ?? true}
                  onChange={(e: any) => handleSettingChange('selfDefense', e.currentTarget.checked)}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Show notifications</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={(settings as any).showNotifications ?? true}
                  onChange={(e: any) => handleSettingChange('showNotifications', e.currentTarget.checked)}
                />
              </div>
            </section>
          </div>
        </div>

        <div class="settings-dots">
          {[0, 1, 2].map((i) => (
            <button
              class={`dot ${i === page ? 'active' : ''}`}
              aria-label={`Go to page ${i + 1}`}
              onClick={() => goToPage(i)}
            />
          ))}
        </div>
      </div>
    </div>
  );
}
