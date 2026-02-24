export interface QuarantineEntry {
  id: string;
  originalFileName: string;
  originalFilePath: string;
  quarantinedFileName: string;
  originalAttributes: string;
  originalCreationTime: string; // ISO date string
  originalLastWriteTime: string; // ISO date string
  originalLastAccessTime: string; // ISO date string
}
