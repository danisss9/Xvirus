import { ThreatPayload } from '../model/ThreatPayload';

const baseUrl = 'http://localhost:5236';

export async function getPendingAlerts(): Promise<ThreatPayload[]> {
  const res = await fetch(`${baseUrl}/actions/pending`);
  if (!res.ok) throw new Error('Failed to fetch pending alerts');
  return res.json();
}

export async function submitAction(
  id: string,
  action: 'quarantine' | 'allow',
  rememberDecision = false,
): Promise<void> {
  const res = await fetch(`${baseUrl}/actions/${id}`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ action, rememberDecision }),
  });
  if (!res.ok) throw new Error('Failed to submit action');
}
