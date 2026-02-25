import { render } from 'preact';
import App from './App';
import { init } from "@neutralinojs/lib"

render(<App />, document.getElementById('app')!);

init();