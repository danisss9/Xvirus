export type ThreatPayload = {
  id: string;
  filePath: string;
  fileName: string;
  processName: string;
  processId: number;
  threatName: string;
  malwareScore: number;
  action: string;
  showNotification: boolean;
  alreadyQuarantined: boolean;
};
