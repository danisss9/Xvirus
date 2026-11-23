export interface DatabaseVersionDTO {
  aiModel: number;
  scriptAIModel: number;
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
  enableAIScriptScan: boolean;
  enableCloudScan: boolean;

  // scan filters
  onlyScanExecutables: boolean;

  // levels
  heuristicsLevel: number;
  aiLevel: number;

  // max lengths (bytes)
  maxScanLength: number | null;
  maxHeuristicsPeScanLength: number | null;
  maxHeuristicsOthersScanLength: number | null;
  maxAIScanLength: number | null;
  maxAIScriptScanLength: number | null;

  // updates
  checkSDKUpdates: boolean;
  databaseFolder: string;
  databaseVersion: DatabaseVersionDTO;
}
