import { minimizeWindow, closeWindow } from '../services/neutralino';

export default function WindowControls() {
  const handleMinimize = async () => {
    await minimizeWindow();
  };

  const handleClose = async () => {
    await closeWindow();
  };

  return (
    <div id="window-controls" class="window-controls">
      <button 
        class="window-btn minimize-btn" 
        onClick={handleMinimize}
        title="Minimize"
        aria-label="Minimize window"
      >
        <svg viewBox="0 0 24 24" class="window-icon">
          <line x1="6" y1="12" x2="18" y2="12" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
        </svg>
      </button>
      <button 
        class="window-btn close-btn" 
        onClick={handleClose}
        title="Close"
        aria-label="Close window"
      >
        <svg viewBox="0 0 24 24" class="window-icon">
          <line x1="6" y1="6" x2="18" y2="18" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
          <line x1="18" y1="6" x2="6" y2="18" stroke="currentColor" strokeWidth="2" strokeLinecap="round"/>
        </svg>
      </button>
    </div>
  );
}
