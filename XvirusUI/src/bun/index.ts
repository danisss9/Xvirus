import { BrowserWindow, Updater, Utils, Screen, BrowserView } from 'electrobun/bun';

const DEV_SERVER_PORT = 5173;
const DEV_SERVER_URL = `http://localhost:${DEV_SERVER_PORT}`;

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
const myWebviewRPC = BrowserView.defineRPC<any>({
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
        // open file dialog and return selected file path
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
    // When the browser sends a message we can handle it
    // in the main bun process
    /*     messages: {
      "*": (messageName, payload) => {
        console.log("global message handler", messageName, payload);
      },
      logToBun: (msg) => {
        console.log("Log to bun: ", msg);
      },
    }, */
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
