import { FunctionComponent } from 'preact';
import { PRODUCT_INFO } from '../services/installer';
import Neutralino from '@neutralinojs/lib';

interface WelcomeViewProps {
  onNext: () => void;
}

const WelcomeView: FunctionComponent<WelcomeViewProps> = ({ onNext }) => {
  return (
    <div class="installer-container">
      <div class="installer-header">
        <div class="installer-title">Installation Wizard</div>
        <div class="installer-subtitle">Step 1 of 4</div>
      </div>

      <div class="installer-content">
        <div class="welcome-view">
          <div class="welcome-icon">🛡️</div>
          <div class="welcome-title">Welcome to {PRODUCT_INFO.name} Setup</div>
          <div class="welcome-description">
            This wizard will guide you through the installation of {PRODUCT_INFO.name} v{PRODUCT_INFO.version}.
          </div>
        </div>
      </div>

      <div class="installer-footer">
        <button class="btn btn-secondary" onClick={() => Neutralino.app.exit()}>
          Cancel
        </button>
        <button class="btn btn-primary" onClick={onNext}>
          Next →
        </button>
      </div>
    </div>
  );
};

export default WelcomeView;
