import { render } from 'preact';
import App from './App';
import './styles/app.css';
import { init, events } from '@neutralinojs/lib';
import { Neutralino } from './services/neutralino';

init();
events.on('windowClose', () => Neutralino.app.exit());

const root = document.getElementById('root');
if (root) {
  render(<App />, root);
}
