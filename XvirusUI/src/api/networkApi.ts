import { NetworkConnection } from '../model/NetworkConnection';
import { Rule } from '../model/Rule';
import { SERVICE_BASE_URL } from '../services/env';

export type { NetworkConnection };

export interface NetworkActionResult {
  success: boolean;
  killedConnections: number;
  rule?: Rule | null;
  message: string;
}

export async function fetchNetworkConnections(): Promise<NetworkConnection[]> {
  const res = await fetch(`${SERVICE_BASE_URL}/network/connections`);
  if (!res.ok) throw new Error('Failed to fetch network connections');
  return res.json();
}

export async function blockPid(pid: number): Promise<NetworkActionResult> {
  const res = await fetch(`${SERVICE_BASE_URL}/network/block-pid/${pid}`, {
    method: 'POST',
  });
  const data = await res.json();
  if (!res.ok) throw new Error(data?.error || 'Failed to block process');
  return data;
}

export async function blockIp(payload: {
  ip: string;
  direction?: string;
  protocol?: string;
  profile?: string;
  killConnections?: boolean;
}): Promise<NetworkActionResult> {
  const res = await fetch(`${SERVICE_BASE_URL}/network/block-ip`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  const data = await res.json();
  if (!res.ok) throw new Error(data?.error || 'Failed to block IP');
  return data;
}

export async function killConnection(payload: {
  localAddress: string;
  remoteAddress: string;
}): Promise<NetworkActionResult> {
  const res = await fetch(`${SERVICE_BASE_URL}/network/kill-connection`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  const data = await res.json();
  if (!res.ok) throw new Error(data?.error || 'Failed to terminate connection');
  return data;
}
