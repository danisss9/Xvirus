export interface NetworkConnection {
  protocol: string;
  localAddress: string;
  remoteAddress: string;
  state: string;
  pid: number;
  fileName: string;
  filePath: string;
  score: number;
}
