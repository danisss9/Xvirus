export type RuleKind = 'program' | 'ip' | 'port' | 'domain';

export interface Rule {
  id: string;
  kind?: RuleKind;
  path: string;
  type?: 'allow' | 'block';
  protocol?: string;
  direction?: string;
  profile?: string;
}
