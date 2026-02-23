export interface Rule {
  id: string;
  path: string;
  type?: 'allow' | 'block';
}
