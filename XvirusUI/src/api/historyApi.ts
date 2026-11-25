import { HistoryEntry } from '../model/HistoryEntry';
import { SERVICE_BASE_URL } from '../services/env';

export async function fetchHistory(): Promise<HistoryEntry[]> {
  const res = await fetch(`${SERVICE_BASE_URL}/history`);
  if (!res.ok) throw new Error('Failed to fetch history');
  return res.json();
}

export async function clearHistory(): Promise<void> {
  const res = await fetch(`${SERVICE_BASE_URL}/history`, { method: 'DELETE' });
  if (!res.ok) throw new Error('Failed to clear history');
}
