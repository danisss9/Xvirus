import { render } from 'preact';
import App from './App';
import './styles/app.css';

// Closing the window exits the installer (Tauri quits when its last window closes).
const root = document.getElementById('root');
if (root) {
  render(<App />, root);
}
