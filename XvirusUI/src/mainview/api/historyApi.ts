import { HistoryEntry } from '../model/HistoryEntry';

const baseUrl = 'http://localhost:5236';

export async function fetchHistory(): Promise<HistoryEntry[]> {
  const res = await fetch(`${baseUrl}/history`);
  if (!res.ok) throw new Error('Failed to fetch history');
  return res.json();
}

export async function clearHistory(): Promise<void> {
  const res = await fetch(`${baseUrl}/history`, { method: 'DELETE' });
  if (!res.ok) throw new Error('Failed to clear history');
}
