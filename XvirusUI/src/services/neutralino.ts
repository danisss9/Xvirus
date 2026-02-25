import Neutralino from '@neutralinojs/lib';
import { ThreatPayload } from '../model/ThreatPayload';

// Neutralino injects these globals before the app JS runs
declare const NL_PATH: string;

// ---------------------------------------------------------------------------
// Server-push event types (C# backend → SSE → webview)
// ---------------------------------------------------------------------------

export type ServerEventType =
  | { type: 'updating'; message: string }
  | { type: 'update-complete'; message: string }
  | { type: 'threat'; payload: ThreatPayload };

type ServerEventHandler = (event: ServerEventType) => void;
const _serverEventHandlers: ServerEventHandler[] = [];

/** Subscribe to server-push events from the C# backend. Returns an unsubscribe function. */
export function onServerEvent(handler: ServerEventHandler): () => void {
  _serverEventHandlers.push(handler);
  return () => {
    const i = _serverEventHandlers.indexOf(handler);
    if (i !== -1) _serverEventHandlers.splice(i, 1);
  };
}

function dispatchServerEvent(event: ServerEventType) {
  _serverEventHandlers.forEach((h) => h(event));
}

// ---------------------------------------------------------------------------
// Window controls
// ---------------------------------------------------------------------------

export async function minimizeWindow() {
  await Neutralino.window.minimize();
}

/** Hide the window — keeps the app alive in the system tray. */
export async function closeWindow() {
  await Neutralino.window.hide();
}

export async function showNotification(title: string, body: string) {
  await Neutralino.os.showNotification(title, body);
}

export async function getFilePath(): Promise<string | undefined> {
  const files = await Neutralino.os.showOpenDialog('Select file', {
    multiSelections: false,
  });
  return files?.[0];
}

// ---------------------------------------------------------------------------
// SSE subscription to C# backend (mirrors the old bun/index.ts logic)
// ---------------------------------------------------------------------------

const SERVICE_BASE_URL = 'http://localhost:5236';

async function subscribeToServiceEvents(): Promise<void> {
  while (true) {
    try {
      const response = await fetch(`${SERVICE_BASE_URL}/events`);
      if (!response.body) throw new Error('SSE response has no body');

      const reader = response.body.getReader();
      const decoder = new TextDecoder();
      let buffer = '';

      while (true) {
        const { done, value } = await reader.read();
        if (done) break;

        buffer += decoder.decode(value, { stream: true });

        // SSE events are delimited by a blank line (\n\n)
        const parts = buffer.split('\n\n');
        buffer = parts.pop() ?? '';

        for (const part of parts) {
          const eventMatch = part.match(/^event:\s*(.+)/m);
          const dataMatch = part.match(/^data:\s*(.+)/m);
          if (!eventMatch || !dataMatch) continue;

          const eventType = eventMatch[1].trim();
          const rawData = dataMatch[1].trim();
          let payload: Record<string, unknown> = {};
          try {
            payload = JSON.parse(rawData);
          } catch {
            /* ignore malformed */
          }

          if (eventType === 'updating') {
            Neutralino.os.showNotification(
              'Xvirus',
              (payload.message as string) || 'Checking for updates…',
            );
            dispatchServerEvent({ type: 'updating', message: (payload.message as string) ?? '' });
          } else if (eventType === 'update-complete') {
            Neutralino.os.showNotification(
              'Xvirus',
              (payload.message as string) || 'Update check complete.',
            );
            dispatchServerEvent({
              type: 'update-complete',
              message: (payload.message as string) ?? '',
            });
          } else if (eventType === 'threat') {
            const isQuarantined =
              payload.action === 'quarantined' || payload.action === 'quarantine-pending-reboot';

            if (payload.showNotification) {
              if (isQuarantined) {
                const status =
                  payload.action === 'quarantine-pending-reboot'
                    ? 'will be quarantined on next reboot'
                    : 'has been quarantined';
                Neutralino.os.showNotification(
                  'Xvirus – Threat Removed',
                  `${(payload.fileName as string) ?? 'A threat'} ${status}.`,
                );
              } else {
                // Process suspended — show alert in UI and bring window forward
                dispatchServerEvent({
                  type: 'threat',
                  payload: payload as unknown as ThreatPayload,
                });
                await Neutralino.window.show();
                await Neutralino.window.focus();
              }
            }
          }
        }
      }
    } catch (err) {
      console.error('SSE connection to XvirusService lost, retrying in 5 s:', err);
    }

    await new Promise<void>((resolve) => setTimeout(resolve, 5000));
  }
}

// ---------------------------------------------------------------------------
// System tray
// ---------------------------------------------------------------------------

function setupTray(appTitle: string) {
  // NL_PATH is the application root; the icon lives in the dist output folder
  Neutralino.os.setTray({
    icon: '/browser/icon.png',
    menuItems: [
      { id: 'open', text: `Open ${appTitle}` },
      { id: '__sep', text: '-' },
      { id: 'quit', text: 'Quit' },
    ],
  });

  Neutralino.events.on('trayMenuItemClicked', async (event: CustomEvent) => {
    const id = (event.detail as { id?: string })?.id ?? '';
    if (id === 'open' || id === '') {
      await Neutralino.window.show();
      await Neutralino.window.focus();
    } else if (id === 'quit') {
      await Neutralino.app.exit();
    }
  });
}

// ---------------------------------------------------------------------------
// Initialization — called once from App.tsx on mount
// ---------------------------------------------------------------------------

export async function initializeWindow() {
  // Position at bottom-right of the primary display (mirrors old bun/index.ts)
  const win = await Neutralino.window.getSize();
  const monitor = (await Neutralino.computer.getDisplays())[0];
  const x = monitor.resolution.width - win.width!;
  const y = monitor.resolution.height - win.height! - 60; // 60px up from the bottom edge to avoid the taskbar
  await Neutralino.window.move(x, y);

  // Clicking the OS close button hides the window rather than quitting
  Neutralino.events.on('windowClose', async () => {
    await Neutralino.window.hide();
  });

  // Make the header bar draggable, excluding the window control buttons
  await Neutralino.window.setDraggableRegion('app-header', {
    exclude: ['window-controls'],
  });

  // Read the app title from the build config and set up the tray
  const config = (await Neutralino.app.getConfig()) as { modes?: { window?: { title?: string } } };
  const appTitle = config?.modes?.window?.title ?? 'Xvirus';
  setupTray(appTitle);

  // Start the SSE loop in the background (never awaited)
  subscribeToServiceEvents();
}
