import { SettingsResponseDTO } from '../model/SettingsResponseDTO';
import { SERVICE_BASE_URL } from '../services/env';

export async function fetchSettings(): Promise<SettingsResponseDTO> {
  const res = await fetch(`${SERVICE_BASE_URL}/settings`);
  if (!res.ok) throw new Error('Failed to fetch settings');
  return res.json();
}

export async function saveSettings(payload: SettingsResponseDTO): Promise<void> {
  const res = await fetch(`${SERVICE_BASE_URL}/settings`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!res.ok) throw new Error('Failed to save settings');
}
