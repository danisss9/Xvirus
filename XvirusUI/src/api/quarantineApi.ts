import { QuarantineEntry } from '../model/QuarantineEntry';
import { SERVICE_BASE_URL } from '../services/env';

export async function fetchQuarantine(): Promise<QuarantineEntry[]> {
  const res = await fetch(`${SERVICE_BASE_URL}/quarantine`);
  if (!res.ok) throw new Error('Failed to fetch quarantine');
  return res.json();
}

export async function deleteQuarantine(id: string): Promise<void> {
  const res = await fetch(`${SERVICE_BASE_URL}/quarantine/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error('Failed to delete quarantined file');
}

export async function restoreQuarantine(id: string): Promise<void> {
  const res = await fetch(`${SERVICE_BASE_URL}/quarantine/${id}/restore`, { method: 'POST' });
  if (!res.ok) throw new Error('Failed to restore quarantined file');
}
