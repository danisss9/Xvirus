import { useState, useEffect } from 'preact/hooks';

export default function ScanningView({ onComplete, scanEvents }: {
  onComplete: () => void;
  scanEvents: any;
}) {
  const [filesScanned, setFilesScanned] = useState(0);
  const [threatsFound, setThreatsFound] = useState(0);
  const [isRunning, setIsRunning] = useState(true);

  useEffect(() => {
    // Start scan with default path
    const startScan = async () => {
      try {
        const response = await fetch('http://localhost:5236/scan', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ path: 'C:\\' })
        });
        const data = await response.json();
        setFilesScanned(data.filesScanned || 0);
        setThreatsFound(data.threatsFound || 0);
        setIsRunning(false);
        onComplete();
      } catch (error) {
        console.error('Scan error:', error);
        setIsRunning(false);
        onComplete();
      }
    };

    startScan();
  }, [onComplete]);

  const handleStop = () => {
    setIsRunning(false);
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
