import { FunctionComponent } from 'preact';
import { useState } from 'preact/hooks';
import { InstallConfig } from '../model/InstallConfig';
import { PRODUCT_INFO } from '../services/installer';

interface ConfigViewProps {
  onNext: (config: InstallConfig) => void;
  onBack: () => void;
}

const ConfigView: FunctionComponent<ConfigViewProps> = ({ onNext, onBack }) => {
  const defaultPath = `C:\\Program Files\\${PRODUCT_INFO.installFolder}`;

  const [installPath, setInstallPath] = useState(defaultPath);
  const [desktopShortcut, setDesktopShortcut] = useState(true);
  const [startMenuShortcut, setStartMenuShortcut] = useState(true);

  const handleNext = () => {
    if (!installPath.trim()) {
      alert('Please enter an installation path');
      return;
    }

    onNext({
      installPath: installPath.trim(),
      desktopShortcut,
      startMenuShortcut
    });
  };

  return (
    <div class="installer-container">
      <div class="installer-header">
        <div class="installer-title">Configuration</div>
        <div class="installer-subtitle">Step 2 of 4 - Choose installation options</div>
      </div>

      <div class="installer-content">
        <div class="config-view">
          <div class="config-section">
            <label class="config-label">Installation Directory</label>
            <input
              type="text"
              class="config-input"
              value={installPath}
              onInput={(e) => setInstallPath((e.target as HTMLInputElement).value)}
              placeholder="C:\Program Files\..."
            />
            <div class="config-hint">
              This is where {PRODUCT_INFO.name} will be installed. Make sure you have
              sufficient disk space.
            </div>
          </div>

          <div class="config-section">
            <div style={{ fontSize: '12px', fontWeight: '500', color: 'var(--text-secondary)', marginTop: '12px' }}>
              Shortcuts
            </div>
            <label class="config-checkbox">
              <input
                type="checkbox"
                checked={desktopShortcut}
                onChange={(e) => setDesktopShortcut((e.target as HTMLInputElement).checked)}
              />
              <span>Create desktop shortcut</span>
            </label>
            <label class="config-checkbox">
              <input
                type="checkbox"
                checked={startMenuShortcut}
                onChange={(e) => setStartMenuShortcut((e.target as HTMLInputElement).checked)}
              />
              <span>Create Start Menu shortcut</span>
            </label>
          </div>
        </div>
      </div>

      <div class="installer-footer">
        <button class="btn btn-secondary" onClick={onBack}>
          ← Back
        </button>
        <button class="btn btn-primary" onClick={handleNext}>
          Install
        </button>
      </div>
    </div>
  );
};

export default ConfigView;
