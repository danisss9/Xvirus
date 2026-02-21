import { SettingsResponseDTO } from '../model/SettingsResponseDTO';

const baseUrl = 'http://localhost:5236';

export async function fetchSettings(): Promise<SettingsResponseDTO> {
  const res = await fetch(`${baseUrl}/settings`);
  if (!res.ok) throw new Error('Failed to fetch settings');
  return res.json();
}

export async function saveSettings(payload: SettingsResponseDTO): Promise<void> {
  const res = await fetch(`${baseUrl}/settings`, {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  if (!res.ok) throw new Error('Failed to save settings');
}
