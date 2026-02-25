import { BrowserWindow, Updater, Utils, Screen, BrowserView, type RPCSchema } from 'electrobun/bun';

type AppRPCType = {
  bun: RPCSchema<{
    requests: {
      closeWindow: { params: {}; response: void };
      minimizeWindow: { params: {}; response: void };
      getFilePath: { params: {}; response: string };
      showNotification: { params: { title: string; body: string }; response: void };
    };
  }>;
  webview: RPCSchema<{
    messages: {
      serverEvent: { type: string; message: string };
    };
  }>;
};

const DEV_SERVER_PORT = 5173;
const DEV_SERVER_URL = `http://localhost:${DEV_SERVER_PORT}`;
const SERVICE_BASE_URL = 'http://localhost:5236';

// Check if Vite dev server is running for HMR
async function getMainViewUrl(): Promise<string> {
  const channel = await Updater.localInfo.channel();
  if (channel === 'dev') {
    try {
      await fetch(DEV_SERVER_URL, { method: 'HEAD' });
      console.log(`HMR enabled: Using Vite dev server at ${DEV_SERVER_URL}`);
      return DEV_SERVER_URL;
    } catch {
      console.log("Vite dev server not running. Run 'bun run dev:hmr' for HMR support.");
    }
  }
  return 'views://mainview/index.html';
}

// Create the main application window
const url = await getMainViewUrl();

// Setup RPC
const myWebviewRPC = BrowserView.defineRPC<AppRPCType>({
  maxRequestTime: 5000,
  handlers: {
    requests: {
      closeWindow: () => {
        mainWindow?.close();
      },
      minimizeWindow: () => {
        mainWindow?.minimize();
      },
      getFilePath: async () => {
        const filePath = await Utils.openFileDialog({
          allowsMultipleSelection: false,
          canChooseDirectory: false,
          canChooseFiles: true,
        });
        return filePath?.[0];
      },
      showNotification: ({ title, body }) => {
        Utils.showNotification({ title, body });
      },
    },
  },
});

// Get screen dimensions
const { workArea } = Screen.getPrimaryDisplay();
const { width: screenWidth, height: screenHeight } = workArea;

// Calculate bottom-right position
const windowWidth = 400;
const windowHeight = 650;
const x = screenWidth - windowWidth;
const y = screenHeight - windowHeight;

const mainWindow = new BrowserWindow({
  title: 'Xvirus Anti-Malware',
  url,
  frame: {
    width: windowWidth,
    height: windowHeight,
    x,
    y,
  },
  titleBarStyle: 'hidden',
  rpc: myWebviewRPC,
});

// Quit the app when the main window is closed
mainWindow.on('close', () => {
  Utils.quit();
});

// ---------------------------------------------------------------------------
// Subscribe to C# backend Server-Sent Events and forward them to the webview
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

        // SSE events are separated by a blank line (\n\n)
        const parts = buffer.split('\n\n');
        buffer = parts.pop() ?? '';

        for (const part of parts) {
          const eventMatch = part.match(/^event:\s*(.+)/m);
          const dataMatch = part.match(/^data:\s*(.+)/m);
          if (!eventMatch || !dataMatch) continue;

          const eventType = eventMatch[1].trim();
          const rawData = dataMatch[1].trim();
          let payload: Record<string, any> = {};
          try {
            payload = JSON.parse(rawData);
          } catch {
            /* ignore malformed */
          }

          if (eventType === 'updating') {
            Utils.showNotification({
              title: 'Xvirus',
              body: payload.message || 'Checking for updates…',
            });
            try {
              mainWindow.webview.rpc!.send.serverEvent({
                type: eventType,
                message: payload.message ?? '',
              });
            } catch {}
          } else if (eventType === 'update-complete') {
            Utils.showNotification({
              title: 'Xvirus',
              body: payload.message || 'Update check complete.',
            });
            try {
              mainWindow.webview.rpc!.send.serverEvent({
                type: eventType,
                message: payload.message ?? '',
              });
            } catch {}
          } else if (eventType === 'threat') {
            const isQuarantined =
              payload.action === 'quarantined' || payload.action === 'quarantine-pending-reboot';

            if (payload.showNotification) {
              if (isQuarantined) {
                // File is handled — show OS notification only
                const status =
                  payload.action === 'quarantine-pending-reboot'
                    ? 'will be quarantined on next reboot'
                    : 'has been quarantined';
                Utils.showNotification({
                  title: 'Xvirus – Threat Removed',
                  body: `${payload.fileName ?? 'A threat'} ${status}.`,
                });
              } else {
                // Process is suspended and needs a user decision — forward
                // the full JSON payload to Preact so AlertView can display it
                try {
                  mainWindow.webview.rpc!.send.serverEvent({ type: 'threat', message: rawData });
                  mainWindow.show();
                  mainWindow.focus();
                } catch {}
              }
            }
          }
        }
      }
    } catch (err) {
      console.error('SSE connection to XvirusService lost, retrying in 5 s:', err);
    }

    // Back-off before reconnecting
    await new Promise<void>((resolve) => setTimeout(resolve, 5000));
  }
}

// Start SSE subscription loop in the background (does not block window creation)
subscribeToServiceEvents();
