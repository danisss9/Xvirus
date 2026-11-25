export interface NetworkConnection {
  protocol: string;
  localAddress: string;
  remoteAddress: string;
  remoteDomain?: string;
  state: string;
  pid: number;
  fileName: string;
  filePath: string;
  score: number;
}
