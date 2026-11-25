import { UpdateStatusDTO } from '../model/UpdateStatusDTO';
import { SERVICE_BASE_URL } from '../services/env';

export async function fetchLastUpdateCheck(): Promise<UpdateStatusDTO> {
  const res = await fetch(`${SERVICE_BASE_URL}/update/lastcheck`);
  if (!res.ok) throw new Error('Failed to fetch last update check');
  return res.json();
}

export async function checkUpdates(): Promise<UpdateStatusDTO> {
  const res = await fetch(`${SERVICE_BASE_URL}/update/check`, { method: 'POST' });
  if (!res.ok) throw new Error('Failed to check for updates');
  return res.json();
}
