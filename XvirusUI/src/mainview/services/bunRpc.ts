import { Electroview, RPCSchema } from 'electrobun/view';
import { ThreatPayload } from '../model/ThreatPayload';

// ---------------------------------------------------------------------------
// Server-push event types (sent from C# → bun → webview)
// ---------------------------------------------------------------------------

export type ServerEventType =
  | { type: 'updating'; message: string }
  | { type: 'update-complete'; message: string }
  | { type: 'threat'; payload: ThreatPayload };

type ServerEventHandler = (event: ServerEventType) => void;
const _serverEventHandlers: ServerEventHandler[] = [];

/** Subscribe to server-push events forwarded from the C# backend. Returns an unsubscribe function. */
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
// RPC type definitions
// ---------------------------------------------------------------------------

export type MyWebviewRPCType = {
  // functions that execute in the main bun process
  bun: RPCSchema<{
    requests: {
      closeWindow: { params: {}; response: void };
      minimizeWindow: { params: {}; response: void };
      getFilePath: { params: {}; response: string };
      showNotification: { params: { title: string; body: string }; response: void };
    };
  }>;
  // functions/messages that bun sends into this webview
  webview: RPCSchema<{
    messages: {
      serverEvent: { type: string; message: string };
    };
  }>;
};

// ---------------------------------------------------------------------------
// RPC instance
// ---------------------------------------------------------------------------

const rpc = Electroview.defineRPC<any>({
  handlers: {
    messages: {
      // bun calls mainWindow.rpc.send.serverEvent({ type, message })
      serverEvent: ({ type, message }: { type: string; message: string }) => {
        if (type === 'updating' || type === 'update-complete') {
          dispatchServerEvent({ type, message } as ServerEventType);
        } else if (type === 'threat') {
          try {
            dispatchServerEvent({ type: 'threat', payload: JSON.parse(message) });
          } catch {
            /* ignore malformed */
          }
        }
      },
    },
  },
});

const electroview = new Electroview({ rpc });

export async function initializeWindow() {}

export async function minimizeWindow() {
  await electroview.rpc!.request.minimizeWindow();
}

export async function closeWindow() {
  await electroview.rpc!.request.closeWindow();
}

export async function showNotification(title: string, body: string) {
  await electroview.rpc!.request.showNotification({ title, body });
}

export async function getFilePath() {
  return await electroview.rpc!.request.getFilePath();
}
