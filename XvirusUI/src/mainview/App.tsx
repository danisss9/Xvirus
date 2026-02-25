import { useState, useEffect } from 'preact/hooks';
import HomeView from './views/HomeView';
import ScanningView from './views/ScanningView';
import SettingsView from './views/SettingsView';
import HistoryView from './views/HistoryView';
import NetworkMonitorView from './views/NetworkMonitorView';
import AlertView from './views/AlertView';
import BottomNav from './components/BottomNav';
import WindowControls from './components/WindowControls';
import { initializeWindow, onServerEvent } from './services/bunRpc';
import { ThreatPayload } from './model/ThreatPayload';
import './styles/app.css';
import { isFirewall } from './services/env';
import { fetchSettings } from './api/settingsApi';
import { getPendingAlerts } from './api/actionsApi';

export default function App() {
  const [currentView, setCurrentView] = useState('home');
  const [scanEvents, setScanEvents] = useState([]);
  const [isScanning, setIsScanning] = useState(false);
  const [alertQueue, setAlertQueue] = useState<ThreatPayload[]>([
    {
      id: '1',
      filePath: 'C:\\temp\\malware.exe',
      fileName: 'malware.exe',
      processName: 'cmd.exe',
      processId: 1234,
      threatName: 'TestTrojan',
      malwareScore: 0.85,
      action: 'quarantine',
      showNotification: true,
      alreadyQuarantined: false,
    },
    {
      id: '2',
      filePath: 'C:\\windows\\system32\\evil.dll',
      fileName: 'evil.dll',
      processName: 'explorer.exe',
      processId: 4321,
      threatName: 'TestWorm',
      malwareScore: 0.6,
      action: 'deny',
      showNotification: false,
      alreadyQuarantined: false,
    },
  ]);

  // Navigate back to home when the alert queue is cleared
  useEffect(() => {
    if (alertQueue.length === 0 && currentView === 'alert') {
      setCurrentView('home');
    }
  }, [alertQueue.length]);

  useEffect(() => {
    initializeWindow();
    fetchSettings().then(data => {
      document.body.classList.toggle('dark', data.appSettings.darkMode);
    }).catch(() => {});

    // Load any alerts that arrived before the UI opened
    getPendingAlerts().then(pending => {
      if (pending.length > 0) {
        setAlertQueue(q => [...q, ...pending]);
        setCurrentView('alert');
      }
    }).catch(() => {});

    // Subscribe to real-time threat events
    const unsub = onServerEvent(event => {
      if (event.type === 'threat') {
        setAlertQueue(q => [...q, event.payload]);
        setCurrentView('alert');
      }
    });
    return unsub;
  }, []);

  const handleScanStart = async () => {
    setIsScanning(true);
    setScanEvents([]);
    setCurrentView('scanning');
  };

  const handleScanComplete = () => {
    setIsScanning(false);
  };

  const handleNavigate = (view: string) => {
    if (!isScanning) {
      setCurrentView(view);
    }
  };

  const dismissAlert = (id: string) => setAlertQueue(q => q.filter(t => t.id !== id));

  return (
    <div class="app-container">
      <div class="app-header">
        <h1 class="app-title">{isFirewall ? 'Xvirus Firewall' : 'Xvirus Anti-Malware'}</h1>
        <WindowControls />
      </div>
      <div class="app-content">
        {currentView === 'home' && <HomeView onScanStart={handleScanStart} onOpenNetworkMonitor={() => setCurrentView('network')} pendingAlerts={alertQueue.length} onOpenAlert={() => setCurrentView('alert')} />}
        {currentView === 'scanning' && <ScanningView onComplete={handleScanComplete} scanEvents={scanEvents} />}
        {currentView === 'settings' && <SettingsView />}
        {currentView === 'history' && <HistoryView />}
        {currentView === 'network' && <NetworkMonitorView />}
        {currentView === 'alert' && <AlertView threats={alertQueue} onDismiss={dismissAlert} />}
      </div>
      <BottomNav currentView={currentView} onNavigate={handleNavigate} isScanning={isScanning} />
    </div>
  );
}
