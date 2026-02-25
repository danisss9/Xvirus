import { QuarantineEntry } from '../model/QuarantineEntry';

const baseUrl = 'http://localhost:5236';

export async function fetchQuarantine(): Promise<QuarantineEntry[]> {
  const res = await fetch(`${baseUrl}/quarantine`);
  if (!res.ok) throw new Error('Failed to fetch quarantine');
  return res.json();
}

export async function deleteQuarantine(id: string): Promise<void> {
  const res = await fetch(`${baseUrl}/quarantine/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error('Failed to delete quaranted file');
}

export async function restoreQuarantine(id: string): Promise<void> {
  const res = await fetch(`${baseUrl}/quarantine/${id}/restore`, { method: 'POST' });
  if (!res.ok) throw new Error('Failed to restore quarantined file');
}
