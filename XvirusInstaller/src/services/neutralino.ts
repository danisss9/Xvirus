import NeutralinoJs from '@neutralinojs/lib';

export const Neutralino = (() => {
  const getNeutralino = () => {
    return NeutralinoJs;
  };

  return {
    os: {
      execCommand(command: string): Promise<{ stdOut: string; stdErr: string; exitCode: number }> {
        return getNeutralino().os.execCommand(command);
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
      exit(): Promise<void> {
        return getNeutralino().app.exit();
      },
    },
  };
})();
