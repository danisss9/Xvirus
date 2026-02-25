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

const baseUrl = 'http://localhost:5236';

export async function fetchNetworkConnections(): Promise<NetworkConnection[]> {
  const res = await fetch(`${baseUrl}/network/connections`);
  if (!res.ok) throw new Error('Failed to fetch network connections');
  return res.json();
}
