import { FunctionComponent } from 'preact';
import { PRODUCT_INFO } from '../services/installer';
import { Neutralino } from '../services/neutralino';

interface DoneViewProps {
  success: boolean;
  error?: string;
}

const DoneView: FunctionComponent<DoneViewProps> = ({ success, error }) => {
  const handleLaunch = async () => {
    try {
      const installPath = `C:\\Program Files\\${PRODUCT_INFO.installFolder}`;
      await Neutralino.os.execCommand(`start "" "${installPath}\\Xvirus.exe"`);
    } catch (e) {
      console.error('Failed to launch app:', e);
    }
    await Neutralino.app.exit();
  };

  const handleClose = async () => {
    await Neutralino.app.exit();
  };

  return (
    <div class="installer-container">
      <div class="installer-header">
        <div class="installer-title">Installation Complete</div>
        <div class="installer-subtitle">Step 4 of 4</div>
      </div>

      <div class="installer-content">
        {success ? (
          <div class="done-view">
            <div class="done-icon">✓</div>
            <div class="done-title">{PRODUCT_INFO.name} installed successfully</div>
            <div class="done-message">
              Installation has completed successfully. You can now launch {PRODUCT_INFO.name}
              {' '}or close this window.
            </div>
          </div>
        ) : (
          <div class="done-view">
            <div class={`done-icon done-error-icon`}>✕</div>
            <div class="done-title">Installation failed</div>
            <div class="done-message">
              An error occurred during installation. Please check the error details below.
            </div>
            {error && <div class="done-error">{error}</div>}
          </div>
        )}
      </div>

      <div class="installer-footer">
        <button class="btn btn-secondary" onClick={handleClose}>
          Close
        </button>
        {success && (
          <button class="btn btn-primary" onClick={handleLaunch}>
            Launch Application
          </button>
        )}
      </div>
    </div>
  );
};

export default DoneView;
