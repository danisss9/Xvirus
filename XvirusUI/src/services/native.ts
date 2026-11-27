import { invoke } from '@tauri-apps/api/core';
import { getCurrentWindow } from '@tauri-apps/api/window';
import { sendNotification } from '@tauri-apps/plugin-notification';
import { open } from '@tauri-apps/plugin-dialog';
import { ThreatPayload } from '../model/ThreatPayload';
import { SERVICE_BASE_URL } from './env';

/**
 * Path passed via the "Scan with Xvirus" context-menu entry (`--scan "<path>"`),
 * or null when the app was launched normally.
 */
export async function getLaunchScanPath(): Promise<string | null> {
  try {
    const args = await invoke<string[]>('launch_args');
    const i = args.indexOf('--scan');
    if (i !== -1 && i + 1 < args.length) {
      const p = args[i + 1];
      if (p && !p.startsWith('--')) return p;
    }
  } catch {
    /* not running inside Tauri */
  }
  return null;
}

// ---------------------------------------------------------------------------
// Server-push event types (C# backend → SSE → webview)
// ---------------------------------------------------------------------------

export type ServerEventType =
  | { type: 'updating'; message: string }
  | { type: 'update-complete'; message: string }
  | { type: 'threat'; payload: ThreatPayload }
  | { type: 'scan-progress'; filesScanned: number; threatsFound: number }
  | { type: 'scan-complete'; filesScanned: number; threatsFound: number; cancelled: boolean };

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
  await getCurrentWindow().minimize();
}

/** Hide the window — keeps the app alive in the system tray. */
export async function closeWindow() {
  await getCurrentWindow().hide();
}

/** Show, restore and focus the window. */
export async function showWindow() {
  await invoke('show_window');
}

export async function showNotification(title: string, body: string) {
  sendNotification({ title, body });
}

export async function getFilePath(): Promise<string | undefined> {
  const file = await open({
    title: 'Select file',
    multiple: false,
    directory: false,
  });
  return file ?? undefined;
}

// ---------------------------------------------------------------------------
// SSE subscription to C# backend (mirrors the old bun/index.ts logic)
// ---------------------------------------------------------------------------

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

          if (eventType === 'scan-progress') {
            dispatchServerEvent({
              type: 'scan-progress',
              filesScanned: (payload.filesScanned as number) ?? 0,
              threatsFound: (payload.threatsFound as number) ?? 0,
            });
          } else if (eventType === 'scan-complete') {
            dispatchServerEvent({
              type: 'scan-complete',
              filesScanned: (payload.filesScanned as number) ?? 0,
              threatsFound: (payload.threatsFound as number) ?? 0,
              cancelled: (payload.cancelled as boolean) ?? false,
            });
          } else if (eventType === 'updating') {
            showNotification(
              'Xvirus',
              (payload.message as string) || 'Checking for updates…',
            );
            dispatchServerEvent({ type: 'updating', message: (payload.message as string) ?? '' });
          } else if (eventType === 'update-complete') {
            showNotification(
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

            if (isQuarantined) {
              if (payload.showNotification) {
                const status =
                  payload.action === 'quarantine-pending-reboot'
                    ? 'will be quarantined on next reboot'
                    : 'has been quarantined';
                showNotification(
                  'Xvirus – Threat Removed',
                  `${(payload.fileName as string) ?? 'A threat'} ${status}.`,
                );
              }
            } else {
              // Process suspended / awaiting user decision — always show AlertView so
              // suspended processes are never left frozen when OS notifications are off.
              dispatchServerEvent({
                type: 'threat',
                payload: payload as unknown as ThreatPayload,
              });
              if (payload.showNotification) {
                showNotification(
                  'Xvirus – Threat Detected',
                  `${(payload.fileName as string) ?? 'A suspicious file'} requires your action.`,
                );
              }
              await showWindow();
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
// Initialization — called once from App.tsx on mount
// ---------------------------------------------------------------------------

// Window placement (bottom-right), hide-on-close, and the system tray are set up natively in
// src-tauri/src/main.rs. The header is draggable via `data-tauri-drag-region` in App.tsx.
export async function initializeWindow() {
  // Start the SSE loop in the background (never awaited)
  subscribeToServiceEvents();
}
