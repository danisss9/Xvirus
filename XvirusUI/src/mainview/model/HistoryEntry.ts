export interface HistoryEntry {
  type: string;
  timestamp: number;
  details: string;
  filesScanned: number;
  threatsFound: number;
}
