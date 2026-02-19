import { BrowserWindow } from 'electrobun/bun';

export async function minimizeWindow() {
  const window = BrowserWindow.getFocusedWindow();
  if (window) {
    await window.minimize();
  }
}

export async function closeWindow() {
  const window = BrowserWindow.getFocusedWindow();
  if (window) {
    await window.close();
  }
}
