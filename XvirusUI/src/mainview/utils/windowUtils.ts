import { Electroview, RPCSchema } from 'electrobun/view';

export type MyWebviewRPCType = {
  // functions that execute in the main process
  bun: RPCSchema<{
    requests: {
      closeWindow: {
        params: {};
        response: void;
      };
      minimizeWindow: {
        params: {};
        response: void;
      };
    };
  }>;
  // functions that execute in the browser context
  webview: RPCSchema<{
    /* requests: {
      someWebviewFunction: {
        params: {
          a: number;
          b: number;
        };
        response: number;
      };
    };
    messages: {
      logToWebview: {
        msg: string;
      };
    }; */
  }>;
};

const rpc = Electroview.defineRPC<any>({
  handlers: {
    requests: {
      /*   someWebviewFunction: ({ a, b }) => {
        document.body.innerHTML += `bun asked me to do math with ${a} and ${b}\n`;
        return a + b;
      }, */
    },
  },
});
const electroview = new Electroview({ rpc });

export async function initializeWindow() {}

export async function minimizeWindow() {
  await electroview.rpc!.request.minimizeWindow();
}

export async function closeWindow() {
  electroview.rpc!.request.closeWindow();
}
