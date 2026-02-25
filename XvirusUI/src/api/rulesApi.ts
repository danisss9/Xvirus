import { Rule } from '../model/Rule';

const baseUrl = 'http://localhost:5236';

export async function fetchRules(): Promise<Rule[]> {
  const res = await fetch(`${baseUrl}/rules`);
  if (!res.ok) throw new Error('Failed to fetch rules');
  return res.json();
}

export async function addAllowRule(path: string): Promise<Rule> {
  const res = await fetch(`${baseUrl}/rules/allow`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  });
  if (!res.ok) throw new Error('Failed to add allow rule');
  return res.json();
}

export async function addBlockRule(path: string): Promise<Rule> {
  const res = await fetch(`${baseUrl}/rules/block`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  });
  if (!res.ok) throw new Error('Failed to add block rule');
  return res.json();
}

export async function removeRule(id: string): Promise<void> {
  const res = await fetch(`${baseUrl}/rules/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error('Failed to remove rule');
}
