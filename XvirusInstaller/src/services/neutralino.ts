import NeutralinoJs, { ExecCommandOptions } from '@neutralinojs/lib';

declare const NL_PATH: string;

export const Neutralino = (() => {
  const getNeutralino = () => {
    return NeutralinoJs;
  };

  return {
    os: {
      execCommand(
        command: string,
        options?: ExecCommandOptions,
      ): Promise<{ stdOut: string; stdErr: string; exitCode: number }> {
        return getNeutralino().os.execCommand(command, options);
      },
    },
    filesystem: {
      writeFile(path: string, content: string): Promise<void> {
        return getNeutralino().filesystem.writeFile(path, content);
      },
      writeBinaryFile(path: string, bytes: ArrayBuffer): Promise<void> {
        return getNeutralino().filesystem.writeBinaryFile(path, bytes);
      },
      readFile(path: string): Promise<string> {
        return getNeutralino().filesystem.readFile(path);
      },
    },
    app: {
      async exit(): Promise<void> {
        try {
          const tmpDir = NL_PATH.replace(/\//g, '\\') + '\\.tmp';
          await getNeutralino().os.execCommand(
            `powershell -WindowStyle Hidden -Command "Start-Sleep 1; Remove-Item -LiteralPath '${tmpDir}' -Recurse -Force -ErrorAction SilentlyContinue; exit"`,
            { background: true },
          );
        } catch {
          /* ignore cleanup errors */
        }
        NeutralinoJs.app.exit();
      },
    },
  };
})();
