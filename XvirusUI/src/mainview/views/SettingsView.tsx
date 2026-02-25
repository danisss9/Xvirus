import { useState, useEffect, useRef } from 'preact/hooks';
import { SettingsDTO } from '../model/SettingsDTO';
import { AppSettingsDTO } from '../model/AppSettingsDTO';
import { SettingsResponseDTO } from '../model/SettingsResponseDTO';
import { fetchSettings, saveSettings } from '../api/settingsApi';
import { isFirewall } from '../services/env';

export default function SettingsView() {
// split state to match backend DTOs
  const [settings, setSettings] = useState<SettingsDTO>({
    enableSignatures: true,
    enableHeuristics: true,
    enableAIScan: true,
    heuristicsLevel: 4,
    aiLevel: 10,
    maxScanLength: null,
    maxHeuristicsPeScanLength: 20971520,
    maxHeuristicsOthersScanLength: 10485760,
    maxAIScanLength: 20971520,
    checkSDKUpdates: true,
    databaseFolder: 'Database',
    databaseVersion: { aiModel: 0, mainDB: 0, dailyDB: 0, whiteDB: 0, dailywlDB: 0, heurDB: 0, heurDB2: 0, malvendorDB: 0 }
  });

  const [appSettings, setAppSettings] = useState<AppSettingsDTO>({
    language: 'en',
    darkMode: true,
    startWithWindows: true,
    enableContextMenu: false,
    passwordProtection: false,
    enableLogs: false,
    onlyScanExecutables: true,
    autoQuarantine: false,
    scheduledScan: 'off',
    realTimeProtection: true,
    threatAction: 'ask',
    behaviorProtection: false,
    cloudScan: false,
    networkProtection: true,
    selfDefense: false,
    showNotifications: true
  });

  useEffect(() => {
    // Fetch structured settings from backend via helper
    const get = async () => {
      try {
        const data = await fetchSettings();
        setSettings(data.settings);
        setAppSettings(data.appSettings);
      } catch (error) {
        console.error('Failed to fetch settings:', error);
      }
    };

    get();
  }, []);

  const handleSettingChange = (field: keyof SettingsDTO, value: boolean | string | number | null) => {
    setSettings(prev => ({ ...prev, [field]: value } as any));
  };

  const handleAppSettingChange = (field: keyof AppSettingsDTO, value: string | boolean) => {
    setAppSettings(prev => ({ ...prev, [field]: value } as any));
    if (field === 'darkMode') {
      document.body.classList.toggle('dark', value as boolean);
    }
  };

  const saveAllSettings = async () => {
    try {
      const payload: SettingsResponseDTO = { settings, appSettings };
      await saveSettings(payload);
    } catch (error) {
      console.error('Failed to save settings:', error);
    }
  };

  // appSettings replaces previous localSettings
  // the saveAllSettings helper will be used when any value changes
  

  const isRealTimeProtectionEnabled = appSettings.realTimeProtection;

  // Slider: 2 pages in firewall mode (General, Protection), 3 in AM mode (General, Scan, Protection)
  const pageTitles = isFirewall
    ? ['General Settings', 'Protection Settings']
    : ['General Settings', 'Scan Settings', 'Protection Settings'];
  const maxPage = pageTitles.length - 1;

  const [page, setPage] = useState(0);
  const sliderRef = useRef<HTMLDivElement | null>(null);
  const viewportRef = useRef<HTMLDivElement | null>(null);
  const startX = useRef<number | null>(null);
  const currentTranslate = useRef(0);
  const isDragging = useRef(false);

  const dragThreshold = 8;

  const clamp = (v: number, a: number, b: number) => Math.max(a, Math.min(b, v));

  const goToPage = (index: number) => {
    setPage(clamp(index, 0, maxPage));
  };

  const onPointerDown = (e: any) => {
    startX.current = e.touches ? e.touches[0].clientX : e.clientX;
    // don't immediately treat it as dragging; wait until movement exceeds threshold
    isDragging.current = false;
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

  const onPointerUp = (_e: any) => {
    if (!isDragging.current) {
      // it was just a tap or insignificant movement
      startX.current = null;
      return;
    }
    isDragging.current = false;
    const width = viewportRef.current?.clientWidth || 1;
    const dx = currentTranslate.current + page * width; // offset from base
    const threshold = width * 0.18;
    if (dx < -threshold && page < maxPage) {
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

  const pageTitle = pageTitles[page] || 'Settings';

  return (
    <div class="view-container">
      <div
        class="card settings-card"
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
          <div class="settings-slider" ref={sliderRef} style={{ width: `${pageTitles.length * 100}%` }}>
            <section class="settings-page">
              <div class="setting-item">
                <label class="setting-label">Language</label>
                <select
                  class="dropdown"
                  value={appSettings.language}
                  onChange={(e: any) => { handleAppSettingChange('language', e.currentTarget.value); saveAllSettings(); }}
                >
                  <option value="en">English</option>
                </select>
              </div>
              <div class="setting-item">
                <label class="setting-label">Dark Mode</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={appSettings.darkMode}
                  onChange={(e: any) => { handleAppSettingChange('darkMode', e.currentTarget.checked); saveAllSettings(); }}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Start with Windows</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={appSettings.startWithWindows}
                  onChange={(e: any) => { handleAppSettingChange('startWithWindows', e.currentTarget.checked); saveAllSettings(); }}
                />
              </div>
              <div class="setting-item">
                <label class="setting-label">Automatic Updates</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={settings.checkSDKUpdates}
                  onChange={(e: any) => { handleSettingChange('checkSDKUpdates', e.currentTarget.checked); saveAllSettings(); }}
                />
              </div>

              {!isFirewall && (
                <div class="setting-item">
                  <label class="setting-label">Enable context-menu scan</label>
                  <input
                    type="checkbox"
                    class="toggle-switch"
                    checked={appSettings.enableContextMenu}
                    onChange={(e: any) => { handleAppSettingChange('enableContextMenu', e.currentTarget.checked); saveAllSettings(); }}
                  />
                </div>
              )}

              {!isFirewall && (
                <div class="setting-item">
                  <label class="setting-label">Password protection</label>
                  <input
                    type="checkbox"
                    class="toggle-switch"
                    checked={appSettings.passwordProtection}
                    onChange={(e: any) => { handleAppSettingChange('passwordProtection', e.currentTarget.checked); saveAllSettings(); }}
                  />
                </div>
              )}

              {!isFirewall && (
                <div class="setting-item">
                  <label class="setting-label">Enable logging</label>
                  <input
                    type="checkbox"
                    class="toggle-switch"
                    checked={appSettings.enableLogs}
                    onChange={(e: any) => { handleAppSettingChange('enableLogs', e.currentTarget.checked); saveAllSettings(); }}
                  />
                </div>
              )}
            </section>

            {!isFirewall && (<section class="settings-page">
              <div class="setting-item">
                <label class="setting-label">Heuristics Level</label>
                <select
                  class="dropdown"
                  value={String(settings.heuristicsLevel)}
                  onChange={(e: any) => { handleSettingChange('heuristicsLevel', Number(e.currentTarget.value)); saveAllSettings(); }}
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
                  onChange={(e: any) => { handleSettingChange('aiLevel', Number(e.currentTarget.value)); saveAllSettings(); }}
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
                  checked={appSettings.onlyScanExecutables}
                  onChange={(e: any) => { handleAppSettingChange('onlyScanExecutables', e.currentTarget.checked); saveAllSettings(); }}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Max EXE scan length</label>
                <select
                  class="dropdown"
                  value={String(settings.maxHeuristicsPeScanLength)}
                  onChange={(e: any) => { handleSettingChange('maxHeuristicsPeScanLength', Number(e.currentTarget.value)); saveAllSettings(); }}
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
                  value={String(settings.maxHeuristicsOthersScanLength)}
                  onChange={(e: any) => { handleSettingChange('maxHeuristicsOthersScanLength', Number(e.currentTarget.value)); saveAllSettings(); }}
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
                  checked={appSettings.autoQuarantine}
                  onChange={(e: any) => { handleAppSettingChange('autoQuarantine', e.currentTarget.checked); saveAllSettings(); }}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">Scheduled scan</label>
                <select
                  class="dropdown"
                  value={appSettings.scheduledScan}
                  onChange={(e: any) => {
                    const val = e.currentTarget.value as AppSettingsDTO['scheduledScan'];
                    handleAppSettingChange('scheduledScan', val);
                    saveAllSettings();
                  }}
                >
                  <option value="off">Off</option>
                  <option value="daily">Daily</option>
                  <option value="weekly">Weekly</option>
                  <option value="monthly">Monthly</option>
                </select>
              </div>
            </section>)}

            <section class="settings-page">
              <div class="setting-item">
                <label class="setting-label">Process Protection</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={isRealTimeProtectionEnabled}
                  onChange={(e: any) => {
                    const enabled = e.currentTarget.checked;
                    handleAppSettingChange('realTimeProtection', enabled);
                    // keep engines in sync as before
                    handleSettingChange('enableSignatures', enabled);
                    handleSettingChange('enableHeuristics', enabled);
                    handleSettingChange('enableAIScan', enabled);
                    saveAllSettings();
                  }}
                />
              </div>

              <div class="setting-item">
                <label class="setting-label">If a threat is found</label>
                <select
                  class="dropdown"
                  value={appSettings.threatAction || 'auto'}
                  onChange={(e: any) => { handleAppSettingChange('threatAction', e.currentTarget.value); saveAllSettings(); }}
                >
                  <option value="auto">Automatically quarantine</option>
                  <option value="ask">Ask me</option>
                </select>
              </div>

              {!isFirewall && (
                <div class="setting-item">
                  <label class="setting-label">Behavior protection</label>
                  <input
                    type="checkbox"
                    class="toggle-switch"
                    checked={appSettings.behaviorProtection ?? true}
                    onChange={(e: any) => { handleAppSettingChange('behaviorProtection', e.currentTarget.checked); saveAllSettings(); }}
                  />
                </div>
              )}

              {!isFirewall && (
                <div class="setting-item">
                  <label class="setting-label">Scan files in the cloud</label>
                  <input
                    type="checkbox"
                    class="toggle-switch"
                    checked={appSettings.cloudScan ?? false}
                    onChange={(e: any) => { handleAppSettingChange('cloudScan', e.currentTarget.checked); saveAllSettings(); }}
                  />
                </div>
              )}

              <div class="setting-item">
                <label class="setting-label">Network protection</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={appSettings.networkProtection ?? true}
                  onChange={(e: any) => { handleAppSettingChange('networkProtection', e.currentTarget.checked); saveAllSettings(); }}
                />
              </div>

              {!isFirewall && (
                <div class="setting-item">
                  <label class="setting-label">Self-defense (protect app from tampering)</label>
                  <input
                    type="checkbox"
                    class="toggle-switch"
                    checked={appSettings.selfDefense ?? true}
                    onChange={(e: any) => { handleAppSettingChange('selfDefense', e.currentTarget.checked); saveAllSettings(); }}
                  />
                </div>
              )}

              <div class="setting-item">
                <label class="setting-label">Show notifications</label>
                <input
                  type="checkbox"
                  class="toggle-switch"
                  checked={appSettings.showNotifications ?? true}
                  onChange={(e: any) => { handleAppSettingChange('showNotifications', e.currentTarget.checked); saveAllSettings(); }}
                />
              </div>
            </section>
          </div>
        </div>

        <div class="settings-dots">
          {pageTitles.map((title, i) => (
            <button
              key={i}
              class={`dot ${i === page ? 'active' : ''}`}
              aria-label={title}
              onClick={() => goToPage(i)}
            />
          ))}
        </div>
      </div>
    </div>
  );
}
