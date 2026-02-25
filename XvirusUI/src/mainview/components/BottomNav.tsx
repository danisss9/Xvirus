export default function BottomNav({ currentView, onNavigate, isScanning }: {
  currentView: string;
  onNavigate: (view: string) => void;
  isScanning: boolean;
}) {
  const getNavLabels = () => {
    switch (currentView) {
      case 'home':
        return { left: { label: 'History', icon: 'history' }, right: { label: 'Settings', icon: 'settings' } };
      case 'scanning':
        return { left: { label: 'History', icon: 'history' }, right: { label: 'Settings', icon: 'settings' } };
      case 'history':
        return { left: { label: 'Home', icon: 'home' }, right: { label: 'Settings', icon: 'settings' } };
      case 'settings':
        return { left: { label: 'History', icon: 'history' }, right: { label: 'Home', icon: 'home' } };
      case 'network':
        return { left: { label: 'Home', icon: 'home' }, right: { label: 'Settings', icon: 'settings' } };
      default:
        return { left: { label: '', icon: '' }, right: { label: '', icon: '' } };
    }
  };

  const getIconSVG = (iconType: string) => {
    switch (iconType) {
      case 'home':
        return <svg class="nav-icon" viewBox="0 0 24 24"><path d="M10 20v-6h4v6h5v-8h3L12 3 2 12h3v8z" fill="currentColor"/></svg>;
      case 'history':
        return <svg class="nav-icon" viewBox="0 0 24 24"><path d="M9 11H7v2h2v-2zm4 0h-2v2h2v-2zm4 0h-2v2h2v-2zm2-7h-1V2h-2v2H8V2H6v2H5c-1.11 0-1.99.9-1.99 2L3 20c0 1.1.89 2 2 2h14c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2zm0 16H5V9h14v11z" fill="currentColor"/></svg>;
      case 'settings':
        return <svg class="nav-icon" viewBox="0 0 24 24"><path d="M19.14 12.94c.04-.3.06-.61.06-.94 0-.32-.02-.64-.07-.94l2.03-1.58c.18-.14.23-.41.12-.64l-1.92-3.32c-.12-.22-.37-.29-.59-.22l-2.39.96c-.5-.38-1.03-.7-1.62-.94l-.36-2.54c-.04-.24-.24-.41-.48-.41h-3.84c-.24 0-.43.17-.47.41l-.36 2.54c-.59.24-1.13.57-1.62.94l-2.39-.96c-.23-.09-.47 0-.59.22L2.74 8.87c-.12.22-.07.49.12.64l2.03 1.58c-.05.3-.07.62-.07.94 0 .33.02.64.07.94l-2.03 1.58c-.18.14-.23.41-.12.64l1.92 3.32c.12.22.37.29.59.22l2.39-.96c.5.38 1.03.7 1.62.94l.36 2.54c.05.24.24.41.48.41h3.84c.24 0 .44-.17.47-.41l.36-2.54c.59-.24 1.13-.56 1.62-.94l2.39.96c.23.09.47 0 .59-.22l1.92-3.32c.12-.22.07-.49-.12-.64l-2.01-1.58zM12 15.6c-1.98 0-3.6-1.62-3.6-3.6s1.62-3.6 3.6-3.6 3.6 1.62 3.6 3.6-1.62 3.6-3.6 3.6z" fill="currentColor"/></svg>;
      default:
        return null;
    }
  };

  const { left, right } = getNavLabels();

  return (
    <nav class="bottom-nav">
      <button 
        class="nav-item"
        onClick={() => onNavigate(left.label.toLowerCase())}
        disabled={isScanning}
        title={left.label}
      >
        {getIconSVG(left.icon)}
        <span class="nav-label">{left.label}</span>
      </button>
      <button 
        class="nav-item"
        onClick={() => onNavigate(right.label.toLowerCase())}
        disabled={isScanning}
        title={right.label}
      >
        {getIconSVG(right.icon)}
        <span class="nav-label">{right.label}</span>
      </button>
    </nav>
  );
}
