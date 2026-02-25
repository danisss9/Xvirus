export interface DatabaseVersionDTO {
  aiModel: number;
  mainDB: number;
  dailyDB: number;
  whiteDB: number;
  dailywlDB: number;
  heurDB: number;
  heurDB2: number;
  malvendorDB: number;
}

export interface SettingsDTO {
  // scan engines
  enableSignatures: boolean;
  enableHeuristics: boolean;
  enableAIScan: boolean;

  // levels
  heuristicsLevel: number;
  aiLevel: number;

  // max lengths (bytes)
  maxScanLength: number | null;
  maxHeuristicsPeScanLength: number | null;
  maxHeuristicsOthersScanLength: number | null;
  maxAIScanLength: number | null;

  // updates
  checkSDKUpdates: boolean;
  databaseFolder: string;
  databaseVersion: DatabaseVersionDTO;
}
