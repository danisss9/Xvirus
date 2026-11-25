import { Rule, RuleKind } from '../model/Rule';
import { SERVICE_BASE_URL } from '../services/env';

export async function fetchRules(): Promise<Rule[]> {
  const res = await fetch(`${SERVICE_BASE_URL}/rules`);
  if (!res.ok) throw new Error('Failed to fetch rules');
  return res.json();
}

export async function createRule(payload: {
  kind: RuleKind;
  path: string;
  type: 'allow' | 'block';
  protocol?: string;
  direction?: string;
  profile?: string;
}): Promise<Rule> {
  const res = await fetch(`${SERVICE_BASE_URL}/rules`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  });
  const data = await res.json();
  if (!res.ok) throw new Error(data?.error || 'Failed to create rule');
  return data;
}

export async function addAllowRule(path: string): Promise<Rule> {
  const res = await fetch(`${SERVICE_BASE_URL}/rules/allow`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  });
  if (!res.ok) throw new Error('Failed to add allow rule');
  return res.json();
}

export async function addBlockRule(path: string): Promise<Rule> {
  const res = await fetch(`${SERVICE_BASE_URL}/rules/block`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ path }),
  });
  if (!res.ok) throw new Error('Failed to add block rule');
  return res.json();
}

export async function removeRule(id: string): Promise<void> {
  const res = await fetch(`${SERVICE_BASE_URL}/rules/${id}`, { method: 'DELETE' });
  if (!res.ok) throw new Error('Failed to remove rule');
}
