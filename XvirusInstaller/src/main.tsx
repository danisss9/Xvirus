import { render } from 'preact';
import App from './App';
import './styles/app.css';
import { init, events, app } from '@neutralinojs/lib';

init();
events.on('windowClose', () => app.exit(0));

const root = document.getElementById('root');
if (root) {
  render(<App />, root);
}
