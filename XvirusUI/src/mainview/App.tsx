import { useState, useEffect } from 'preact/hooks';
import HomeView from './views/HomeView';
import ScanningView from './views/ScanningView';
import SettingsView from './views/SettingsView';
import HistoryView from './views/HistoryView';
import BottomNav from './components/BottomNav';
import WindowControls from './components/WindowControls';
import { initializeWindow } from './services/bunRpc';
import './styles/app.css';
import { isFirewall } from './services/env';

export default function App() {
  const [currentView, setCurrentView] = useState('home');
  const [scanEvents, setScanEvents] = useState([]);
  const [isScanning, setIsScanning] = useState(false);

  useEffect(() => {
    // Initialize window position on app mount
    initializeWindow();
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

  return (
    <div class="app-container">
      <div class="app-header">
        <h1 class="app-title">{isFirewall ? 'Xvirus Firewall' : 'Xvirus Anti-Malware'}</h1>
        <WindowControls />
      </div>
      <div class="app-content">
        {currentView === 'home' && <HomeView onScanStart={handleScanStart} />}
        {currentView === 'scanning' && <ScanningView onComplete={handleScanComplete} scanEvents={scanEvents} />}
        {currentView === 'settings' && <SettingsView />}
        {currentView === 'history' && <HistoryView />}
      </div>
      <BottomNav currentView={currentView} onNavigate={handleNavigate} isScanning={isScanning} />
    </div>
  );
}
