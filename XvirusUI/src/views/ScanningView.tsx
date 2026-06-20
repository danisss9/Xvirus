import { useState, useEffect } from 'preact/hooks';
import { onServerEvent } from '../services/neutralino';

export default function ScanningView({ onComplete, scanPath = 'C:\\' }: {
  onComplete: () => void;
  scanEvents?: any;
  scanPath?: string;
}) {
  const [filesScanned, setFilesScanned] = useState(0);
  const [threatsFound, setThreatsFound] = useState(0);
  const [isRunning, setIsRunning] = useState(true);

  // Live progress: the backend streams scan-progress / scan-complete over SSE.
  useEffect(() => {
    const unsub = onServerEvent(event => {
      if (event.type === 'scan-progress') {
        setFilesScanned(event.filesScanned);
        setThreatsFound(event.threatsFound);
      }
    });
    return unsub;
  }, []);

  useEffect(() => {
    // Start a full scan; the POST resolves only when the scan finishes/cancels.
    const startScan = async () => {
      try {
        const response = await fetch('http://localhost:5236/scan', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ path: scanPath })
        });
        const data = await response.json();
        setFilesScanned(data.filesScanned || 0);
        setThreatsFound(data.threatsFound || 0);
      } catch (error) {
        console.error('Scan error:', error);
      } finally {
        setIsRunning(false);
        onComplete();
      }
    };

    startScan();
  }, [onComplete, scanPath]);

  const handleStop = async () => {
    setIsRunning(false);
    try {
      await fetch('http://localhost:5236/scan/cancel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ path: scanPath })
      });
    } catch (error) {
      console.error('Failed to cancel scan:', error);
    }
    onComplete();
  };

  return (
    <div class="view-container">
      <div class="card">
        <h2 class="title">Scanning</h2>
        <div class="scan-progress">
          <svg class="progress-ring" viewBox="0 0 120 120">
            <circle cx="60" cy="60" r="50" class="progress-bg"/>
            <circle cx="60" cy="60" r="50" class="progress-circle" style={`stroke-dasharray: ${Math.min(filesScanned * 3.14, 314)}px 314px`}/>
          </svg>
          <div class="scan-info">
            <p class="scan-label">Scanning...</p>
            <p class="scan-counter">{filesScanned} files</p>
            {threatsFound > 0 && <p class="threat-counter">{threatsFound} threats</p>}
          </div>
        </div>
        <button class="btn-secondary" onClick={handleStop} disabled={!isRunning}>Stop</button>
      </div>
    </div>
  );
}
