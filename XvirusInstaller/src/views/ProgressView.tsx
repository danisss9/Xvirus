import { FunctionComponent } from 'preact';
import { InstallConfig, InstallProgress } from '../model/InstallConfig';

interface ProgressViewProps {
  config: InstallConfig;
  progress: InstallProgress;
  onComplete: (success: boolean, error?: string) => void;
}

const ProgressView: FunctionComponent<ProgressViewProps> = ({ config, progress, onComplete }) => {
  return (
    <div class="installer-container">
      <div class="installer-header">
        <div class="installer-title">Installing</div>
        <div class="installer-subtitle">Step 3 of 4 - Please wait while {PRODUCT_INFO.name} is being installed</div>
      </div>

      <div class="installer-content">
        <div class="progress-view">
          <div class="progress-step">{progress.step}</div>

          <div class="progress-bar-container">
            <div
              class="progress-bar-fill"
              style={{ width: `${Math.min(100, Math.max(0, progress.progress))}%` }}
            />
          </div>

          <div class="progress-percentage">{Math.round(progress.progress)}%</div>

          {progress.done && !progress.success && (
            <div style={{ marginTop: '16px', textAlign: 'center', color: 'var(--error)' }}>
              Installation failed: {progress.error}
            </div>
          )}
        </div>
      </div>

      <div class="installer-footer">
        {progress.done ? (
          <button
            class="btn btn-primary"
            onClick={() => {
              if (progress.success) {
                onComplete(true);
              } else {
                onComplete(false, progress.error);
              }
            }}
          >
            {progress.success ? 'Next →' : 'Back'}
          </button>
        ) : (
          <div style={{ flex: 1 }} />
        )}
      </div>
    </div>
  );
};

// Import PRODUCT_INFO for display
import { PRODUCT_INFO } from '../services/installer';

export default ProgressView;
