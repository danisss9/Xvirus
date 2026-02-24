export interface UpdateStatusDTO {
  message: string;
  lastUpdateCheck?: string | null;
}

const baseUrl = 'http://localhost:5236';

export async function fetchLastUpdateCheck(): Promise<UpdateStatusDTO> {
  const res = await fetch(`${baseUrl}/update/lastcheck`);
  if (!res.ok) throw new Error('Failed to fetch last update check');
  return res.json();
}

export async function checkUpdates(): Promise<UpdateStatusDTO> {
  const res = await fetch(`${baseUrl}/update/check`, { method: 'POST' });
  if (!res.ok) throw new Error('Failed to check for updates');
  return res.json();
}
